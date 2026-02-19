using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Interaction.Midi.CompatibleDevices;
using T3.Editor.Gui.Styling;
using T3.Core.Utils;

namespace T3.Editor.Gui.Interaction.Midi;

internal sealed class MidiDevicesImGuiView : T3.Editor.Gui.Windows.Window
{
    internal MidiDevicesImGuiView()
    {
        Config.Title = "MIDI Devices";
    }

    private double _lastDrawUtc;
    private bool _blinkOn;
    private double _lastBlinkFlipMs;

    protected override void DrawContent()
    {
        var now = DateTime.UtcNow;
        var nowMs = (now - DateTime.UnixEpoch).TotalMilliseconds;
        if (nowMs - _lastDrawUtc < 50) { }
        _lastDrawUtc = nowMs;

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
            DrawDeviceReadOnly(s, _blinkOn);
            ImGui.Separator();
        }
        ImGui.EndChild();
    }

    private void DrawDeviceReadOnly(MidiDeviceStatus s, bool blinkOn)
    {
        ImGui.PushID(s.ProductName);

        ImGui.TextUnformatted($"{s.ProductName} ({s.DeviceTypeName})");
        ImGui.SameLine();
        var mode = s.IsInControlMode ? "[Control Mode]" : "[Passthrough]";
        ImGui.TextUnformatted(mode);

        ImGui.Spacing();

        if (s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0)
            DrawApc40Mk1Layout(s, blinkOn);
        else
            DrawGenericGrid(s, blinkOn);

        ImGui.PopID();
    }

    #region Shared Helpers

    /// <summary>Safe color code lookup from ControllerColors array.</summary>
    private static int GetColorCode(MidiDeviceStatus s, int noteId)
        => noteId >= 0 && noteId < s.ControllerColors.Length ? s.ControllerColors[noteId] : -1;

    /// <summary>Draws a tooltip on the last item if hovered.</summary>
    private static void DrawTooltipIfHovered(string text)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    /// <summary>Draws a tooltip with two lines on the last item if hovered.</summary>
    private static void DrawTooltipIfHovered(string line1, string line2)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(line1);
        ImGui.TextUnformatted(line2);
        ImGui.EndTooltip();
    }

    /// <summary>Draws a colored LED button with tooltip.</summary>
    private static void DrawLedButton(string label, int noteId, Vector4 col, Vector2 size, string tooltip)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, col);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
        ImGui.Button($"{label}##{noteId}", size);
        DrawTooltipIfHovered(tooltip);
        ImGui.PopStyleColor(2);
    }

    /// <summary>Draws an icon button with background color based on LED state.</summary>
    private static void DrawIconButton(MidiDeviceStatus s, Icon icon, int noteId, Vector2 size, bool blinkOn, string tooltip)
    {
        var colorCode = GetColorCode(s, noteId);
        var state = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol = ColorForSimpleLed(colorCode, blinkOn);
        DrawIconButtonWithBg(icon, size, bgCol, state);
        DrawTooltipIfHovered(tooltip);
    }

    /// <summary>Draws a transport Stop button with a square shape.</summary>
    private static void DrawStopButton(int noteId, MidiDeviceStatus s, Vector2 size, bool blinkOn)
    {
        var colorCode = GetColorCode(s, noteId);
        var state = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol = ColorForSimpleLed(colorCode, blinkOn);

        DrawStyledButton(noteId, bgCol, state, size, () =>
        {
            var sysColor = GetStateColorVec(state);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var center = (min + max) / 2f;
            var dl = ImGui.GetWindowDrawList();
            DrawStopShape(dl, center, Icons.FontSize, ImGui.GetColorU32(sysColor));
        });
    }

    /// <summary>Draws a transport Record button with a circle shape.</summary>
    private static void DrawRecordButton(int noteId, MidiDeviceStatus s, Vector2 size, bool blinkOn)
    {
        var colorCode = GetColorCode(s, noteId);
        var state = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol = ColorForSimpleLed(colorCode, blinkOn);

        DrawStyledButton(noteId, bgCol, state, size, () =>
        {
            var sysColor = GetStateColorVec(state);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var center = (min + max) / 2f;
            var dl = ImGui.GetWindowDrawList();
            DrawRecordShape(dl, center, Icons.FontSize, ImGui.GetColorU32(sysColor));
        });
    }

    private static void DrawStopShape(ImDrawListPtr dl, Vector2 center, float iconSize, uint color)
    {
        var iconMin = new Vector2(center.X - iconSize / 2f, center.Y - iconSize / 2f).Floor();
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        dl.AddRectFilled(iconMin, iconMax, color, 2f);
    }

    private static void DrawRecordShape(ImDrawListPtr dl, Vector2 center, float iconSize, uint color)
    {
        dl.AddCircleFilled(center.Floor(), iconSize / 2f, color, 16);
    }

    /// <summary>Helper to draw a single button with LED state from ControllerColors.</summary>
    private void DrawSimpleButton(MidiDeviceStatus s, string label, int noteId, Vector2 size, bool blinkOn, string tooltipLabel)
    {
        var col = ColorForSimpleLed(GetColorCode(s, noteId), blinkOn);
        DrawLedButton(label, noteId, col, size, $"{tooltipLabel} (Note {noteId})");
    }

    private static void DrawIconButtonWithBg(Icon icon, Vector2 size, Vector4 bgCol, CustomComponents.ButtonStates state)
    {
        DrawStyledButton((int)icon, bgCol, state, size, () => Icons.DrawIconOnLastItem(icon, GetStateColorVec(state)));
    }

    /// <summary>
    /// Pushes the common button styling, emits an invisible button and invokes the drawContent action
    /// to render either an icon or a custom shape on top of the button rectangle.
    /// </summary>
    private static void DrawStyledButton(object idKey, Vector4 bgCol, CustomComponents.ButtonStates state, Vector2 size, Action drawContent)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Button, bgCol);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(bgCol, 1.2f));

        ImGui.PushID(idKey?.ToString() ?? string.Empty);
        ImGui.Button(string.Empty, size);
        ImGui.PopID();

        drawContent();

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(1);
    }

    #endregion

    #region APC40 MK1 Full Hardware Layout

    /// <summary>
    /// Draws the full APC40 MK1 physical layout matching the actual hardware.
    /// The physical APC40 has two side-by-side panels:
    ///
    /// LEFT PANEL (top to bottom):
    ///   1. Clip Launch Grid (8x5) + Scene Launch (right column)
    ///   4. Clip Stop row + Stop All Clips
    ///   7. Track Selection faders (8 + Master)
    ///  10. Activator row
    ///  11. Solo/Cue row
    ///  12. Record Arm row
    ///  14. Cue Level knob
    ///  13. Channel Faders (8 + Master)
    ///
    /// RIGHT PANEL (top to bottom):
    ///   6. Track Control knobs (2 rows of 4) + PAN/SEND A/SEND B/SEND C
    ///   2. Bank Select / Navigation (arrows + display)
    ///  19. Tap Tempo
    ///  18. Nudge -/+
    ///   8. Device Control knobs (2 rows of 4)
    ///   9. Device Control buttons (2 rows of 4)
    ///  15. Transport (Play, Stop, Rec)
    ///  16. Crossfader
    /// </summary>
    private void DrawApc40Mk1Layout(MidiDeviceStatus s, bool blinkOn)
    {
        var scale = T3Ui.UiScaleFactor;
        var smallBtnSize = new Vector2(26 * scale, 18 * scale);
        var knobSize = new Vector2(24 * scale, 24 * scale);
        var clipBtnSize = new Vector2(30 * scale, 22 * scale);
        var sceneBtnSize = smallBtnSize;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3 * scale, 3 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        // Two-column table for left and right panels side-by-side
        if (ImGui.BeginTable("apc40_panels", 2, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
        {
            ImGui.TableNextRow();

            // ========== LEFT PANEL ==========
            ImGui.TableSetColumnIndex(0);
            ImGui.BeginChild("apc40_left", new Vector2(340 * scale, 0), false);

            // draw left panel as a single 9-column table to align every column vertically
            DrawLeftPanelAligned(s, blinkOn, clipBtnSize, smallBtnSize, knobSize, scale);

            ImGui.EndChild();

            // ========== RIGHT PANEL ==========
            ImGui.TableSetColumnIndex(1);
            ImGui.BeginChild("apc40_right", new Vector2(200 * scale, 0), false);

            DrawSectionLabel("TRACK CONTROL");
            DrawKnobGrid("TrkKnob", 48, 4, 2, knobSize, new Vector4(0.3f, 0.5f, 0.7f, 1f));
            DrawModeKnobLabels(smallBtnSize, s);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawSectionLabel("BANK SELECT");
            DrawBankSelectNavigation(s, blinkOn, scale);

            ImGui.Spacing();

            DrawSimpleButton(s, "TAP TEMPO", 99, new Vector2(70 * scale, smallBtnSize.Y), blinkOn, "Tap Tempo");

            ImGui.Spacing();

            DrawSimpleButton(s, "NUD-", 100, smallBtnSize, blinkOn, "Nudge -");
            ImGui.SameLine();
            DrawSimpleButton(s, "NUD+", 101, smallBtnSize, blinkOn, "Nudge +");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawSectionLabel("DEVICE CONTROL");
            DrawKnobGrid("DevKnob", 16, 4, 2, knobSize, new Vector4(0.5f, 0.4f, 0.6f, 1f));

            ImGui.Spacing();

            DrawDeviceControlButtons(s, blinkOn, smallBtnSize);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawTransportButtons(s, blinkOn, scale);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawCrossfader(scale);

            ImGui.EndChild();

            ImGui.EndTable();
        }

        ImGui.PopStyleVar(2);
    }

    private static void DrawSectionLabel(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
    }

    /// <summary>Draws knobs in a grid layout (e.g. 2 rows of 4).</summary>
    private static void DrawKnobGrid(string idPrefix, int ccStart, int cols, int rows, Vector2 size, Vector4 color)
    {
        var hoverColor = BrightenColor(color, 1.15f);
        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                if (c > 0) ImGui.SameLine();
                var idx = r * cols + c;
                var cc = ccStart + idx;
                ImGui.PushStyleColor(ImGuiCol.Button, color);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
                ImGui.Button($"{idx + 1}##{idPrefix}{cc}", size);
                DrawTooltipIfHovered($"{idPrefix} {idx + 1} (CC {cc})");
                ImGui.PopStyleColor(2);
            }
        }
    }

    /// <summary>Draws the PAN / SEND A / SEND B / SEND C mode labels below the Track Control knobs.</summary>
    private void DrawModeKnobLabels(Vector2 btnSize, MidiDeviceStatus s)
    {
        var isApc40 = s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isApc40) return;

        var modeNotes = Apc40Mk1.ModeButtonNoteOrder;
        var modeLabels = Apc40Mk1.ModeButtonLabels;
        for (var i = 0; i < modeNotes.Length && i < modeLabels.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            var noteId = modeNotes[i];
            var colorCode = GetColorCode(s, noteId);
            var col = colorCode > 0 ? _greenColor : _offColor;
            DrawLedButton(modeLabels[i], noteId, col, btnSize, $"{modeLabels[i]} (Note {noteId})");
        }
    }

    /// <summary>Draws the Bank Select / Navigation section with arrow pad.</summary>
    private void DrawBankSelectNavigation(MidiDeviceStatus s, bool blinkOn, float scale)
    {
        var navBtnSize = new Vector2(30 * scale, 20 * scale);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + navBtnSize.X + 3 * scale);
        DrawIconButton(s, Icon.ArrowUp, 94, navBtnSize, blinkOn, "Bank Up (Note 94)");

        DrawIconButton(s, Icon.ArrowLeft, 97, navBtnSize, blinkOn, "Bank Left (Note 97)");
        ImGui.SameLine();
        DrawSimpleButton(s, "SHIFT", 98, navBtnSize, blinkOn, "Shift");
        ImGui.SameLine();
        DrawIconButton(s, Icon.ArrowRight, 96, navBtnSize, blinkOn, "Bank Right (Note 96)");

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + navBtnSize.X + 3 * scale);
        DrawIconButton(s, Icon.ArrowDown, 95, navBtnSize, blinkOn, "Bank Down (Note 95)");
    }

    /// <summary>Draws the Track Selection fader indicators (8 tracks + Master).</summary>
    private static void DrawTrackSelectionFaders(float scale)
    {
        var faderSize = new Vector2(26 * scale, 12 * scale);
        for (var i = 0; i < 8; i++)
        {
            if (i > 0) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.Button($"{i + 1}##trksel{i}", faderSize);
            DrawTooltipIfHovered($"Track Select {i + 1}");
            ImGui.PopStyleColor();
        }
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.3f, 1f));
        ImGui.Button("MST##trkselM", faderSize);
        DrawTooltipIfHovered("Master");
        ImGui.PopStyleColor();
    }

    /// <summary>Draws Device Control buttons in 2 rows of 4.</summary>
    private void DrawDeviceControlButtons(MidiDeviceStatus s, bool blinkOn, Vector2 btnSize)
    {
        var isApc40 = s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0;
        var notes = isApc40 ? Apc40Mk1.DeviceControlNoteOrder : _fallbackDeviceControlNotes;
        var labels = isApc40 ? Apc40Mk1.DeviceControlLabels : _fallbackDeviceControlLabels;

        for (var i = 0; i < notes.Length && i < labels.Length; i++)
        {
            if (i > 0 && i % 4 != 0) ImGui.SameLine();
            var noteId = notes[i];
            var label = labels[i];

            switch (noteId)
            {
                case 60:
                    DrawIconButton(s, Icon.ChevronLeft, noteId, btnSize, blinkOn, $"Device Left (Note {noteId})");
                    break;
                case 61:
                    DrawIconButton(s, Icon.ChevronRight, noteId, btnSize, blinkOn, $"Device Right (Note {noteId})");
                    break;
                default:
                    DrawSimpleButton(s, label, noteId, btnSize, blinkOn, label);
                    break;
            }
        }
    }

    /// <summary>Draws Transport buttons (Play, Stop, Record).</summary>
    private void DrawTransportButtons(MidiDeviceStatus s, bool blinkOn, float scale)
    {
        DrawSectionLabel("TRANSPORT");

        var transportBtnSize = new Vector2(40 * scale, 22 * scale);

        {
            var colorCode = GetColorCode(s, 91);
            var state = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
            var bgCol = colorCode > 0 ? new Vector4(0.1f, 0.85f, 0.2f, 1f) : _offColor;
            DrawIconButtonWithBg(Icon.PlayForwards, transportBtnSize, bgCol, state);
            DrawTooltipIfHovered("Play (Note 91)");
        }

        ImGui.SameLine();
        DrawStopButton(92, s, transportBtnSize, blinkOn);
        DrawTooltipIfHovered("Stop (Note 92)");

        ImGui.SameLine();
        DrawRecordButton(93, s, transportBtnSize, blinkOn);
        DrawTooltipIfHovered("Record (Note 93)");
    }

    /// <summary>Draws the A-B Crossfader indicator.</summary>
    private static void DrawCrossfader(float scale)
    {
        var crossfaderSize = new Vector2(140 * scale, 18 * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.4f, 1f));
        ImGui.Button("A \u25c4\u2500\u2500 CROSSFADER \u2500\u2500\u25ba B##xfader", crossfaderSize);
        DrawTooltipIfHovered("A-B Crossfader (CC 15)");
        ImGui.PopStyleColor();
    }

    /// <summary>Draws fader indicators (8 channel + Master).</summary>
    private static void DrawFaders(float scale)
    {
        DrawSectionLabel("FADERS");

        var faderBtnSize = new Vector2(26 * scale, 50 * scale);

        for (var i = 0; i < 8; i++)
        {
            if (i > 0) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.Button($"F{i + 1}##fader{i}", faderBtnSize);
            DrawTooltipIfHovered($"Track Fader {i + 1} (CC 7, Ch {i + 1})");
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.3f, 1f));
        ImGui.Button("MST##faderM", faderBtnSize);
        DrawTooltipIfHovered("Master Fader (CC 14)");
        ImGui.PopStyleColor();
    }

    /// <summary>Draws the 8x5 clip launch grid with scene launch buttons on the right.</summary>
    private void DrawClipGridWithSceneLaunch(MidiDeviceStatus s, bool blinkOn, Vector2 clipBtnSize, Vector2 sceneBtnSize, float scale)
    {
        const int cols = 8;
        const int rows = 5;
        var tableId = $"clipGrid_{s.ProductName}";

        if (!ImGui.BeginTable(tableId, cols + 2,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            return;

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("");

        for (var c = 1; c <= cols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.3f, 1f));
            ImGui.Button($"T{c}", clipBtnSize);
            ImGui.PopStyleColor();
        }

        ImGui.TableSetColumnIndex(cols + 1);
        DrawSectionLabel("SCENE");

        for (var r = 0; r < rows; r++)
        {
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.3f, 1f));
            ImGui.Button($"R{r + 1}", new Vector2(24 * scale, clipBtnSize.Y));
            ImGui.PopStyleColor();

            for (var c = 0; c < cols; c++)
            {
                ImGui.TableSetColumnIndex(c + 1);
                var idx = r * cols + c;
                var colorCode = GetColorCode(s, idx);
                var col = ColorForClipLaunch(colorCode, blinkOn);

                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"##{idx}", clipBtnSize);
                DrawTooltipIfHovered($"Clip Launch R{r + 1}C{c + 1} (idx {idx})", $"Color: {ColorCodeName(colorCode)}");
                ImGui.PopStyleColor(2);
            }

            ImGui.TableSetColumnIndex(cols + 1);
            var sceneIdx = 82 + r;
            var sceneCol = ColorForSimpleLed(GetColorCode(s, sceneIdx), blinkOn);
            DrawLedButton($"S{r + 1}", sceneIdx, sceneCol, sceneBtnSize, $"Scene Launch {r + 1} (Note {sceneIdx})");
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(cols + 1);
        var stopAllCol = ColorForSimpleLed(GetColorCode(s, 81), blinkOn);
        ImGui.PushStyleColor(ImGuiCol.Button, stopAllCol);
        ImGui.Button("STOP##81", sceneBtnSize);
        DrawTooltipIfHovered("Stop All Clips (Note 81)");
        ImGui.PopStyleColor();

        ImGui.EndTable();
    }

    private void DrawTrackButtonRow(MidiDeviceStatus s, bool blinkOn, string label,
        int startIdx, Vector2 btnSize, Vector2 labelSize,
        bool hasPerTrackIndices, string tooltip)
    {
        const int tracks = 8;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 1f));
        ImGui.Button(label, labelSize);
        ImGui.PopStyleColor();

        for (var i = 0; i < tracks; i++)
        {
            ImGui.SameLine();
            var noteIdx = hasPerTrackIndices ? startIdx + i : startIdx;
            var colorCode = hasPerTrackIndices || i == 0 ? GetColorCode(s, noteIdx) : -1;
            var col = ColorForSimpleLed(colorCode, blinkOn);

            ImGui.PushStyleColor(ImGuiCol.Button, col);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
            ImGui.Button($"{i + 1}##{label}{i}", btnSize);
            DrawTooltipIfHovered(
                $"{label} Track {i + 1}",
                hasPerTrackIndices ? $"Note {noteIdx}, Color: {colorCode}" : tooltip);
            ImGui.PopStyleColor(2);
        }
    }

    // Fallback data for non-APC40 devices
    private static readonly int[] _fallbackDeviceControlNotes = { 58, 59, 60, 61, 62, 63, 64, 65 };
    private static readonly string[] _fallbackDeviceControlLabels = { "Dev\u25c4", "Dev\u25ba", "Bnk\u25c4", "Bnk\u25ba", "On/Off", "Lock", "Clip/D", "Detail" };
    private static readonly (string Label, int NoteId, Vector4 ActiveColor)[] _fallbackModeButtons =
    {
        ("PAN", 87, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
        ("SEND", 88, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
        ("USER", 89, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
        ("METRO", 90, new Vector4(0.7f, 0.4f, 0.2f, 1f)),
    };

    #endregion

    #region Generic Grid Fallback

    /// <summary>Fallback grid view for non-APC40 devices.</summary>
    private void DrawGenericGrid(MidiDeviceStatus s, bool blinkOn)
    {
        var clipSize = s.ClipGridSize ?? 40;
        var cols = 8;
        var rows = Math.Max(1, clipSize / cols);
        var btnSize = new Vector2(32 * T3Ui.UiScaleFactor, 24 * T3Ui.UiScaleFactor);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        if (ImGui.BeginTable($"midiGrid_{s.ProductName}", cols + 1, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersInnerV))
        {
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
                    var idx = r * cols + c;
                    var colorCode = GetColorCode(s, idx);
                    var col = ColorForClipLaunch(colorCode, blinkOn);

                    ImGui.PushStyleColor(ImGuiCol.Button, col);
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

    #endregion

    #region Color Mapping

    private static readonly Vector4 _greenColor = new(0.1f, 0.85f, 0.2f, 1f);
    private static readonly Vector4 _redColor = new(0.9f, 0.15f, 0.1f, 1f);
    private static readonly Vector4 _yellowColor = new(0.95f, 0.75f, 0.05f, 1f);
    private static readonly Vector4 _offColor = new(0.25f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 _dimColor = new(0.15f, 0.15f, 0.15f, 0.8f);

    private static Vector4 ColorForClipLaunch(int colorCode, bool blinkOn)
    {
        return colorCode switch
        {
            0 => _offColor,
            1 => _greenColor,
            2 => blinkOn ? _greenColor : _dimColor,
            3 => _redColor,
            4 => blinkOn ? _redColor : _dimColor,
            5 => _yellowColor,
            6 => blinkOn ? _yellowColor : _dimColor,
            >= 7 => _greenColor,
            _ => _offColor
        };
    }

    private static Vector4 ColorForSimpleLed(int colorCode, bool blinkOn)
    {
        return colorCode switch
        {
            0 => _offColor,
            2 => blinkOn ? _greenColor : _dimColor,
            >= 1 => _greenColor,
            _ => _offColor
        };
    }

    private static string ColorCodeName(int code)
    {
        return code switch
        {
            0 => "Off",
            1 => "Green",
            2 => "Green Blink",
            3 => "Red",
            4 => "Red Blink",
            5 => "Yellow",
            6 => "Yellow Blink",
            >= 7 => $"Green ({code})",
            _ => $"Unknown ({code})"
        };
    }

    private static Vector4 BrightenColor(Vector4 col, float factor)
    {
        return new Vector4(
            Math.Min(col.X * factor, 1f),
            Math.Min(col.Y * factor, 1f),
            Math.Min(col.Z * factor, 1f),
            col.W);
    }

    #endregion

    private static Vector4 GetStateColorVec(CustomComponents.ButtonStates state)
    {
        return state switch
        {
            CustomComponents.ButtonStates.Dimmed => UiColors.Text.Fade(0.8f).Rgba,
            CustomComponents.ButtonStates.Disabled => UiColors.TextDisabled.Fade(0.6f).Rgba,
            CustomComponents.ButtonStates.Activated => UiColors.StatusActivated.Rgba,
            CustomComponents.ButtonStates.NeedsAttention => UiColors.StatusAttention.Rgba,
            _ => UiColors.Text.Rgba
        };
    }

    internal override IReadOnlyList<T3.Editor.Gui.Windows.Window> GetInstances()
    {
        return new List<T3.Editor.Gui.Windows.Window>();
    }

    /// <summary>
    /// Draw the left side as a 9-column table: 8 clip columns + 1 scene/control column.
    /// Ensures every column lines up vertically. The 9th column stacks scene launch buttons,
    /// Stop All, Master track select, Cue knob (centered between activator/solo/rec) and Master fader.
    /// </summary>
    private void DrawLeftPanelAligned(MidiDeviceStatus s, bool blinkOn, Vector2 clipBtnSize, Vector2 smallBtnSize, Vector2 knobSize, float scale)
    {
        const int clipCols = 8;
        const int columns = clipCols + 1; // extra column for scene/control
        const int clipRows = 5;

        if (!ImGui.BeginTable("left_panel_table_" + s.ProductName, columns, ImGuiTableFlags.SizingFixedFit))
            return;

        // ensure every clip column has the same fixed width so rows align perfectly
        for (var cc = 0; cc < clipCols; cc++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, clipBtnSize.X);
        // scene/control column uses same width as clip columns so the stack lines up
        ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, clipBtnSize.X);

        // unified widths (use clip button width for all buttons; vary height as appropriate)
        var btnW = clipBtnSize.X;
        var clipH = clipBtnSize.Y;
        var smallH = smallBtnSize.Y;
        var knobH = knobSize.Y;
        var faderH = 50 * scale;

        // Header row (empty)
        ImGui.TableNextRow();
        for (var c = 0; c < columns; c++)
        {
            ImGui.TableSetColumnIndex(c);
            ImGui.TextUnformatted("");
        }

        // === Clip grid rows with scene buttons in last column ===
        for (var r = 0; r < clipRows; r++)
        {
            ImGui.TableNextRow();
            for (var c = 0; c < clipCols; c++)
            {
                ImGui.TableSetColumnIndex(c);
                var idx = r * clipCols + c; // 0..39
                var colorCode = GetColorCode(s, idx);
                var col = ColorForClipLaunch(colorCode, blinkOn);

                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"##clip{idx}", new Vector2(btnW, clipH));
                DrawTooltipIfHovered($"Clip Launch R{r + 1}C{c + 1} (idx {idx})", $"Color: {ColorCodeName(colorCode)}");
                ImGui.PopStyleColor(2);
            }

            // scene button in last column - use clipBtnSize width so it lines up with the clip grid rows
            ImGui.TableSetColumnIndex(clipCols);
            var sceneIdx = 82 + r; // 82..86
            var sceneCol = ColorForSimpleLed(GetColorCode(s, sceneIdx), blinkOn);
            DrawLedButton($"S{r + 1}", sceneIdx, sceneCol, new Vector2(btnW, clipH), $"Scene Launch {r + 1} (Note {sceneIdx})");
        }

        // === Clip Stop row: 8 clip stop buttons + Stop All in last column ===
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var note = 52 + c; // clip stop notes 52..59
            var col = ColorForSimpleLed(GetColorCode(s, note), blinkOn);
            DrawLedButton((c + 1).ToString(), note, col, new Vector2(btnW, smallH), $"Clip Stop Track {c + 1} (Note {note})");
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.PushStyleColor(ImGuiCol.Button, ColorForSimpleLed(GetColorCode(s, 81), blinkOn));
        ImGui.Button("STOP ALL", new Vector2(btnW, smallH));
        DrawTooltipIfHovered("Stop All Clips (Note 81)");
        ImGui.PopStyleColor();

        // spacer row
        ImGui.TableNextRow();
        for (var c = 0; c < columns; c++) { ImGui.TableSetColumnIndex(c); ImGui.TextUnformatted(""); }

        // === Track Selection row: 8 small selectors + Master select in last column ===
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 1f));
            ImGui.Button($"{c + 1}##trksel{c}", new Vector2(btnW, smallH));
            ImGui.PopStyleColor();
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.4f, 1f));
        ImGui.Button("MST##trkselM", new Vector2(btnW, smallH));
        DrawTooltipIfHovered("Master Track Select");
        ImGui.PopStyleColor();

        // === Activator / Solo / Rec Arm rows with cue knob centered in middle row (solo) ===
        // Activator
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var note = 66 + c; // activator 66..73
            var col = ColorForSimpleLed(GetColorCode(s, note), blinkOn);
            DrawLedButton("A", note, col, new Vector2(btnW, smallH), $"Activator Track {c + 1}");
        }
        ImGui.TableSetColumnIndex(clipCols);
        // empty cell for activator row
        ImGui.TextUnformatted("");

        // Solo/Cue (middle row) - draw cue knob in last column to center it between the three rows
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            // Keep single-note LED behavior for now (note 49)
            var col = ColorForSimpleLed(GetColorCode(s, 49), blinkOn);
            DrawLedButton((c + 1).ToString(), 49, col, new Vector2(btnW, smallH), $"Solo/Cue Track {c + 1}");
        }
        ImGui.TableSetColumnIndex(clipCols);
        // cue knob centered here - make sure it fits the scene column width
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.5f, 1f));
        ImGui.Button("CUE", new Vector2(btnW, knobH));
        DrawTooltipIfHovered("Cue Level (CC 47)");
        ImGui.PopStyleColor();

        // Record Arm
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var note = 48 + c; // record arm 48..55
            var col = ColorForSimpleLed(GetColorCode(s, note), blinkOn);
            DrawLedButton("R", note, col, new Vector2(btnW, smallH), $"Record Arm Track {c + 1}");
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.TextUnformatted("");

        // spacer row before faders
        ImGui.TableNextRow();
        for (var c = 0; c < columns; c++) { ImGui.TableSetColumnIndex(c); ImGui.TextUnformatted(""); }

        // === Faders row: 8 channel faders and Master fader in last column ===
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.Button($"F{c + 1}##fader{c}", new Vector2(btnW, faderH));
            ImGui.PopStyleColor();
        }
        // Master fader in last column - use same width as clip columns
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.3f, 1f));
        ImGui.Button("MST##faderM", new Vector2(btnW, faderH));
        ImGui.PopStyleColor();

        ImGui.EndTable();
    }

}

