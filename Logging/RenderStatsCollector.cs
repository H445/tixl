namespace T3.Core.Logging;

// todo: maybe move together with OpUpdateCounter. On the other hand, it's only required in Editor...
public static class RenderStatsCollector
{
    public static void RegisterProvider(IRenderStatsProvider newProvider)
    {
        if (_providers.Contains(newProvider))
        {
            Log.Warning($"Already registered provider {newProvider}");
            return;
        }
        _providers.Add(newProvider);
    }

    public static void UnregisterProvider(IRenderStatsProvider obsoleteProvider)
    {
        if (!_providers.Contains(obsoleteProvider))
            return;
            
        _providers.Remove(obsoleteProvider);
    }
        
    public static bool IsCollecting { get; private set; }
    private static bool _meshOcclusionEnabled;
    public const string PendingTriangleCount = "Mesh triangle readback pending";
    public const string CulledTriangleCount = "Mesh triangles skipped";
    public static void MarkMeshOcclusionEnabled() => _meshOcclusionEnabled = true;

    public static void StartNewFrame()
    {
        IsCollecting = true;
        ResultsForLastFrame.Clear();

        foreach (var (topic, count) in GetFrameResults())
        {
            ResultsForLastFrame.TryGetValue(topic, out var sum);
            ResultsForLastFrame[topic] = sum + count;
        }
            
        ResultsForLastFrame.Remove(CulledTriangleCount, out var skipped);
        bool pending = ResultsForLastFrame.Remove(PendingTriangleCount);
        if (_meshOcclusionEnabled && !pending)
            ResultsForLastFrame["Triangles drawn"] = ResultsForLastFrame.GetValueOrDefault("Triangles") - skipped;
        _meshOcclusionEnabled = false;

        foreach (var p in _providers)
        {
            p.StartNewFrame();
        }
    }

    private static IEnumerable<(string, int)> GetFrameResults()
    {
        foreach (var p in _providers)
        {
            if (p == null)
                continue;
                
            foreach (var statAndCount in p.GetStats())
            {
                yield return statAndCount;
            }
        }
    }

    private static readonly List<IRenderStatsProvider> _providers = new();
    public static Dictionary<string, int> ResultsForLastFrame { get; private set; } = new();
}