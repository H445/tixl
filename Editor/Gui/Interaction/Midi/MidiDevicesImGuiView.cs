using ImGuiNET;
using System.Collections.Generic;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Window shell for the MIDI Devices panel.
/// Iterates connected devices and dispatches each one to the appropriate layout view:
/// <list type="bullet">
///   <item><see cref="Apc40Mk1LayoutView"/> — Akai APC40 MK1 full hardware layout</item>
///   <item><see cref="GenericMidiLayoutView"/> — generic clip-grid fallback for all other devices</item>
/// </list>
/// Shared drawing primitives live in <see cref="MidiLayoutDrawHelpers"/>.
/// Optional MIDI Debug panel (mappings and live CC values) is shown below each controller layout
/// </summary>
internal sealed class MidiDevicesImGuiView : T3.Editor.Gui.Windows.Window
{
    internal MidiDevicesImGuiView()
    {
        Config.Title = "MIDI Devices";
    }

    private bool   _blinkOn;
    private double _lastBlinkFlipMs;

    // Debug panel settings (persist across draws in this view)
    private static bool _debugShowZeros;
    private static bool _debugShowAll;
    private static int  _debugMaxRows   = 128;

    protected override void DrawContent()
    {
        var now   = DateTime.UtcNow;
        var nowMs = (now - DateTime.UnixEpoch).TotalMilliseconds;

        if (_lastBlinkFlipMs == 0)
        {
            _lastBlinkFlipMs = nowMs;
        }
        else if (nowMs - _lastBlinkFlipMs >= 500)
        {
            _lastBlinkFlipMs = nowMs;
            _blinkOn = !_blinkOn;
        }

        var statuses = CompatibleMidiDeviceHandling.GetConnectedDeviceStatuses();

        if (statuses.Count == 0)
        {
            ImGui.TextUnformatted("No compatible MIDI devices connected.");
            return;
        }

        ImGui.BeginChild("device_list", new Vector2(-1, -1), false);
        foreach (var s in statuses)
        {
            DrawDevice(s, _blinkOn);
            ImGui.Separator();
        }
        ImGui.EndChild();
    }

    private static void DrawDevice(MidiDeviceStatus s, bool blinkOn)
    {
        ImGui.PushID(s.ProductName);

        // Header line
        ImGui.TextUnformatted($"{s.ProductName} ({s.DeviceTypeName})");
        ImGui.SameLine();
        ImGui.TextUnformatted(s.IsInControlMode ? "[Control Mode]" : "[Passthrough]");

        // Dispatch to the correct layout view
        if (s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0)
            Apc40Mk1LayoutView.Draw(s, blinkOn);
        else
            GenericMidiLayoutView.Draw(s, blinkOn);

        ImGui.Spacing();
        // Debug panel (rendered under the controller layout)
        if (ImGui.CollapsingHeader("MIDI Debug: Mappings and live values"))
            DrawDebugPanel(s);

        ImGui.PopID();
    }

    private static void DrawDebugPanel(MidiDeviceStatus s)
    {
        ImGui.BeginChild("debug_map", new Vector2(0, 200), true);

        ImGui.TextUnformatted($"Snapshot: {s.SnapshotTimeUtc:O}");
        ImGui.TextUnformatted($"Device: {s.ProductName} ({s.DeviceTypeName})");
        ImGui.TextUnformatted($"ControlCount: {s.ControlCount}, ClipGridSize: {(s.ClipGridSize?.ToString() ?? "n/a")}, UseGenericMode: {(s.UseGenericMode.HasValue ? s.UseGenericMode.Value.ToString() : "n/a")}");
        ImGui.Spacing();

        ImGui.Checkbox("Show zero values", ref _debugShowZeros);
        ImGui.SameLine();
        ImGui.Checkbox("Show all matches", ref _debugShowAll);
        ImGui.SameLine();
        ImGui.TextUnformatted($"Max rows: {_debugMaxRows}");

        ImGui.Separator();

        var vals = s.ControllerValues ?? Array.Empty<float>();
        var cols = s.ControllerColors ?? Array.Empty<int>();
        var len  = Math.Max(vals.Length, cols.Length);

        // Build a list of matching indices (non-zero or all if requested)
        var matches = new List<int>(Math.Min(len, 256));
        for (var i = 0; i < len; i++)
        {
            // Consider a value 'zero' if the raw controller value equals 0.
            var hasValDisplay = i < vals.Length && vals[i] != 0f;

            // Treat color codes <= 0 as 'no color' (<=0 covers 0=Off and -1=unknown/uninitialized).
            var hasCol = i < cols.Length && cols[i] > 0;

            // If the UI is hiding zero-values, skip entries that both have no visible value and no meaningful color (i.e. both value is zero and color code is <= 0).
            if (!_debugShowZeros && !hasValDisplay && !hasCol)
                continue;

            matches.Add(i);
        }

        var toShow = matches.Count;
        if (!_debugShowAll && toShow > _debugMaxRows)
            toShow = _debugMaxRows;

        if (ImGui.BeginTable("debug_table", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            ImGui.TableSetupColumn("Index");
            ImGui.TableSetupColumn("Ch");
            ImGui.TableSetupColumn("CC/Note");
            ImGui.TableSetupColumn("Value");
            ImGui.TableSetupColumn("ColorCode");

            // Freeze the top row as a sticky header
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            for (var ri = 0; ri < toShow; ri++)
            {
                var idx = matches[ri];
                var ch  = idx / 128;
                var cc  = idx % 128;
                var val = (idx < vals.Length) ? vals[idx] : 0f;
                var col = (idx < cols.Length) ? cols[idx] : 0;

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(idx.ToString());
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(ch.ToString());
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(cc.ToString());
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted($"{Math.Round(val * 100)}%");
                ImGui.TableSetColumnIndex(4);
                var colLabel = col switch
                {
                    -1 => "unknown",
                     0 => "off",
                    _  => col.ToString()
                };
                ImGui.TextUnformatted(colLabel);
            }

            ImGui.EndTable();
        }

        if (!_debugShowAll && matches.Count > _debugMaxRows)
        {
            ImGui.TextUnformatted($"... {matches.Count - _debugMaxRows} more matching controllers hidden. Toggle 'Show all matches' to reveal.");
        }

        ImGui.EndChild();
    }

    internal override IReadOnlyList<T3.Editor.Gui.Windows.Window> GetInstances()
    {
        return new List<T3.Editor.Gui.Windows.Window>();
    }
}
