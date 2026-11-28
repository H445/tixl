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

void reduceTile(uint2 tile, uint lane, float value)
{
    tileDepth[lane] = value;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint step = 32; step > 0; step >>= 1)
    {
        if (lane < step)
            tileDepth[lane] = max(tileDepth[lane], tileDepth[lane + step]);
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane == 0)
        TileOutput[tile.y * TilesX + tile.x] = tileDepth[0];
}

[numthreads(8, 8, 1)]
void reduceDepth(uint3 pixel : SV_DispatchThreadID, uint3 tile : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint2 p = min(pixel.xy, (uint2)ViewSize - 1);
    reduceTile(tile.xy, lane, Depth.Load(int3(p, 0)));
}

[numthreads(8, 8, 1)]
void reduceDepthMS(uint3 pixel : SV_DispatchThreadID, uint3 tile : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint width, height, samples;
    DepthMS.GetDimensions(width, height, samples);
    uint2 p = min(pixel.xy, uint2(width, height) - 1);
    float farthest = 0;
    for (uint s = 0; s < samples; s++)
        farthest = max(farthest, DepthMS.Load(p, s));
    reduceTile(tile.xy, lane, farthest);
}

struct Part { float4 lower; float4 upper; uint4 ranges; };
struct IndexedCommand { uint indexCount, instanceCount, firstIndex; int vertexOffset; uint firstInstance; };
StructuredBuffer<Part> Parts : register(t0);
StructuredBuffer<float> TileMax : register(t3);
RWStructuredBuffer<IndexedCommand> Commands : register(u0);

bool hidden(float2 lo, float2 hi, float nearest)
{
    // Include the border pixels even when a projected edge rounds onto a tile boundary.
    int2 first = (int2)clamp(floor(lo - 1), 0, ViewSize - 1) / 8;
    int2 last = (int2)clamp(ceil(hi + 1), 0, ViewSize - 1) / 8;
    for (int y = first.y; y <= last.y; y++)
        for (int x = first.x; x <= last.x; x++)
            if (!(nearest > TileMax[y * TilesX + x] + 0.00001))
                return false;
    return true;
}

[numthreads(64, 1, 1)]
void classify(uint3 group : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint id = (group.x + group.y * GroupsX) * 64 + lane;
    if (id >= PartCount) return;
    Part part = Parts[id];
    float2 lo = 1e30, hi = -1e30;
    float nearest = 1;
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
    if (visible && !crossesNear && UseDepth)
        visible = !hidden(lo, hi, nearest);
    bool coarse = !crossesNear && max(hi.x - lo.x, hi.y - lo.y) < LodPixels && part.ranges.w > 0;
    IndexedCommand cmd;
    cmd.firstIndex = coarse ? part.ranges.z : part.ranges.x;
    cmd.indexCount = coarse ? part.ranges.w : part.ranges.y;
    cmd.instanceCount = visible ? 1 : 0;
    cmd.vertexOffset = 0;
    cmd.firstInstance = 0;
    Commands[id] = cmd;
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
