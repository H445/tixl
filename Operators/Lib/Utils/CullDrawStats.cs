using T3.Graphics.Compat;

namespace Lib.render.culling;

internal sealed class CullDrawStats : IRenderStatsProvider, IDisposable
{
    // Keep independent results for each mesh submitted by the camera in one frame.
    private sealed class Sample
    {
        public readonly Queue<(Buffer Staging, int InputFaces)> Pending = new();
        public int MaterialFaces;
        public int? InputFaces;
    }

    private readonly List<Sample> _samples = new();
    private readonly Stack<Buffer> _available = new();
    private int _materialDraws, _depthDraws, _materialTriangles, _inputTriangles;

    public void CountDirect(int faces, int depthFaces)
    {
        if (!RenderStatsCollector.IsCollecting) return;
        _materialDraws++;
        _materialTriangles += faces;
        _inputTriangles += faces;
        if (depthFaces > 0) _depthDraws++;
    }

    public void CountIndirect(DeviceContext context, Buffer arguments, int faces, int depthFaces)
    {
        // The editor advances the stats collector. Player renders never request readbacks.
        if (!RenderStatsCollector.IsCollecting) return;
        while (_samples.Count <= _materialDraws)
            _samples.Add(new Sample());
        var sample = _samples[_materialDraws++];
        Span<byte> vertexCount = stackalloc byte[sizeof(uint)];
        while (sample.Pending.TryPeek(out var oldest)
               && ResourceManager.Device.Backend.TryReadCompletedBuffer(oldest.Staging.GpuBuffer, vertexCount))
        {
            sample.InputFaces = oldest.InputFaces;
            sample.MaterialFaces = (int)(BitConverter.ToUInt32(vertexCount) / 3);
            _available.Push(sample.Pending.Dequeue().Staging);
        }
        // Keep the scene count current even when the GPU readback is delayed.
        _inputTriangles += faces;
        // After a topology change, use the full input until its first matching GPU result arrives.
        _materialTriangles += sample.InputFaces == faces ? sample.MaterialFaces : faces;
        if (depthFaces > 0) _depthDraws++;

        // Use the latest completed packet while the GPU is busy. Bound memory even if collection stalls.
        if (sample.Pending.Count >= 3) return;
        if (!_available.TryPop(out var staging))
        {
            staging = new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = sizeof(uint), Usage = ResourceUsage.Staging, CpuAccessFlags = CpuAccessFlags.Read
            });
        }
        // Copy just vertexCount from the indirect packet (one instance, triangle list).
        context.CopyResource(arguments, staging);
        sample.Pending.Enqueue((staging, faces));
    }

    public IEnumerable<(string, int)> GetStats()
    {
        yield return ("Triangles", _inputTriangles);
        yield return (RenderStatsCollector.CulledTriangleCount, _inputTriangles - _materialTriangles);
        yield return ("Draw calls", _depthDraws + _materialDraws);
    }

    public void StartNewFrame()
    {
        _materialDraws = _depthDraws = 0;
        _materialTriangles = _inputTriangles = 0;
    }

    public void Dispose()
    {
        foreach (var sample in _samples)
            foreach (var pending in sample.Pending)
                pending.Staging.Dispose();
        foreach (var buffer in _available)
            buffer.Dispose();
    }
}
