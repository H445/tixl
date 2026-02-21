using ImGuiNET;
using static T3.Editor.Gui.Interaction.Midi.MidiLayoutDrawHelpers;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Fallback grid layout for MIDI devices that do not have a dedicated layout view.
/// Renders a simple rows × columns button grid from the device's clip grid size.
/// </summary>
internal static class GenericMidiLayoutView
{
    internal static void Draw(MidiDeviceStatus s)
    {
        var clipSize = s.ClipGridSize ?? 40;
        const int cols = 8;
        var rows    = Math.Max(1, clipSize / cols);
        var btnSize = new Vector2(32 * T3Ui.UiScaleFactor,
                                  24 * T3Ui.UiScaleFactor);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        if (ImGui.BeginTable($"midiGrid_{s.ProductName}", cols + 1,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV))
        {
            // Header row
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

            // Data rows
            for (var r = 0; r < rows; r++)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.BeginDisabled();
                ImGui.Button($"R{r + 1}", btnSize);
                ImGui.EndDisabled();

                for (var c = 0; c < cols; c++)
                {
                    ImGui.TableSetColumnIndex(c + 1);
                    var idx       = r * cols + c;
                    var colorCode = GetColorCode(s, idx);
                    var col       = ColorForClipLaunch(colorCode);

                    ImGui.PushStyleColor(ImGuiCol.Button,        col);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                    ImGui.Button("", btnSize);
                    DrawTooltipIfHovered($"Button {idx} (R{r + 1}C{c + 1})", $"Color: {colorCode}");
                    ImGui.PopStyleColor(2);
                }
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar(2);
    }
}




