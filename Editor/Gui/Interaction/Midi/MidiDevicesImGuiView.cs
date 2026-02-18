using System;
using System.Collections.Generic;
using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Interaction.Midi;

internal sealed class MidiDevicesImGuiView : T3.Editor.Gui.Windows.Window
{
    internal MidiDevicesImGuiView()
    {
        Config.Title = "MIDI Devices";
    }

    private readonly Dictionary<string, DeviceUiState> _uiStates = new();
    private double _lastDrawUtc;

    protected override void DrawContent()
    {
        // Throttle UI updates to ~20Hz
        var now = DateTime.UtcNow;
        var nowMs = (now - DateTime.UnixEpoch).TotalMilliseconds;
        if (nowMs - _lastDrawUtc < 50)
        {
            // still draw but don't refresh heavy state
        }
        _lastDrawUtc = nowMs;

        var statuses = CompatibleMidiDeviceHandling.GetConnectedDeviceStatuses();

        if (statuses.Count == 0)
        {
            ImGui.TextUnformatted("No compatible MIDI devices connected.");
            return;
        }

        ImGui.BeginChild("device_list", new System.Numerics.Vector2(-1, -1), false);
        foreach (var s in statuses)
        {
            var blinkOn = ((int)(nowMs / 500)) % 2 == 0; // 500ms blink period
            DrawDeviceReadOnly(s, blinkOn);
            ImGui.Separator();
        }
        ImGui.EndChild();
    }

    private void DrawDeviceReadOnly(MidiDeviceStatus s, bool blinkOn)
    {
        ImGui.PushID(s.ProductName);

        // Header: product and mode
        ImGui.TextUnformatted($"{s.ProductName} ({s.DeviceTypeName})");
        ImGui.SameLine();
        var mode = s.IsInControlMode ? "[Control Mode]" : "[Passthrough]";
        ImGui.TextUnformatted(mode);

        ImGui.Spacing();

        // Visual representation - read-only
        DrawApc40VisualReadOnly(s, blinkOn);

        ImGui.PopID();
    }

    private void DrawApc40VisualReadOnly(MidiDeviceStatus s, bool blinkOn)
    {
        // Use ClipGridSize if known, otherwise fallback to 40 for APC40
        var clipSize = s.ClipGridSize ?? 40;
        var cols = 8;
        var rows = Math.Max(1, clipSize / cols);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new System.Numerics.Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new System.Numerics.Vector2(2, 2));

        var btnSize = new System.Numerics.Vector2(32 * T3Ui.UiScaleFactor, 24 * T3Ui.UiScaleFactor);

        // Use an ImGui Table to align headers and cells exactly and ensure R1 is top
        if (ImGui.BeginTable($"midiGrid_{s.ProductName}", cols + 1, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV))
        {
            // Header row: empty top-left cell + column headers
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted("");
            for (var c = 1; c <= cols; c++)
            {
                ImGui.TableSetColumnIndex(c);
                ImGui.BeginDisabled();
                ImGui.Button(c.ToString(), btnSize);
                ImGui.EndDisabled();
            }

            // Draw rows top-to-bottom, mapping UI row r to device indices r*cols..r*cols+cols-1 (R1 top)
            for (var r = 0; r < rows; r++)
            {
                ImGui.TableNextRow();
                // Row label cell
                ImGui.TableSetColumnIndex(0);
                ImGui.BeginDisabled();
                ImGui.Button($"R{r + 1}", btnSize);
                ImGui.EndDisabled();

                for (var c = 0; c < cols; c++)
                {
                    ImGui.TableSetColumnIndex(c + 1);
                    var idx = r * cols + c; // R1 => idx 0..7
                    var colorCode = idx < s.ControllerColors.Length ? s.ControllerColors[idx] : -1;
                    var col = ColorForApc40ColorCode(colorCode, blinkOn);

                    ImGui.PushStyleColor(ImGuiCol.Button, col);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new System.Numerics.Vector4(Math.Min(col.X * 1.2f, 1f), Math.Min(col.Y * 1.2f, 1f), Math.Min(col.Z * 1.2f, 1f), 1f));
                    ImGui.Button("", btnSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted($"Button {idx} (R{r + 1}C{c + 1})");
                        ImGui.TextUnformatted($"Current color: {colorCode}");
                        ImGui.EndTooltip();
                    }
                    ImGui.PopStyleColor(2);
                }
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(2);

        ImGui.Spacing();

        // Scene launch buttons - try device-specific indices when known
        int[] sceneIndices;
        if (s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // APC40 Mk1 mapping for scene launch is 82..86
            sceneIndices = new[] { 82, 83, 84, 85, 86 };
        }
        else if (s.ClipGridSize.HasValue)
        {
            // fallback: use indices immediately after clip grid
            sceneIndices = new[] { s.ClipGridSize.Value, s.ClipGridSize.Value + 1, s.ClipGridSize.Value + 2, s.ClipGridSize.Value + 3, s.ClipGridSize.Value + 4 };
        }
        else
        {
            sceneIndices = new[] { 40, 41, 42, 43, 44 };
        }

        ImGui.TextUnformatted("Scene Launch:");
        ImGui.SameLine();
        for (var i = 0; i < sceneIndices.Length; i++)
        {
            var idx = sceneIndices[i];
            var colorCode = idx < s.ControllerColors.Length ? s.ControllerColors[idx] : -1;
            var col = ColorForApc40ColorCode(colorCode, blinkOn);
            ImGui.PushStyleColor(ImGuiCol.Button, col);
            ImGui.Button($"S{i + 1}", new System.Numerics.Vector2(28 * T3Ui.UiScaleFactor, 20 * T3Ui.UiScaleFactor));
            ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.NewLine();
    }

    private static System.Numerics.Vector4 ColorForApc40ColorCode(int colorCode, bool blinkOn)
    {
        // Map APC40 Mk1 7-state codes to approximate RGBA colors (pure UI mapping)
        var green = new System.Numerics.Vector4(0.1f, 0.85f, 0.2f, 1f);
        var red = new System.Numerics.Vector4(0.9f, 0.15f, 0.1f, 1f);
        var orange = new System.Numerics.Vector4(0.95f, 0.5f, 0.05f, 1f);
        var off = new System.Numerics.Vector4(0.25f, 0.25f, 0.25f, 1f);
        var dim = new System.Numerics.Vector4(0.15f, 0.15f, 0.15f, 0.8f);

        return colorCode switch
        {
            0 => off,
            1 => green,
            2 => blinkOn ? green : dim,
            3 => red,
            4 => blinkOn ? red : dim,
            5 => orange,
            6 => blinkOn ? orange : dim,
            _ => off
        };
    }

    internal override IReadOnlyList<T3.Editor.Gui.Windows.Window> GetInstances()
    {
        return new List<T3.Editor.Gui.Windows.Window>();
    }

    private sealed class DeviceUiState
    {
        // keep lightweight per-device state (none required for read-only view yet)
    }
}

