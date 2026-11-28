using T3.Graphics.Compat;

namespace Lib.render.culling;

internal sealed class CullDrawStats : IRenderStatsProvider, IDisposable
{
    // Keep all indirect arguments from a camera frame together. Mixing independent
    // per-draw samples can produce a total that was never submitted by any frame.
    private sealed class Frame
    {
        public readonly List<Buffer> Packets = new();
        public int InputFaces, Draws, ReadIndex, DrawnFaces;
    }

    private readonly Queue<Frame> _pending = new();
    private readonly Stack<Frame> _available = new();
    private Frame _current;
    private int _inputFaces, _drawCalls, _indirectFaces, _indirectDraws;
    private int _completedInput, _completedDraws, _completedSkipped;

    public void CountDepthDraw()
    {
        if (RenderStatsCollector.IsCollecting) _drawCalls++;
    }

    public void CountDirect(int faces)
    {
        if (!RenderStatsCollector.IsCollecting) return;
        _inputFaces += faces;
        _drawCalls++;
    }

    public void CountIndirect(DeviceContext context, Buffer arguments, int faces)
    {
        if (!RenderStatsCollector.IsCollecting) return;
        CountDirect(faces);
        _indirectDraws++;
        _indirectFaces += faces;

        // Under GPU backpressure skip a whole sample, never part of a frame.
        if (_pending.Count >= 3) return;
        _current ??= _available.TryPop(out var frame) ? frame : new Frame();
        if (_current.Draws == _current.Packets.Count)
        {
            _current.Packets.Add(new Buffer(ResourceManager.Device, new BufferDescription
            {
                SizeInBytes = DeviceContext.DrawInstancedIndirectArgsStride,
                Usage = ResourceUsage.Staging,
                CpuAccessFlags = CpuAccessFlags.Read
            }));
        }
        // Indexed and non-indexed packets begin with the element count and instance count.
        context.CopySubresourceRegion(arguments, 0, new ResourceRegion(0, 0, 0, DeviceContext.DrawInstancedIndirectArgsStride, 1, 1),
                                      _current.Packets[_current.Draws++], 0);
        _current.InputFaces += faces;
    }

    private void ReadCompletedFrames()
    {
        if (_current != null)
        {
            _pending.Enqueue(_current);
            _current = null;
        }
        Span<byte> packet = stackalloc byte[DeviceContext.DrawInstancedIndirectArgsStride];
        while (_pending.TryPeek(out var frame))
        {
            while (frame.ReadIndex < frame.Draws)
            {
                var buffer = frame.Packets[frame.ReadIndex];
                if (!ResourceManager.Device.Backend.TryReadCompletedBuffer(buffer.GpuBuffer, packet))
                    return;

                // Each packet describes a triangle list: vertices per instance, then instance count.
                frame.DrawnFaces += (int)((BitConverter.ToUInt32(packet) / 3) * BitConverter.ToUInt32(packet[4..]));
                frame.ReadIndex++;
            }
            _completedInput = frame.InputFaces;
            _completedDraws = frame.Draws;
            _completedSkipped = frame.InputFaces - frame.DrawnFaces;
            frame.InputFaces = frame.Draws = frame.ReadIndex = frame.DrawnFaces = 0;
            _available.Push(_pending.Dequeue());
        }
    }

    public IEnumerable<(string, int)> GetStats()
    {
        ReadCompletedFrames();
        yield return ("Triangles", _inputFaces);
        yield return ("Draw calls", _drawCalls);
        if (_indirectDraws == 0) yield break;
        if (_completedInput == _indirectFaces && _completedDraws == _indirectDraws)
            yield return (RenderStatsCollector.CulledTriangleCount, _completedSkipped);
        else
            yield return (RenderStatsCollector.PendingTriangleCount, 1);
    }

    public void StartNewFrame()
    {
        _inputFaces = _drawCalls = _indirectFaces = _indirectDraws = 0;
    }

    public void Dispose()
    {
        if (_current != null) _available.Push(_current);
        foreach (var frame in _available.Concat(_pending))
            foreach (var packet in frame.Packets)
                packet.Dispose();
    }
}
