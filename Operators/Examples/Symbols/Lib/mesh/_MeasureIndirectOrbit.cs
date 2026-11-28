using T3.Graphics.Compat;

namespace Examples.Lib.mesh;

[Guid("d13d7e22-04f4-4d03-8af6-000000001200")]
internal sealed class _MeasureIndirectOrbit : Instance<_MeasureIndirectOrbit>
{
    [Output(Guid = "d13d7e22-04f4-4d03-8af6-000000001201", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001202")]
    public readonly InputSlot<Texture2D> Scene = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001203")]
    public readonly InputSlot<bool> Benchmark = new();

    [Input(Guid = "d13d7e22-04f4-4d03-8af6-000000001204")]
    public readonly InputSlot<bool> PairDepthModes = new();

    [Input(Guid = "0497d31c-df80-4074-b323-1214b16c80bf")]
    public readonly InputSlot<bool> CompareExamples = new();

    private static readonly int[] ExampleModes = [6, 7, 3, 4];
    private const int Warmup = 24, Modes = 8, Passes = 5, Steps = _IndirectOrbitScene.Steps;
    private const int FramesPerMode = Warmup + Steps;
    private readonly double[] _times = new double[Passes * Modes * Steps];
    private readonly Timers[] _ring = new Timers[8];
    private int _frame;
    private bool _complete, _paired, _examples;

    public _MeasureIndirectOrbit() => Output.UpdateAction = Update;

    private void Update(EvaluationContext context)
    {
        if (!Benchmark.GetValue(context))
        {
            if (_frame > 0 && !_complete)
                Drain();
            _frame = 0;
            _complete = false;
            Output.Value = Scene.GetValue(context);
            return;
        }
        if (_complete)
        {
            Output.Value = Scene.GetValue(context);
            return;
        }
        if (_frame == 0)
        {
            _examples = CompareExamples.GetValue(context);
            _paired = !_examples && PairDepthModes.GetValue(context);
            Array.Fill(_times, double.NaN);
            Log.Info("Starting GPU benchmark. Keep the example pinned until the CSV path appears in the log.", this);
        }

        int block = _frame / FramesPerMode, pass = block / Modes;
        int mode = (block + 2 * pass) % Modes, step = _frame % FramesPerMode - Warmup;
        if (_examples)
        {
            var count = ExampleModes.Length;
            int framesPerPass = Warmup + count * Steps;
            pass = _frame / framesPerPass;
            int within = _frame % framesPerPass - Warmup;
            step = within < 0 ? within : within / count;
            int index = (within + Warmup + pass + Math.Max(step, 0)) % count;
            mode = ExampleModes[index];
        }
        else if (_paired)
        {
            // Two adjacent frames use the same pose. Reverse their order across poses and passes.
            int framesPerPass = Warmup + 2 * Steps;
            pass = _frame / framesPerPass;
            int within = _frame % framesPerPass - Warmup;
            step = within < 0 ? within : within / 2;
            bool depthFirst = (pass + Math.Max(0, step)) % 2 != 0;
            bool firstFrame = (within & 1) == 0;
            mode = firstFrame == depthFirst ? 4 : 2;
        }
        var variables = context.FloatVariables;
        var hadPrevious = variables.TryGetValue(_IndirectOrbitScene.SampleVariable, out var previous);
        variables[_IndirectOrbitScene.SampleVariable] = mode * Steps + (step + Steps) % Steps;
        try
        {
            if (step < 0)
                Output.Value = Scene.GetValue(context);
            else
                Measure(context, (pass * Modes + mode) * Steps + step);
        }
        finally
        {
            if (hadPrevious)
                variables[_IndirectOrbitScene.SampleVariable] = previous;
            else
                variables.Remove(_IndirectOrbitScene.SampleVariable);
        }
        int frames = _examples ? (Warmup + Steps * ExampleModes.Length) * Passes : _paired ? (Warmup + 2 * Steps) * Passes : FramesPerMode * Modes * Passes;
        if (++_frame != frames)
            return;

        _complete = true;
        Drain();
        SaveResults();
    }

    private void Measure(EvaluationContext context, int sample)
    {
        var device = ResourceManager.Device;
        var dc = device.ImmediateContext;
        var q = _ring[_frame % _ring.Length] ??= new Timers(device);
        if (!Collect(q))
        {
            // Only stall if a query is still in flight when its slot is needed again.
            device.Backend.WaitForGpu();
            Collect(q);
        }
        dc.Begin(q.Clock);
        dc.End(q.Start);
        try
        {
            Output.Value = Scene.GetValue(context);
        }
        finally
        {
            dc.End(q.End);
            dc.End(q.Clock);
            q.Sample = sample;
        }
    }

    private bool Collect(Timers q)
    {
        if (q.Sample < 0)
            return true;
        var dc = ResourceManager.Device.ImmediateContext;
        if (!dc.GetData(q.Clock, AsynchronousFlags.DoNotFlush, out QueryDataTimestampDisjoint clock)
            || !dc.GetData(q.Start, AsynchronousFlags.DoNotFlush, out long start)
            || !dc.GetData(q.End, AsynchronousFlags.DoNotFlush, out long end))
            return false;
        if (!clock.Disjoint && clock.Frequency > 0 && end > start)
            _times[q.Sample] = 1000.0 * (end - start) / clock.Frequency;
        q.Sample = -1;
        return true;
    }

    private void Drain()
    {
        ResourceManager.Device.Backend.WaitForGpu();
        foreach (var q in _ring)
        {
            if (q == null) continue;
            Collect(q);
            q.Sample = -1;
        }
    }

    private void SaveResults()
    {
        var path = Path.Combine(Path.GetTempPath(), $"TiXL-IndirectOrbit-{Guid.NewGuid():N}.csv");
        try
        {
            using var csv = new StreamWriter(path);
            csv.WriteLine("pass,mode,step,degrees,gpu_ms");
            for (var i = 0; i < _times.Length; i++)
            {
                int pass = i / (Steps * Modes), mode = i / Steps % Modes, step = i % Steps;
                if (_examples && Array.IndexOf(ExampleModes, mode) < 0) continue;
                if (_paired && mode is not (2 or 4)) continue;
                csv.WriteLine(FormattableString.Invariant($"{pass + 1},{(DrawGeometryIndirectExample.DrawMode)mode},{step},{360 * step / Steps},{_times[i]:F6}"));
            }
            Log.Info($"Indirect orbit benchmark on {ResourceManager.Device.Backend.AdapterName} saved to {path}", this);
        }
        catch (IOException e)
        {
            Log.Warning($"Could not save benchmark to {path}: {e.Message}", this);
        }
    }

    private sealed class Timers(Device device) : IDisposable
    {
        public readonly Query Clock = new(device, new QueryDescription { Type = QueryType.TimestampDisjoint });
        public readonly Query Start = new(device, new QueryDescription { Type = QueryType.Timestamp });
        public readonly Query End = new(device, new QueryDescription { Type = QueryType.Timestamp });
        public int Sample = -1;

        public void Dispose()
        {
            Clock.Dispose();
            Start.Dispose();
            End.Dispose();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var q in _ring)
                q?.Dispose();
    }
}
