// Build the packed maximum-depth pyramid consumed by MarkDepth. Depth increases away from the camera.
// A group is hidden only when its nearest depth lies behind the farthest covered sample, including MSAA samples.
// A clear or invalid sample contributes 1, so holes in an occluder keep the candidate visible.

// TiXL forward hardware depth. Each cell records the farthest sample in its footprint.
cbuffer Reduction : register(b0)
{
    uint Width, Height, SourceStart, TargetStart;
    uint TargetWidth, TargetHeight, SampleCount, LevelCount;
};
Texture2D<float> Depth : register(t0);
Texture2DMS<float> DepthMS : register(t1);
RWStructuredBuffer<float> Levels : register(u0);
groupshared float values[64];

float checkedDepth(float z)
{
    return isfinite(z) && z >= 0 && z <= 1 ? z : 1.0;
}

void storeTile(uint2 tile, uint lane, float z)
{
    values[lane] = z;
    GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint span = 32; span > 0; span >>= 1)
    {
        if (lane < span)
            values[lane] = max(values[lane], values[lane + span]);
        GroupMemoryBarrierWithGroupSync();
    }
    if (lane == 0)
        Levels[tile.y * TargetWidth + tile.x] = values[0];
}

// Eight tiles share one workgroup. Each lane reduces four pixels in registers.
groupshared float tileValues[128];
[numthreads(16, 8, 1)] void Seed(uint3 group : SV_GroupID, uint3 local : SV_GroupThreadID) {
    uint2 tile = group.xy * uint2(4, 2) + local.xy / 4;
    uint2 lane = local.xy % 4;
    uint2 p = tile * 8 + lane * 2;
    uint base = ((local.y / 4) * 4 + local.x / 4) * 16;
    float z = 1;
    // A partial tile contains the screen border and must remain conservative.
    if (p.x + 1 < Width && p.y + 1 < Height)
    {
        z = max(
            max(checkedDepth(Depth.Load(int3(p, 0))), checkedDepth(Depth.Load(int3(p + uint2(1, 0), 0)))),
            max(checkedDepth(Depth.Load(int3(p + uint2(0, 1), 0))), checkedDepth(Depth.Load(int3(p + 1, 0)))));
    }
    tileValues[base + lane.y * 4 + lane.x] = z;
    GroupMemoryBarrierWithGroupSync();
    if (all(lane == 0) && tile.x < TargetWidth && tile.y < TargetHeight)
    {
        [unroll] for (uint i = 1; i < 16; i++) z = max(z, tileValues[base + i]);
        Levels[tile.y * TargetWidth + tile.x] = z;
    }
}

    // Reduce every sample before combining pixels. Even one uncovered sample must keep a partially covered tile visible.
    [numthreads(8, 8, 1)] void SeedMS(uint3 pixel : SV_DispatchThreadID, uint3 tile : SV_GroupID,
                                      uint lane : SV_GroupIndex)
{
    float z = 1;
    if (pixel.x < Width && pixel.y < Height)
    {
        z = 0;
        [loop] for (uint s = 0; s < SampleCount; ++s) z = max(z, checkedDepth(DepthMS.Load(int2(pixel.xy), s)));
    }
    storeTile(tile.xy, lane, z);
}

// Each workgroup combines an 8 x 8 tile locally. Successive levels remain
// available at their original packed offsets for the conservative box test.
[numthreads(8, 8, 1)] void ReduceLevels(uint3 thread : SV_DispatchThreadID, uint3 local : SV_GroupThreadID,
                                        uint lane : SV_GroupIndex) {
    uint2 size = uint2(TargetWidth, TargetHeight);
    uint2 inputLimit = uint2(Width, Height) - 1;
    uint2 a = 2 * min(thread.xy, size - 1);
    uint2 b = min(a + 1, inputLimit);
    float z = max(max(Levels[SourceStart + a.y * Width + a.x], Levels[SourceStart + a.y * Width + b.x]),
                  max(Levels[SourceStart + b.y * Width + a.x], Levels[SourceStart + b.y * Width + b.x]));
    uint offset = TargetStart;
    if (all(thread.xy < size))
        Levels[offset + thread.y * size.x + thread.x] = z;
    values[lane] = z;

    [unroll] for (uint level = 1; level < 4; ++level)
    {
        if (level >= LevelCount)
            break;
        GroupMemoryBarrierWithGroupSync();
        offset += size.x * size.y;
        size = (size + 1) / 2;
        uint step = 1u << (level - 1);
        uint mask = 2 * step - 1;
        if (((local.x | local.y) & mask) == 0)
        {
            z = max(max(values[lane], values[lane + step]),
                    max(values[lane + 8 * step], values[lane + 9 * step]));
            values[lane] = z;
            uint2 cell = thread.xy >> level;
            if (all(cell < size))
                Levels[offset + cell.y * size.x + cell.x] = z;
        }
    }
}
