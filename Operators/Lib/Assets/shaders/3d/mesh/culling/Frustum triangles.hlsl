// Compact a mesh index stream for DrawInstancedIndirect. Every 128 consecutive faces form a group.
// Mark writes four visibility words per group, Prefix assigns output offsets, and Scatter preserves face order.
// Static meshes may reuse local bounds. MarkDepth can reject a whole group using the opaque depth pyramid.

// The draw uses TiXL's stock PBR vertex shader and the same clip-space convention.
struct PbrVertex
{
    float3 Position;
    float3 Normal;
    float3 Tangent;
    float3 Bitangent;
    float2 TexCoord;
    float2 TexCoord2;
    float Selected;
    float3 ColorRGB;
};

cbuffer CullParameters : register(b0)
{
    float4x4 ObjectToClip;
    uint FaceCount;
    uint GroupCount;
    uint GroupsX;
    uint VertexCount;
    uint DepthWidth, DepthHeight;
    float DepthBias;
    uint CopyCount;
    float PointSize;
    int ScaleFactorMode, UsePointScale, Padding;
};
StructuredBuffer<PbrVertex> Vertices : register(t0);
StructuredBuffer<uint3> Faces : register(t1);
RWStructuredBuffer<uint4> Masks : register(u0);
RWStructuredBuffer<uint> Offsets : register(u1);
RWStructuredBuffer<uint3> Visible : register(u2);
RWStructuredBuffer<uint> Arguments : register(u3);
struct Bounds
{
    float4 low;
    float4 high;
};
RWStructuredBuffer<Bounds> GroupBounds : register(u4);

// Signed distances use homogeneous clip coordinates: left/right and bottom/top are Ãƒâ€šÃ‚Â±w, near/far are 0 and w.
float distanceToPlane(float4 v, uint p)
{
    if (p == 0)
        return v.w + v.x;
    if (p == 1)
        return v.w - v.x;
    if (p == 2)
        return v.w + v.y;
    if (p == 3)
        return v.w - v.y;
    return p == 4 ? v.z : v.w - v.z;
}

uint outcode(float4 p, float pad)
{
    return (p.x < -p.w - pad ? 1u : 0u) | (p.x > p.w + pad ? 2u : 0u) | (p.y < -p.w - pad ? 4u : 0u) |
           (p.y > p.w + pad ? 8u : 0u) | (p.z < -pad ? 16u : 0u) | (p.z > p.w + pad ? 32u : 0u);
}

// Only the ambiguous case reaches this clipper. Clip a polygon in triangle
// barycentric coordinates; this also handles a triangle surrounding the view.
bool intersectsRemainder(float4 a, float4 b, float4 c, uint planes, float pad)
{
    float2 polygon[9], clipped[9];
    polygon[0] = float2(0, 0);
    polygon[1] = float2(1, 0);
    polygon[2] = float2(0, 1);
    uint count = 3;
    [loop] for (uint plane = 0; plane < 6; ++plane)
    {
        if ((planes & (1u << plane)) == 0)
            continue;
        float da = distanceToPlane(a, plane);
        float2 slopes = float2(distanceToPlane(b, plane) - da, distanceToPlane(c, plane) - da);
        float2 prev = polygon[count - 1];
        float dp = da + dot(prev, slopes) + pad;
        uint n = 0;
        [loop] for (uint i = 0; i < count; ++i)
        {
            float2 cur = polygon[i];
            float dc = da + dot(cur, slopes) + pad;
            if ((dp < 0) != (dc < 0))
            {
                if (n >= 9)
                    return true;
                clipped[n++] = lerp(prev, cur, saturate(dp / (dp - dc)));
            }
            if (dc >= 0)
            {
                if (n >= 9)
                    return true;
                clipped[n++] = cur;
            }
            prev = cur;
            dp = dc;
        }
        if (n == 0)
            return false;
        count = n;
        [loop] for (uint j = 0; j < count; ++j) polygon[j] = clipped[j];
    }
    return true;
}

// Trivially reject triangles outside one common plane. Clip ambiguous triangles, retaining numerically uncertain cases.
bool intersects(uint3 f)
{
    if (any(f >= VertexCount))
        return false;
    float4 a = mul(float4(Vertices[f.x].Position, 1), ObjectToClip);
    float4 b = mul(float4(Vertices[f.y].Position, 1), ObjectToClip);
    float4 c = mul(float4(Vertices[f.z].Position, 1), ObjectToClip);
    float4 m = max(abs(a), max(abs(b), abs(c)));
    float scale = max(max(m.x, m.y), max(m.z, m.w));
    if (!all(isfinite(a)) || !all(isfinite(b)) || !all(isfinite(c)) || scale > 1e35)
        return true;
    float pad = 2e-6 * max(scale, 1e-6);
    uint ca = outcode(a, pad), cb = outcode(b, pad), cc = outcode(c, pad);
    if ((ca & cb & cc) != 0)
        return false;
    if (ca == 0 || cb == 0 || cc == 0)
        return true;
    return intersectsRemainder(a, b, c, ca | cb | cc, pad);
}

groupshared uint maskWords[4];
[numthreads(128, 1, 1)] void Mark(uint3 block : SV_GroupID, uint lane : SV_GroupIndex) {
    uint group = block.y * GroupsX + block.x;
    if (group >= GroupCount)
        return;
    if (lane < 4)
        maskWords[lane] = 0;
    GroupMemoryBarrierWithGroupSync();
    uint id = group * 128 + lane;
    if (id < FaceCount && intersects(Faces[min(id, FaceCount - 1)]))
        InterlockedOr(maskWords[lane >> 5], 1u << (lane & 31));
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0)
        Masks[group] = uint4(maskWords[0], maskWords[1], maskWords[2], maskWords[3]);
}

// Build local bounds once for an explicitly static vertex/index source.
groupshared float3 minimums[128],
    maximums[128];
[numthreads(128, 1, 1)] void BuildBounds(uint3 block : SV_GroupID, uint lane : SV_GroupIndex) {
    uint g = block.y * GroupsX + block.x;
    if (g >= GroupCount)
        return;
    uint id = g * 128 + lane;
    float3 lo = 3.4e38, hi = -3.4e38;
    if (id < FaceCount)
    {
        uint3 f = Faces[id];
        if (all(f < VertexCount))
        {
            float3 a = Vertices[f.x].Position, b = Vertices[f.y].Position, c = Vertices[f.z].Position;
            lo = min(a, min(b, c));
            hi = max(a, max(b, c));
            if (!all(isfinite(a)) || !all(isfinite(b)) || !all(isfinite(c)))
            {
                lo = -3.4e38;
                hi = 3.4e38;
            }
        }
        else
        {
            lo = -3.4e38;
            hi = 3.4e38;
        }
    }
    minimums[lane] = lo;
    maximums[lane] = hi;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint stride = 64; stride > 0; stride /= 2)
    {
        if (lane < stride)
        {
            minimums[lane] = min(minimums[lane], minimums[lane + stride]);
            maximums[lane] = max(maximums[lane], maximums[lane + stride]);
        }
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane == 0)
    {
        Bounds result;
        result.low = float4(minimums[0], 0);
        result.high = float4(maximums[0], 0);
        GroupBounds[g] = result;
    }
}

// A box well within or beyond a plane can route an entire coherent group.
// The round-off guard includes the absolute matrix products before cancellation.
int classifyGroup(Bounds bounds)
{
    float3 center = .5 * bounds.low.xyz + .5 * bounds.high.xyz;
    float3 ext = .5 * bounds.high.xyz - .5 * bounds.low.xyz;
    if (!all(isfinite(center + ext)) || any(ext > 1e30))
        return 0;
    float4 p = mul(float4(center, 1), ObjectToClip);
    float4 xx = ObjectToClip[0], yy = ObjectToClip[1], zz = ObjectToClip[2];
    float4 scales = mul(float4(abs(center) + ext, 1), abs(ObjectToClip));
    float guard = 1e-5 * max(max(max(scales.x, scales.y), max(scales.z, scales.w)), 1e-6);
    bool inside = true;
    [unroll] for (uint i = 0; i < 6; ++i)
    {
        float d = distanceToPlane(p, i);
        float3 n = float3(distanceToPlane(xx, i), distanceToPlane(yy, i), distanceToPlane(zz, i));
        float support = dot(abs(n), ext);
        if (d + support < -guard)
            return -1;
        if (!(d - support > guard))
            inside = false;
    }
    return inside ? 1 : 0;
}

// Test a conservative projection of all eight local box corners. A near-plane or
// eye crossing stays visible. The depth pyramid is max-reduced with clear = 1.
StructuredBuffer<float> OccluderDepth : register(t2);
bool hiddenByDepth(Bounds box)
{
    float2 low = 1, high = -1;
    float nearest = 1;
    float3 magnitude = max(abs(box.low.xyz), abs(box.high.xyz));
    float4 error = 8e-6 * max(mul(float4(magnitude, 1), abs(ObjectToClip)), 1e-6);
    [unroll] for (uint corner = 0; corner < 8; ++corner)
    {
        float3 v = float3((corner & 1) ? box.high.x : box.low.x, (corner & 2) ? box.high.y : box.low.y,
                          (corner & 4) ? box.high.z : box.low.z);
        float4 clip = mul(float4(v, 1), ObjectToClip);
        if (!all(isfinite(clip)) || !all(isfinite(error)) || clip.w <= error.w + 1e-7 || clip.z <= error.z)
            return false;
        float2 ndc = clip.xy / clip.w;
        float2 delta = (error.xy + abs(ndc) * error.w) / (clip.w - error.w);
        low = min(low, ndc - delta);
        high = max(high, ndc + delta);
        nearest = min(nearest, (clip.z - error.z) / (clip.w + error.w));
    }
    float2 dimensions = float2(DepthWidth, DepthHeight);
    float2 uvMin = float2(low.x, -high.y) * .5 + .5;
    float2 uvMax = float2(high.x, -low.y) * .5 + .5;
    int2 limit = int2(DepthWidth, DepthHeight) - 1;
    int2 a = clamp(int2(floor(saturate(uvMin) * dimensions)) - 1, 0, limit) / 8;
    int2 b = clamp(int2(ceil(saturate(uvMax) * dimensions)) + 1, 0, limit) / 8;
    uint2 size = (uint2(DepthWidth, DepthHeight) + 7) / 8;
    uint start = 0;
    [loop] while (any(b - a > 1))
    {
        start += size.x * size.y;
        size = (size + 1) / 2;
        a /= 2;
        b /= 2;
    }
    float farthest =
        max(max(OccluderDepth[start + a.y * size.x + a.x], OccluderDepth[start + a.y * size.x + b.x]),
            max(OccluderDepth[start + b.y * size.x + a.x], OccluderDepth[start + b.y * size.x + b.x]));
    return nearest > farthest + DepthBias;
}

uint firstBits(uint count)
{
    return count >= 32 ? 0xffffffffu : ((1u << count) - 1);
}
groupshared int boxRelation;
void markGroup(uint3 block, uint lane, bool useDepth)
{
    uint g = block.y * GroupsX + block.x;
    if (g >= GroupCount)
        return;
    if (lane == 0)
    {
        Bounds box = GroupBounds[g];
        boxRelation = classifyGroup(box);
        if (useDepth && boxRelation >= 0 && hiddenByDepth(box))
            boxRelation = -1;
    }
    if (lane < 4)
        maskWords[lane] = 0;
    GroupMemoryBarrierWithGroupSync();
    if (boxRelation != 0)
    {
        if (lane == 0)
        {
            int n = (int)min(128u, FaceCount - g * 128);
            Masks[g] = boxRelation < 0 ? uint4(0, 0, 0, 0)
                                       : uint4(firstBits(n), firstBits(max(n - 32, 0)),
                                               firstBits(max(n - 64, 0)), firstBits(max(n - 96, 0)));
        }
        return;
    }
    uint id = g * 128 + lane;
    if (id < FaceCount && intersects(Faces[min(id, FaceCount - 1)]))
        InterlockedOr(maskWords[lane >> 5], 1u << (lane & 31));
    GroupMemoryBarrierWithGroupSync();
    if (lane == 0)
        Masks[g] = uint4(maskWords[0], maskWords[1], maskWords[2], maskWords[3]);
}

[numthreads(128, 1, 1)] void MarkStatic(uint3 block : SV_GroupID, uint lane : SV_GroupIndex) {
    markGroup(block, lane, false);
}

    [numthreads(128, 1, 1)] void MarkDepth(uint3 block : SV_GroupID, uint lane : SV_GroupIndex)
{
    markGroup(block, lane, true);
}

// A single workgroup assigns disjoint, source-ordered output ranges. Each lane
// scans a contiguous run of 128-face masks, then the group scans those 128 totals.
groupshared uint sums[128];
// Eight 16-lane rows share their subtotals. This scan preserves source order.
groupshared uint rowSums[8];
uint exclusiveSum(uint lane, uint value)
{
    sums[lane] = value;
    GroupMemoryBarrierWithGroupSync();
    uint row = lane / 16, start = row * 16, before = 0;
    [unroll] for (uint i = 0; i < 16; i++) if (start + i < lane) before += sums[start + i];
    if ((lane & 15) == 15)
        rowSums[row] = before + value;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint r = 0; r < 8; r++) if (r < row) before += rowSums[r];
    return before;
}

// Each lane scans a contiguous run of masks. The exclusive scan adds the preceding runs and supplies the indirect vertex count.
[numthreads(128, 1, 1)] void Prefix(uint lane : SV_GroupIndex) {
    uint span = (GroupCount + 127) / 128;
    uint begin = lane * span, end = min(begin + span, GroupCount);
    uint total = 0;
    [loop] for (uint i = begin; i < end; ++i)
    {
        Offsets[i] = total;
        uint4 k = countbits(Masks[i]);
        total += k.x + k.y + k.z + k.w;
    }
    uint baseOffset = exclusiveSum(lane, total);
    [loop] for (uint j = begin; j < end; ++j) Offsets[j] += baseOffset;
    if (lane == 127)
    {
        Arguments[0] = 3 * (baseOffset + total);
        Arguments[1] = 1;
        Arguments[2] = 0;
        Arguments[3] = 0;
    }
}

    // Count set bits preceding this lane to find its stable destination in the compacted face buffer.
    [numthreads(128, 1, 1)] void Scatter(uint3 block : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint group = block.y * GroupsX + block.x;
    if (group >= GroupCount)
        return;
    uint4 bits = Masks[group];
    uint word = lane >> 5, flag = 1u << (lane & 31);
    if ((bits[word] & flag) == 0)
        return;
    uint rank = countbits(bits[word] & (flag - 1));
    if (word > 0)
        rank += countbits(bits.x);
    if (word > 1)
        rank += countbits(bits.y);
    if (word > 2)
        rank += countbits(bits.z);
    Visible[Offsets[group] + rank] = Faces[group * 128 + lane];
}

// For at most 1024 triangles, mark, compact and publish the indirect count in one workgroup.
[numthreads(128, 1, 1)] void FilterSmall(uint lane : SV_GroupIndex) {
    uint span = (FaceCount + 127) / 128, start = lane * span, bits = 0;
    [loop] for (uint j = 0; j < span && start + j < FaceCount; j++) if (intersects(Faces[start + j])) bits |=
        1u << j;
    uint count = countbits(bits), offset = exclusiveSum(lane, count);
    if (lane == 127)
    {
        Arguments[0] = 3 * (offset + count);
        Arguments[1] = 1;
        Arguments[2] = 0;
        Arguments[3] = 0;
    }
    [loop] while (bits != 0)
    {
        uint j = firstbitlow(bits);
        Visible[offset++] = Faces[start + j];
        bits &= bits - 1;
    }
}

#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

StructuredBuffer<Point> InstancePoints : register(t3);
groupshared uint written;

// BuildBounds ran on the source mesh once, independent of the number of copies.
// Merge its partial boxes, then project a conservative rotated box for each point.
// Each tile is emitted in input order to preserve blending and point identity.
[numthreads(128, 1, 1)]
void CullPoints(uint lane : SV_GroupIndex)
{
    float3 lo = 3.4e38, hi = -3.4e38;
    for (uint g = lane; g < GroupCount; g += 128)
    {
        lo = min(lo, GroupBounds[g].low.xyz);
        hi = max(hi, GroupBounds[g].high.xyz);
    }
    minimums[lane] = lo;
    maximums[lane] = hi;
    if (lane == 0)
        written = 0;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint stride = 64; stride > 0; stride /= 2)
    {
        if (lane < stride)
        {
            minimums[lane] = min(minimums[lane], minimums[lane + stride]);
            maximums[lane] = max(maximums[lane], maximums[lane + stride]);
        }
        GroupMemoryBarrierWithGroupSync();
    }
    float3 center = minimums[0] * .5 + maximums[0] * .5;
    float3 extent = maximums[0] * .5 - minimums[0] * .5;
    for (uint first = 0; first < CopyCount; first += 128)
    {
        uint index = first + lane;
        bool keep = false;
        if (index < CopyCount)
        {
            Point p = InstancePoints[index];
            float factor = ScaleFactorMode == 0 ? 1 : (ScaleFactorMode == 1 ? p.FX1 : p.FX2);
            float3 scale = PointSize * factor * (UsePointScale ? p.Scale : 1);
            float3x3 rotation = (float3x3)transpose(qToMatrix(normalize(p.Rotation)));
            float3 c = mul(center * scale, rotation) + p.Position;
            float3 e = mul(extent * abs(scale), abs(rotation));
            // Account for cancellation in the placement transform before projection.
            e += 8e-6 * (abs(p.Position) + mul((abs(center) + extent) * abs(scale), abs(rotation)));
            Bounds box;
            box.low = float4(c - e, 0);
            box.high = float4(c + e, 0);
            keep = classifyGroup(box) >= 0 && !hiddenByDepth(box);
        }
        // Each lane writes directly into its stable position in the visible list.
        uint offset = exclusiveSum(lane, keep ? 1 : 0);
        uint tileStart = written;
        GroupMemoryBarrierWithGroupSync();
        if (keep)
            Offsets[tileStart + offset] = index;
        if (lane == 127)
            written = tileStart + offset + (keep ? 1 : 0);
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane == 0)
    {
        Arguments[0] = FaceCount * 3;
        Arguments[1] = written;
        Arguments[2] = 0;
        Arguments[3] = 0; // base vertex
        Arguments[4] = 0; // first instance
    }
}

// The private depth pass needs position and winding only. A row of coplanar
// tessellated quads can therefore use its two end edges for all instances.
// Material rendering still uses every original vertex and interpolant.
[numthreads(1, 1, 1)]
void InitDepthStripArgs()
{
    Arguments[0] = 0;           // groups reserve index ranges below
    Arguments[1] = CopyCount;
    Arguments[2] = 0;
    Arguments[3] = 0;
    Arguments[4] = 0;
}

// Recognize an extrusion along one object-space axis using exact comparisons.
// a,b are the start edge and c,d the end edge. No tolerance is allowed: beveled,
// deformed or folded strips must keep their original triangles.
int stripDirection(float3 a, float3 b, float3 c, float3 d)
{
    if (!all(isfinite(a)) || !all(isfinite(b)) || !all(isfinite(c)) || !all(isfinite(d)))
        return 0;
    [unroll] for (int axis = 0; axis < 3; ++axis)
    {
        int j = (axis + 1) % 3, k = (axis + 2) % 3;
        if (a[j] == c[j] && a[k] == c[k] && b[j] == d[j] && b[k] == d[k]
            && a[axis] == b[axis] && c[axis] == d[axis] && a[axis] != c[axis])
            return a[axis] < c[axis] ? axis + 1 : -(axis + 1);
    }
    return 0;
}

groupshared uint3 firstFace[128], secondFace[128];
groupshared int directions[128];
groupshared uint joinsPrevious[128];
groupshared uint groupStart;

[numthreads(128, 1, 1)]
void PackDepthStrips(uint3 block : SV_GroupID, uint lane : SV_GroupIndex)
{
    uint face = ((block.y * GroupsX + block.x) * 128 + lane) * 2;
    uint count = face < FaceCount ? min(2u, FaceCount - face) : 0;
    uint3 f = 0, g = 0;
    int direction = 0;
    if (count > 0)
        f = Faces[face];
    if (count == 2)
    {
        g = Faces[face + 1];
        if (f.y == g.x && f.z == g.z && all(f < VertexCount) && g.y < VertexCount)
            direction = stripDirection(Vertices[f.x].Position, Vertices[f.y].Position,
                                       Vertices[f.z].Position, Vertices[g.y].Position);
    }
    firstFace[lane] = f;
    secondFace[lane] = g;
    directions[lane] = direction;
    GroupMemoryBarrierWithGroupSync();

    // Only join cells sharing the exact two vertex indices in the same order.
    uint prev = max(1u, lane) - 1;
    bool join = lane > 0 && direction != 0 && direction == directions[prev]
                && firstFace[prev].z == f.x && secondFace[prev].y == f.y;
    joinsPrevious[lane] = join ? 1 : 0;
    uint emitted = join ? 0 : count;
    uint offset = exclusiveSum(lane, emitted);
    if (lane == 127)
    {
        // Depth writes use the nearest sample, so independent groups can reserve
        // index ranges in any order. The material index stream stays untouched.
        uint firstIndex;
        InterlockedAdd(Arguments[0], 3 * (offset + emitted), firstIndex);
        groupStart = firstIndex / 3;
    }
    GroupMemoryBarrierWithGroupSync();
    if (emitted == 0)
        return;
    uint last = lane;
    [loop] while (last < 127 && joinsPrevious[min(last + 1, 127u)] != 0)
        ++last;
    uint target = groupStart + offset;
    if (last == lane)
    {
        Visible[target] = f;
        if (count == 2)
            Visible[target + 1] = g;
    }
    else
    {
        // Sweep the unchanged start edge to the last cell's end edge. Matching
        // signed axes and shared indices prove there are no gaps or reversals.
        Visible[target] = uint3(f.x, f.y, firstFace[last].z);
        Visible[target + 1] = uint3(f.y, secondFace[last].y, firstFace[last].z);
    }
}
