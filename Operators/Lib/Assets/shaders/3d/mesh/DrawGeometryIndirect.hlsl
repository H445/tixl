cbuffer Parameters : register(b0)
{
    float4 ObjectToClipRows[4];
    float2 ViewSize;
    float LodPixels;
    uint PartCount;
    uint TilesX;
    uint UseDepth;
    uint GroupsX;
    uint Padding;
};

float4 project(float3 p)
{
    return p.x * ObjectToClipRows[0] + p.y * ObjectToClipRows[1]
         + p.z * ObjectToClipRows[2] + ObjectToClipRows[3];
}

Texture2D<float> Depth : register(t1);
Texture2DMS<float> DepthMS : register(t2);
RWStructuredBuffer<float> TileOutput : register(u1);
groupshared float tileDepth[64];

// A group reads eight adjacent 8x8 tiles. Each lane owns one column; adjacent
// lanes read adjacent texels and a single shared-memory barrier joins the columns.
void storeTile(uint2 tile, uint lane, float columnMax)
{
    tileDepth[lane] = columnMax;
    GroupMemoryBarrierWithGroupSync();
    if ((lane & 7) != 0 || tile.x >= TilesX)
        return;
    float farthest = tileDepth[lane];
    [unroll] for (uint column = 1; column < 8; column++)
        farthest = max(farthest, tileDepth[lane + column]);
    TileOutput[tile.y * TilesX + tile.x] = farthest;
}

[numthreads(64, 1, 1)]
void reduceDepth(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint2 tile = uint2(group.x * 8 + lane / 8, group.y);
    uint2 p = tile * 8 + uint2(lane & 7, 0);
    uint2 last = (uint2)ViewSize - 1;
    p.x = min(p.x, last.x);
    float farthest = Depth.Load(int3(p, 0));
    // A clear sample already attains the maximum possible hardware depth.
    if (farthest < 1)
    {
        [unroll] for (uint row = 1; row < 8; row++)
            farthest = max(farthest, Depth.Load(int3(p.x, min(p.y + row, last.y), 0)));
    }
    storeTile(tile, lane, farthest);
}

[numthreads(64, 1, 1)]
void reduceDepthMS(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint width, height, samples;
    DepthMS.GetDimensions(width, height, samples);
    uint2 tile = uint2(group.x * 8 + lane / 8, group.y);
    uint2 p = tile * 8 + uint2(lane & 7, 0);
    p.x = min(p.x, width - 1);
    float farthest = 0;
    for (uint row = 0; row < 8 && farthest < 1; row++)
        for (uint sample = 0; sample < samples; sample++)
            farthest = max(farthest, DepthMS.Load(uint2(p.x, min(p.y + row, height - 1)), sample));
    storeTile(tile, lane, farthest);
}

struct Part { float4 lower; float4 upper; uint4 ranges; };
struct IndexedCommand { uint indexCount, instanceCount, firstIndex; int vertexOffset; uint firstInstance; };
StructuredBuffer<Part> Parts : register(t0);
StructuredBuffer<float> TileMax : register(t3);
RWStructuredBuffer<IndexedCommand> Commands : register(u0);

IndexedCommand selectLod(uint id, out float2 lo, out float2 hi, out float nearest, out bool canTestDepth)
{
    Part part = Parts[id];
    lo = 1e30;
    hi = -1e30;
    nearest = 1;
    uint common = 63;
    bool crossesNear = false;
    [unroll] for (uint i = 0; i < 8; ++i)
    {
        float3 p = float3((i & 1) ? part.upper.x : part.lower.x,
                          (i & 2) ? part.upper.y : part.lower.y,
                          (i & 4) ? part.upper.z : part.lower.z);
        float4 h = project(p);
        // A part is outside only when all eight corners are beyond the same plane.
        common &= (h.x < -h.w ? 1 : 0) | (h.x > h.w ? 2 : 0)
                | (h.y < -h.w ? 4 : 0) | (h.y > h.w ? 8 : 0)
                | (h.z < 0 ? 16 : 0) | (h.z > h.w ? 32 : 0);
        crossesNear = crossesNear || h.z <= 0 || h.w <= 1e-6;
        float3 ndc = h.xyz / max(h.w, 1e-6);
        float2 pixel = (ndc.xy * float2(.5, -.5) + .5) * ViewSize;
        lo = min(lo, pixel);
        hi = max(hi, pixel);
        nearest = min(nearest, ndc.z);
    }
    bool visible = common == 0;
    canTestDepth = visible && !crossesNear;
    bool coarse = !crossesNear && max(hi.x - lo.x, hi.y - lo.y) < LodPixels && part.ranges.w > 0;
    IndexedCommand cmd;
    cmd.firstIndex = coarse ? part.ranges.z : part.ranges.x;
    cmd.indexCount = coarse ? part.ranges.w : part.ranges.y;
    cmd.instanceCount = visible ? 1 : 0;
    cmd.vertexOffset = 0;
    cmd.firstInstance = 0;
    return cmd;
}

[numthreads(64, 1, 1)]
void classify(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint id = (group.x + group.y * GroupsX) * 64 + lane;
    if (id >= PartCount) return;
    float2 lo, hi;
    float nearest;
    bool canTestDepth;
    Commands[id] = selectLod(id, lo, hi, nearest, canTestDepth);
}

groupshared uint4 testRect; // First tile x/y, width and tile count.
groupshared float nearestDepth;
groupshared uint foundVisible;
groupshared IndexedCommand selected;

[numthreads(64, 1, 1)]
void classifyDepth(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint id = group.x + group.y * GroupsX;
    if (id >= PartCount) return;
    if (lane == 0)
    {
        float2 lo, hi;
        float nearest;
        bool canTest;
        selected = selectLod(id, lo, hi, nearest, canTest);
        nearestDepth = nearest;
        testRect = 0;
        foundVisible = 0;
        if (canTest)
        {
            // Include the border pixels even when a projected edge rounds onto a tile boundary.
            uint2 first = (uint2)clamp(floor(lo - 1), 0, ViewSize - 1) / 8;
            uint2 last = (uint2)clamp(ceil(hi + 1), 0, ViewSize - 1) / 8;
            uint2 center = (first + last) / 2;
            // One uncovered tile is enough to keep the part.
            if (nearest > TileMax[center.y * TilesX + center.x] + 0.00001)
            {
                uint2 size = last - first + 1;
                testRect = uint4(first, size.x, size.x * size.y);
            }
        }
    }
    GroupMemoryBarrierWithGroupSync();
    if (testRect.w != 0)
    {
        for (uint i = lane; i < testRect.w; i += 64)
        {
            uint2 tile = testRect.xy + uint2(i % testRect.z, i / testRect.z);
            if (!(nearestDepth > TileMax[tile.y * TilesX + tile.x] + 0.00001))
            {
                InterlockedOr(foundVisible, 1);
                break;
            }
        }
        GroupMemoryBarrierWithGroupSync();
        if (lane == 0) selected.instanceCount = foundVisible;
    }
    if (lane == 0) Commands[id] = selected;
}

StructuredBuffer<float4> Vertices : register(t4);
struct Varyings { float4 position : SV_Position; float4 color : COLOR0; };
Varyings vsMain(uint id : SV_VertexID)
{
    Varyings o;
    o.position = project(Vertices[2 * id].xyz);
    o.color = Vertices[2 * id + 1];
    return o;
}
float4 psMain(Varyings input) : SV_Target { return input.color; }
