using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Editor.App.DebugProtocol;
using Xunit;

namespace Editor.IntegrationTests;

[Trait("Category", "Offline")]
public sealed class DebugLogBufferTests
{
    [Fact]
    public void StaleCursor_ReportsOldestRetainedSequenceAfterRollover()
    {
        var buffer = new DebugLogBuffer();
        for (var i = 0; i < 4101; i++)
        {
            buffer.ProcessEntry(new Entry(ILogEntry.EntryLevel.Info, "entry " + i));
        }
        var entries = new JArray();
        buffer.CollectEntries(0, ILogEntry.EntryLevel.Debug, 3, entries, out var latest, out var oldest);
        Assert.Equal(4100, latest);
        Assert.Equal(5, oldest);
        Assert.Equal(new long[] { 5, 6, 7 }, entries.Select(e => e["seq"]!.Value<long>()));
        // A null string JValue serializes as JSON null even though its in-memory type is String.
        Assert.All(JArray.Parse(entries.ToString()), e => Assert.Equal(JTokenType.Null, e["sourceId"]!.Type));
    }

    [Fact]
    public void NewestWindow_IsChosenBeforeFilteringAndNonpositiveLimitIsEmpty()
    {
        var buffer = new DebugLogBuffer();
        buffer.ProcessEntry(new Entry(ILogEntry.EntryLevel.Error, "older error"));
        buffer.ProcessEntry(new Entry(ILogEntry.EntryLevel.Info, "newest info"));
        var entries = new JArray();
        buffer.CollectEntries(-1, ILogEntry.EntryLevel.Error, 1, entries, out _, out _);
        Assert.Empty(entries);
        buffer.CollectEntries(-1, ILogEntry.EntryLevel.Error, 2, entries, out _, out _);
        Assert.Single(entries);
        Assert.Equal("older error", entries[0]["message"]!.Value<string>());
        foreach (var count in new[] { 0, -1 })
        {
            entries.Clear();
            buffer.CollectEntries(-1, ILogEntry.EntryLevel.Debug, count, entries, out _, out _);
            Assert.Empty(entries);
        }
    }

    private sealed record Entry(ILogEntry.EntryLevel Level, string Message) : ILogEntry
    {
        public DateTime TimeStamp => new(2026, 1, 1);
        public IReadOnlyList<Guid> SourceIdPath => [];
        public double SecondsSinceStart => 0;
        public double SecondsAgo => 0;
        public Guid SourceId => Guid.Empty;
    }
}
