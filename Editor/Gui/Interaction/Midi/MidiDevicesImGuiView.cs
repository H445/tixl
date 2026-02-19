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

    /// <summary>
    /// Draws a colored LED button with tooltip.
    /// </summary>
    private static void DrawLedButton(string label, int noteId, Vector4 col, Vector2 size, string tooltip)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, col);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
        ImGui.Button($"{label}##{noteId}", size);
        DrawTooltipIfHovered(tooltip);
        ImGui.PopStyleColor(2);
    }

    /// <summary>
    /// Draws an icon button with background color based on LED state.
    /// </summary>
    private static void DrawIconButton(MidiDeviceStatus s, Icon icon, int noteId, Vector2 size, bool blinkOn, string tooltip)
    {
        var colorCode = GetColorCode(s, noteId);
        var state = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol = ColorForSimpleLed(colorCode, blinkOn);
        DrawIconButtonWithBg(icon, size, bgCol, state);
        DrawTooltipIfHovered(tooltip);
    }

    /// <summary>
    /// Draws a transport button with a custom shape (square for Stop, circle for Record)
    /// rendered via the draw list.
    /// </summary>
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

    #endregion

    #region APC40 MK1 Full Hardware Layout

    /// <summary>
    /// Draws the full APC40 MK1 physical layout matching the actual hardware.
    /// Layout (top to bottom):
    ///   1. Track Control Knobs (top row of 8 rotary encoders)
    ///   2. Device Control Knobs (8 knobs, banked per track selection)
    ///   3. Clip Launch Grid (8 columns x 5 rows) + Scene Launch buttons (right column)
    ///   4. Track Control Buttons (Clip Stop, Solo, Activator, Record Arm, Track Select)
    ///   5. Device Control Buttons + Mode Buttons
    ///   6. Transport + Navigation
    ///   7. Faders (8 channel + Master + Crossfader + Cue + Tempo).
    /// </summary>
    private void DrawApc40Mk1Layout(MidiDeviceStatus s, bool blinkOn)
    {
        var scale = T3Ui.UiScaleFactor;
        var clipBtnSize = new Vector2(30 * scale, 22 * scale);
        var smallBtnSize = new Vector2(26 * scale, 18 * scale);
        var knobSize = new Vector2(24 * scale, 24 * scale);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3 * scale, 3 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));

        // === 1. TRACK CONTROL KNOBS (top row) ===
        DrawSectionLabel("TRACK CONTROL");
        DrawKnobRow("TrkKnob", 48, 8, knobSize, new Vector4(0.3f, 0.5f, 0.7f, 1f));

        ImGui.Spacing();

        // === 2. DEVICE CONTROL KNOBS (banked per track) ===
        DrawSectionLabel("DEVICE CONTROL");
        DrawKnobRow("DevKnob", 16, 8, knobSize, new Vector4(0.5f, 0.4f, 0.6f, 1f));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === 3. CLIP LAUNCH GRID + SCENE LAUNCH ===
        DrawClipGridWithSceneLaunch(s, blinkOn, clipBtnSize, smallBtnSize, scale);

        ImGui.Spacing();

        // === 4. TRACK CONTROL BUTTONS (below grid) ===
        DrawTrackControlButtons(s, blinkOn, smallBtnSize, scale);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === 5. DEVICE CONTROL BUTTONS + MODE BUTTONS ===
        DrawDeviceAndModeButtons(s, blinkOn, smallBtnSize);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === 6. TRANSPORT + NAVIGATION ===
        DrawTransportAndNavigation(s, blinkOn, smallBtnSize, scale);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // === 7. FADERS ===
        DrawFaders(scale);

        ImGui.PopStyleVar(2);
    }

    private static void DrawSectionLabel(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Draws a row of knob indicators. Knobs don't have LED color state in ControllerColors,
    /// so they're shown as static indicators with their CC number.
    /// </summary>
    private static void DrawKnobRow(string idPrefix, int ccStart, int count, Vector2 size, Vector4 color)
    {
        var hoverColor = BrightenColor(color, 1.15f);
        for (var i = 0; i < count; i++)
        {
            if (i > 0) ImGui.SameLine();
            var cc = ccStart + i;
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoverColor);
            ImGui.Button($"K{i + 1}##{idPrefix}{cc}", size);
            DrawTooltipIfHovered($"{idPrefix} {i + 1} (CC {cc})");
            ImGui.PopStyleColor(2);
        }
    }

    /// <summary>
    /// Draws the 8x5 clip launch grid, with scene launch buttons on the right side
    /// and Stop All Clips button below the scene launch column.
    /// </summary>
    private void DrawClipGridWithSceneLaunch(MidiDeviceStatus s, bool blinkOn, Vector2 clipBtnSize, Vector2 sceneBtnSize, float scale)
    {
        const int cols = 8;
        const int rows = 5;
        var tableId = $"clipGrid_{s.ProductName}";

        // Table: Row label + 8 clip columns + Scene Launch column
        if (!ImGui.BeginTable(tableId, cols + 2,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            return;

        // Column headers
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

        // Clip grid rows (R1 at top)
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
                var idx = r * cols + c; // index 0-39
                var colorCode = GetColorCode(s, idx);
                var col = ColorForClipLaunch(colorCode, blinkOn);

                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"##{idx}", clipBtnSize);
                DrawTooltipIfHovered($"Clip Launch R{r + 1}C{c + 1} (idx {idx})", $"Color: {ColorCodeName(colorCode)}");
                ImGui.PopStyleColor(2);
            }

            // Scene Launch button (right column)
            ImGui.TableSetColumnIndex(cols + 1);
            var sceneIdx = 82 + r; // Scene Launch 1-5 = notes 82-86
            var sceneCol = ColorForSimpleLed(GetColorCode(s, sceneIdx), blinkOn);
            DrawLedButton($"S{r + 1}", sceneIdx, sceneCol, sceneBtnSize, $"Scene Launch {r + 1} (Note {sceneIdx})");
        }

        // Stop All Clips row
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(cols + 1);
        var stopAllCol = ColorForSimpleLed(GetColorCode(s, 81), blinkOn);
        ImGui.PushStyleColor(ImGuiCol.Button, stopAllCol);
        ImGui.Button("STOP##81", sceneBtnSize);
        DrawTooltipIfHovered("Stop All Clips (Note 81)");
        ImGui.PopStyleColor();

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws per-track control button rows below the clip grid:
    /// Clip Stop, Solo, Activator (A-B), Record Arm, Track Select
    /// </summary>
    private void DrawTrackControlButtons(MidiDeviceStatus s, bool blinkOn, Vector2 btnSize, float scale)
    {
        var labelWidth = new Vector2(60 * scale, btnSize.Y);

        DrawTrackButtonRow(s, blinkOn, "CLIP STOP", 52, btnSize, labelWidth, true, "Clip Stop (Note 52-59)");
        DrawTrackButtonRow(s, blinkOn, "TRK SEL", 51, btnSize, labelWidth, false, "Track Select (Note 51, Ch 0-7 in Ableton)");
        DrawTrackButtonRow(s, blinkOn, "ACTIVATOR", 66, btnSize, labelWidth, true, "Activator (Note 66-73)");
        DrawTrackButtonRow(s, blinkOn, "SOLO", 49, btnSize, labelWidth, false, "Solo (Note 49, Ch 0-7 in Ableton)");
        DrawTrackButtonRow(s, blinkOn, "REC ARM", 48, btnSize, labelWidth, false, "Record Arm (Note 48, Ch 0-7 in Ableton)");
        
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

    /// <summary>
    /// Draws Device Control buttons and Mode buttons.
    /// Device Control: Dev◄(58), Dev►(59), Bnk◄(60), Bnk►(61),
    ///                 On/Off(62), Lock(63), Clip/D(64), Detail(65)
    /// Mode: Pan(87), Sends(88), User(89), Metronome(90)
    /// </summary>
    private void DrawDeviceAndModeButtons(MidiDeviceStatus s, bool blinkOn, Vector2 btnSize)
    {
        DrawSectionLabel("DEVICE CONTROL");

        // Determine button layout — APC40 uses ordered mapping, fallback uses hardcoded
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
                case 58:
                    DrawIconButton(s, Icon.ChevronLeft, noteId, btnSize, blinkOn, $"Device Left (Note {noteId})");
                    break;
                case 59:
                    DrawIconButton(s, Icon.ChevronRight, noteId, btnSize, blinkOn, $"Device Right (Note {noteId})");
                    break;
                default:
                    DrawSimpleButton(s, label, noteId, btnSize, blinkOn, label);
                    break;
            }
        }

        ImGui.Spacing();
        DrawSectionLabel("MODE");

        if (isApc40)
        {
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
        else
        {
            for (var i = 0; i < _fallbackModeButtons.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                var (label, noteId, activeColor) = _fallbackModeButtons[i];
                var colorCode = GetColorCode(s, noteId);
                var col = colorCode > 0 ? activeColor : _offColor;
                DrawLedButton(label, noteId, col, btnSize, $"{label} (Note {noteId})");
            }
        }
    }

    // Fallback data for non-APC40 devices (avoids per-frame allocations)
    private static readonly int[] _fallbackDeviceControlNotes = { 58, 59, 60, 61, 62, 63, 64, 65 };
    private static readonly string[] _fallbackDeviceControlLabels = { "Dev◄", "Dev►", "Bnk◄", "Bnk►", "On/Off", "Lock", "Clip/D", "Detail" };
    private static readonly (string Label, int NoteId, Vector4 ActiveColor)[] _fallbackModeButtons =
    {
        ("PAN", 87, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
        ("SEND", 88, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
        ("USER", 89, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
        ("METRO", 90, new Vector4(0.7f, 0.4f, 0.2f, 1f)),
    };

    /// <summary>
    /// Draws Transport controls and Navigation.
    /// </summary>
    private void DrawTransportAndNavigation(MidiDeviceStatus s, bool blinkOn, Vector2 btnSize, float scale)
    {
        DrawSectionLabel("TRANSPORT");

        var transportBtnSize = new Vector2(50 * scale, 22 * scale);

        // Play
        {
            var colorCode = GetColorCode(s, 91);
            var state = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
            var bgCol = colorCode > 0 ? new Vector4(0.1f, 0.85f, 0.2f, 1f) : _offColor;
            DrawIconButtonWithBg(Icon.PlayForwards, transportBtnSize, bgCol, state);
            DrawTooltipIfHovered("Play (Note 91)");
        }

        // Stop
        ImGui.SameLine();
        DrawStopButton(92, s, transportBtnSize, blinkOn);
        DrawTooltipIfHovered("Stop (Note 92)");

        // Record
        ImGui.SameLine();
        DrawRecordButton(93, s, transportBtnSize, blinkOn);
        DrawTooltipIfHovered("Record (Note 93)");

        ImGui.Spacing();
        DrawSectionLabel("NAVIGATION");

        var navBtnSize = new Vector2(30 * scale, 20 * scale);

        // Row 1: Up button (centered)
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + navBtnSize.X + 3 * scale);
        DrawIconButton(s, Icon.ArrowUp, 94, navBtnSize, blinkOn, "Bank Up (Note 94)");

        // Row 2: Left, Shift, Right
        DrawIconButton(s, Icon.ArrowLeft, 97, navBtnSize, blinkOn, "Bank Left (Note 97)");
        ImGui.SameLine();
        DrawSimpleButton(s, "SHIFT", 98, navBtnSize, blinkOn, "Shift");
        ImGui.SameLine();
        DrawIconButton(s, Icon.ArrowRight, 96, navBtnSize, blinkOn, "Bank Right (Note 96)");

        // Row 3: Down button (centered)
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + navBtnSize.X + 3 * scale);
        DrawIconButton(s, Icon.ArrowDown, 95, navBtnSize, blinkOn, "Bank Down (Note 95)");

        ImGui.Spacing();

        // Utility buttons
        DrawSimpleButton(s, "TAP", 99, btnSize, blinkOn, "Tap Tempo");
        ImGui.SameLine();
        DrawSimpleButton(s, "NUD-", 100, btnSize, blinkOn, "Nudge -");
        ImGui.SameLine();
        DrawSimpleButton(s, "NUD+", 101, btnSize, blinkOn, "Nudge +");
        ImGui.SameLine();
        DrawSimpleButton(s, "SESS", 102, btnSize, blinkOn, "Session / Clip Track");
    }

    /// <summary>
    /// Draws fader indicators.
    /// </summary>
    private static void DrawFaders(float scale)
    {
        DrawSectionLabel("FADERS");

        var faderBtnSize = new Vector2(26 * scale, 50 * scale);
        var knobBtnSize = new Vector2(24 * scale, 24 * scale);

        // 8 channel faders
        for (var i = 0; i < 8; i++)
        {
            if (i > 0) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.Button($"F{i + 1}##fader{i}", faderBtnSize);
            DrawTooltipIfHovered($"Track Fader {i + 1} (CC 7, Ch {i + 1})");
            ImGui.PopStyleColor();
        }

        // Master fader
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.3f, 1f));
        ImGui.Button("MST##faderM", faderBtnSize);
        DrawTooltipIfHovered("Master Fader (CC 14)");
        ImGui.PopStyleColor();

        // Crossfader and utility knobs
        ImGui.Spacing();
        var crossfaderSize = new Vector2(160 * scale, 18 * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.4f, 1f));
        ImGui.Button("A \u25c4\u2500\u2500 CROSSFADER \u2500\u2500\u25ba B##xfader", crossfaderSize);
        DrawTooltipIfHovered("A-B Crossfader (CC 15)");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.5f, 1f));
        ImGui.Button("CUE##cue47", knobBtnSize);
        DrawTooltipIfHovered("Cue Level (CC 47)");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.3f, 0.5f, 1f));
        ImGui.Button("TEMPO##tempo13", knobBtnSize);
        DrawTooltipIfHovered("Tempo (CC 13)");
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Helper to draw a single button with LED state from ControllerColors.
    /// </summary>
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
    /// This keeps styling identical between icon and shape buttons.
    /// </summary>
    private static void DrawStyledButton(object idKey, Vector4 bgCol, CustomComponents.ButtonStates state, Vector2 size, Action drawContent)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Button, bgCol);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(bgCol, 1.2f));

        // Push a string id to match ImGui.PushID overloads (int/IntPtr/string/ReadOnlySpan<char>)
        ImGui.PushID(idKey?.ToString() ?? string.Empty);
        ImGui.Button(string.Empty, size);
        ImGui.PopID();

         // Let the caller draw the icon/shape using the current item rect
         drawContent();

         ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(1);
    }

    #endregion

    #region Generic Grid Fallback

    /// <summary>
    /// Fallback grid view for non-APC40 devices.
    /// </summary>
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

}

