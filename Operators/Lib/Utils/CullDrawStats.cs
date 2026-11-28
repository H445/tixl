using T3.Graphics.Compat;

namespace Lib.render.culling;

internal sealed class CullDrawStats : IRenderStatsProvider, IDisposable
{
    // Keep independent results if this operator draws under multiple cameras in one frame.
    private sealed class Sample
    {
        public readonly Queue<Buffer> Pending = new();
        public int LastTriangles;
    }

    private readonly List<Sample> _samples = new();
    private readonly Stack<Buffer> _available = new();
    private int _draws, _triangles;
    private bool _collecting;

    public void CountDirect(int faces)
    {
        if (!_collecting) return;
        _draws++;
        _triangles += faces;
    }

    public void CountIndirect(DeviceContext context, Buffer arguments)
    {
        // The editor advances the stats collector. Player renders never request readbacks.
        if (!_collecting) return;
        while (_samples.Count <= _draws)
            _samples.Add(new Sample());
        var sample = _samples[_draws++];
        Span<byte> vertexCount = stackalloc byte[sizeof(uint)];
        while (sample.Pending.TryPeek(out var oldest)
               && ResourceManager.Device.Backend.TryReadCompletedBuffer(oldest.GpuBuffer, vertexCount))
        {
            sample.LastTriangles = (int)(BitConverter.ToUInt32(vertexCount) / 3);
            _available.Push(sample.Pending.Dequeue());
        }
        _triangles += sample.LastTriangles;

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
        sample.Pending.Enqueue(staging);
    }

    public IEnumerable<(string, int)> GetStats()
    {
        yield return ("Triangles", _triangles);
        yield return ("Draw calls", _draws);
    }

    public void StartNewFrame()
    {
        _collecting = true;
        _draws = 0;
        _triangles = 0;
    }

    public void Dispose()
    {
        foreach (var sample in _samples)
            foreach (var buffer in sample.Pending)
                buffer.Dispose();
        foreach (var buffer in _available)
            buffer.Dispose();
    }
}
