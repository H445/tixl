using ImGuiNET;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Interaction.Midi.CompatibleDevices;
using T3.Editor.Gui.Styling;

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
        for (var i = 0; i < count; i++)
        {
            if (i > 0) ImGui.SameLine();
            var cc = ccStart + i;
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(color, 1.15f));
            ImGui.Button($"K{i + 1}##{idPrefix}{cc}", size);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"{idPrefix} {i + 1} (CC {cc})");
                ImGui.EndTooltip();
            }
            ImGui.PopStyleColor(2);
        }
    }

    /// <summary>
    /// Draws the 8x5 clip launch grid with scene launch buttons on the right side
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
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextUnformatted("SCENE");
        ImGui.PopStyleColor();

        // Clip grid rows (R1 at top)
        for (var r = 0; r < rows; r++)
        {
            ImGui.TableNextRow();

            // Row label
            ImGui.TableSetColumnIndex(0);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.3f, 1f));
            ImGui.Button($"R{r + 1}", new Vector2(24 * scale, clipBtnSize.Y));
            ImGui.PopStyleColor();

            // 8 clip launch buttons
            for (var c = 0; c < cols; c++)
            {
                ImGui.TableSetColumnIndex(c + 1);
                var idx = r * cols + c; // index 0-39
                var colorCode = idx < s.ControllerColors.Length ? s.ControllerColors[idx] : -1;
                var col = ColorForClipLaunch(colorCode, blinkOn);

                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"##{idx}", clipBtnSize);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"Clip Launch R{r + 1}C{c + 1} (idx {idx})");
                    ImGui.TextUnformatted($"Color: {ColorCodeName(colorCode)}");
                    ImGui.EndTooltip();
                }
                ImGui.PopStyleColor(2);
            }

            // Scene Launch button (right column)
            ImGui.TableSetColumnIndex(cols + 1);
            var sceneIdx = 82 + r; // Scene Launch 1-5 = notes 82-86
            var sceneColor = sceneIdx < s.ControllerColors.Length ? s.ControllerColors[sceneIdx] : -1;
            var sceneCol = ColorForSimpleLed(sceneColor, blinkOn);
            ImGui.PushStyleColor(ImGuiCol.Button, sceneCol);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(sceneCol, 1.2f));
            ImGui.Button($"S{r + 1}##{sceneIdx}", sceneBtnSize);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Scene Launch {r + 1} (Note {sceneIdx})");
                ImGui.EndTooltip();
            }
            ImGui.PopStyleColor(2);
        }

        // Stop All Clips row
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        for (var c = 1; c <= cols; c++)
            ImGui.TableSetColumnIndex(c);

        ImGui.TableSetColumnIndex(cols + 1);
        var stopAllColor = 81 < s.ControllerColors.Length ? s.ControllerColors[81] : -1;
        var stopAllCol = ColorForSimpleLed(stopAllColor, blinkOn);
        ImGui.PushStyleColor(ImGuiCol.Button, stopAllCol);
        ImGui.Button($"STOP##81", sceneBtnSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Stop All Clips (Note 81)");
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor();

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws per-track control button rows below the clip grid:
    /// Clip Stop, Solo, Activator (A-B), Record Arm, Track Select
    /// </summary>
    private void DrawTrackControlButtons(MidiDeviceStatus s, bool blinkOn, Vector2 btnSize, float scale)
    {
        const int tracks = 8;
        var labelWidth = new Vector2(60 * scale, btnSize.Y);

        // --- Clip Stop (indices 52-59, Note 0x34 on Ch0-7 in Ableton mode) ---
        DrawTrackButtonRow(s, blinkOn, "CLIP STOP", 52, tracks, btnSize, labelWidth, true,
            "Clip Stop (Note 52-59)");

        // --- Solo (Note 49/0x31 on Ch0-7 in Ableton mode, Note 49 in generic) ---
        DrawTrackButtonRow(s, blinkOn, "SOLO", 49, 1, btnSize, labelWidth, false,
            "Solo (Note 49, Ch 0-7 in Ableton)");

        // --- Activator / A-B buttons (indices 66-73, Note 66-73 on Ch1) ---
        DrawTrackButtonRow(s, blinkOn, "ACTIVATOR", 66, tracks, btnSize, labelWidth, true,
            "Activator (Note 66-73)");

        // --- Record Arm (Note 48/0x30 on Ch0-7 in Ableton, Notes 48-55 in Generic) ---
        DrawTrackButtonRow(s, blinkOn, "REC ARM", 48, 1, btnSize, labelWidth, false,
            "Record Arm (Note 48, Ch 0-7 in Ableton)");

        // --- Track Select (Note 51/0x33 on Ch0-7 in Ableton, Notes 58-65 in Generic) ---
        DrawTrackButtonRow(s, blinkOn, "TRK SEL", 51, 1, btnSize, labelWidth, false,
            "Track Select (Note 51, Ch 0-7 in Ableton)");
    }

    private void DrawTrackButtonRow(MidiDeviceStatus s, bool blinkOn, string label,
        int startIdx, int _, Vector2 btnSize, Vector2 labelSize,
        bool hasPerTrackIndices, string tooltip)
    {
        const int tracks = 8;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 1f));
        ImGui.Button(label, labelSize);
        ImGui.PopStyleColor();

        for (var i = 0; i < tracks; i++)
        {
            ImGui.SameLine();
            int colorCode;
            int noteIdx;
            if (hasPerTrackIndices)
            {
                noteIdx = startIdx + i;
                colorCode = noteIdx < s.ControllerColors.Length ? s.ControllerColors[noteIdx] : -1;
            }
            else
            {
                noteIdx = startIdx;
                colorCode = (i == 0 && noteIdx < s.ControllerColors.Length) ? s.ControllerColors[noteIdx] : -1;
            }
            var col = ColorForSimpleLed(colorCode, blinkOn);

            ImGui.PushStyleColor(ImGuiCol.Button, col);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
            ImGui.Button($"{i + 1}##{label}{i}", btnSize);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"{label} Track {i + 1}");
                ImGui.TextUnformatted(hasPerTrackIndices ? $"Note {noteIdx}, Color: {colorCode}" : tooltip);
                ImGui.EndTooltip();
            }
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

        // Use device-specific ordered mapping when available
        if (s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var notes = Apc40Mk1.DeviceControlNoteOrder;
            var labels = Apc40Mk1.DeviceControlLabels;
            for (var i = 0; i < notes.Length && i < labels.Length; i++)
            {
                if (i > 0 && i % 4 != 0) ImGui.SameLine();
                var noteId = notes[i];
                var label = labels[i];
                DrawSimpleButton(s, label, noteId, btnSize, blinkOn, label);
            }
        }
        else
        {
            var devButtons = new (string Label, int NoteId)[]
            {
                ("Dev◄", 58), ("Dev►", 59), ("Bnk◄", 60), ("Bnk►", 61),
                ("On/Off", 62), ("Lock", 63), ("Clip/D", 64), ("Detail", 65),
            };

            for (var i = 0; i < devButtons.Length; i++)
            {
                if (i > 0 && i % 4 != 0) ImGui.SameLine();
                var (lbl, noteId) = devButtons[i];
                DrawSimpleButton(s, lbl, noteId, btnSize, blinkOn, lbl);
            }
        }

        ImGui.Spacing();

        DrawSectionLabel("MODE");

        // If this is an APC40 device and the compatible device class exposes the ordered
        // mapping, use it to render mode buttons in the exact physical order.
        if (s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var notes = Apc40Mk1.ModeButtonNoteOrder;
            var labels = Apc40Mk1.ModeButtonLabels;
            for (var i = 0; i < notes.Length && i < labels.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                var noteId = notes[i];
                var label = labels[i];
                var colorCode = noteId < s.ControllerColors.Length ? s.ControllerColors[noteId] : -1;
                var col = colorCode > 0 ? _greenColor : _offColor; // use green to indicate active for simplicity

                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"{label}##{noteId}", btnSize);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"{label} (Note {noteId})");
                    ImGui.EndTooltip();
                }
                ImGui.PopStyleColor(2);
            }
        }
        else
        {
            // Fallback to previous hardcoded layout
            var modeButtons = new (string Label, int NoteId, Vector4 ActiveColor)[]
            {
                ("PAN", 87, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
                ("SEND", 88, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
                ("USER", 89, new Vector4(0.2f, 0.6f, 0.8f, 1f)),
                ("METRO", 90, new Vector4(0.7f, 0.4f, 0.2f, 1f)),
            };

            for (var i = 0; i < modeButtons.Length; i++)
            {
                if (i > 0) ImGui.SameLine();
                var (label, noteId, activeColor) = modeButtons[i];
                var colorCode = noteId < s.ControllerColors.Length ? s.ControllerColors[noteId] : -1;
                var col = colorCode > 0 ? activeColor : _offColor;

                ImGui.PushStyleColor(ImGuiCol.Button, col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"{label}##{noteId}", btnSize);
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"{label} (Note {noteId})");
                    ImGui.EndTooltip();
                }
                ImGui.PopStyleColor(2);
            }
        }
    }

    /// <summary>
    /// Draws Transport controls (Play, Stop, Record) and Navigation (Bank arrows, Shift,
    /// Tap Tempo, Nudge+/-, Session).
    /// </summary>
    private void DrawTransportAndNavigation(MidiDeviceStatus s, bool blinkOn, Vector2 btnSize, float scale)
    {
        DrawSectionLabel("TRANSPORT");

        var transportBtnSize = new Vector2(50 * scale, 22 * scale);
        var transportButtons = new (string Label, int NoteId, Vector4 OnColor)[]
        {
            ("\u25b6 PLAY", 91, new Vector4(0.1f, 0.85f, 0.2f, 1f)),
            ("\u25a0 STOP", 92, new Vector4(0.6f, 0.6f, 0.6f, 1f)),
            ("\u25cf REC", 93, new Vector4(0.9f, 0.15f, 0.1f, 1f)),
        };

        for (var i = 0; i < transportButtons.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            var (lbl, noteId, onColor) = transportButtons[i];
            var colorCode = noteId < s.ControllerColors.Length ? s.ControllerColors[noteId] : -1;
            var col = colorCode > 0 ? onColor : _offColor;

            ImGui.PushStyleColor(ImGuiCol.Button, col);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
            ImGui.Button($"{lbl}##{noteId}", transportBtnSize);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"{lbl} (Note {noteId})");
                ImGui.EndTooltip();
            }
            ImGui.PopStyleColor(2);
        }

        ImGui.Spacing();

        DrawSectionLabel("NAVIGATION");

        var navBtnSize = new Vector2(30 * scale, 20 * scale);

        // Row 1: Up button (centered)
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + navBtnSize.X + 3 * scale);
        DrawSimpleButton(s, "\u25b2", 94, navBtnSize, blinkOn, "Bank Up");

        // Row 2: Left, Shift, Right
        DrawSimpleButton(s, "\u25c4", 97, navBtnSize, blinkOn, "Bank Left");
        ImGui.SameLine();
        DrawSimpleButton(s, "SHIFT", 98, navBtnSize, blinkOn, "Shift");
        ImGui.SameLine();
        DrawSimpleButton(s, "\u25ba", 96, navBtnSize, blinkOn, "Bank Right");

        // Row 3: Down button (centered)
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + navBtnSize.X + 3 * scale);
        DrawSimpleButton(s, "\u25bc", 95, navBtnSize, blinkOn, "Bank Down");

        ImGui.Spacing();

        // Utility buttons: Tap Tempo, Nudge-, Nudge+, Session
        DrawSimpleButton(s, "TAP", 99, btnSize, blinkOn, "Tap Tempo");
        ImGui.SameLine();
        DrawSimpleButton(s, "NUD-", 100, btnSize, blinkOn, "Nudge -");
        ImGui.SameLine();
        DrawSimpleButton(s, "NUD+", 101, btnSize, blinkOn, "Nudge +");
        ImGui.SameLine();
        DrawSimpleButton(s, "SESS", 102, btnSize, blinkOn, "Session / Clip Track");
    }

    /// <summary>
    /// Draws fader indicators for 8 channel faders, master fader, A-B crossfader,
    /// Cue Level knob, and Tempo knob.
    /// Since fader/knob positions are CC values not stored in ControllerColors,
    /// these are shown as labeled static UI elements.
    /// </summary>
    private static void DrawFaders(float scale)
    {
        DrawSectionLabel("FADERS");

        var faderBtnSize = new Vector2(26 * scale, 50 * scale);
        var knobBtnSize = new Vector2(24 * scale, 24 * scale);

        // 8 channel faders (CC 7 on ch 1-8)
        for (var i = 0; i < 8; i++)
        {
            if (i > 0) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.35f, 1f));
            ImGui.Button($"F{i + 1}##fader{i}", faderBtnSize);
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Track Fader {i + 1} (CC 7, Ch {i + 1})");
                ImGui.EndTooltip();
            }
            ImGui.PopStyleColor();
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.4f, 0.3f, 0.3f, 1f));
        ImGui.Button("MST##faderM", faderBtnSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Master Fader (CC 14)");
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor();

        // Crossfader and utility knobs
        ImGui.Spacing();
        var crossfaderSize = new Vector2(160 * scale, 18 * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.4f, 1f));
        ImGui.Button("A \u25c4\u2500\u2500 CROSSFADER \u2500\u2500\u25ba B##xfader", crossfaderSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("A-B Crossfader (CC 15)");
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.5f, 1f));
        ImGui.Button("CUE##cue47", knobBtnSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Cue Level (CC 47)");
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.5f, 0.3f, 0.5f, 1f));
        ImGui.Button("TEMPO##tempo13", knobBtnSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Tempo (CC 13)");
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor();
    }

    /// <summary>
    /// Helper to draw a single button with LED state from ControllerColors.
    /// </summary>
    private void DrawSimpleButton(MidiDeviceStatus s, string label, int noteId, Vector2 size, bool blinkOn, string tooltipLabel)
    {
        var colorCode = noteId < s.ControllerColors.Length ? s.ControllerColors[noteId] : -1;
        var col = ColorForSimpleLed(colorCode, blinkOn);

        ImGui.PushStyleColor(ImGuiCol.Button, col);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
        ImGui.Button($"{label}##{noteId}", size);
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"{tooltipLabel} (Note {noteId})");
            ImGui.EndTooltip();
        }
        ImGui.PopStyleColor(2);
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
                    var colorCode = idx < s.ControllerColors.Length ? s.ControllerColors[idx] : -1;
                    var col = ColorForClipLaunch(colorCode, blinkOn);

                    ImGui.PushStyleColor(ImGuiCol.Button, col);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                    ImGui.Button("", btnSize);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted($"Button {idx} (R{r + 1}C{c + 1})");
                        ImGui.TextUnformatted($"Color: {colorCode}");
                        ImGui.EndTooltip();
                    }
                    ImGui.PopStyleColor(2);
                }
            }
            ImGui.EndTable();
        }
        ImGui.PopStyleVar(2);
    }

    #endregion

    #region Color Mapping

    // APC40 Mk1 clip launch LED color states (from protocol):
    // 0=off, 1=green, 2=green blink, 3=red, 4=red blink, 5=yellow, 6=yellow blink, 7-127=green
    private static readonly Vector4 _greenColor = new(0.1f, 0.85f, 0.2f, 1f);
    private static readonly Vector4 _redColor = new(0.9f, 0.15f, 0.1f, 1f);
    private static readonly Vector4 _yellowColor = new(0.95f, 0.75f, 0.05f, 1f);
    private static readonly Vector4 _offColor = new(0.25f, 0.25f, 0.25f, 1f);
    private static readonly Vector4 _dimColor = new(0.15f, 0.15f, 0.15f, 0.8f);

    /// <summary>
    /// Maps APC40 Mk1 7-state clip launch color codes to RGBA colors.
    /// Protocol: 0=off, 1=green, 2=green blink, 3=red, 4=red blink,
    ///          5=yellow, 6=yellow blink, 7-127=green
    /// </summary>
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

    /// <summary>
    /// Maps simple LED state for non-clip-grid buttons.
    /// Most buttons: 0=off, 1-127=on. Clip Stop also supports 2=blink.
    /// </summary>
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

    /// <summary>
    /// Returns a human-readable name for a color code value.
    /// </summary>
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

    internal override IReadOnlyList<T3.Editor.Gui.Windows.Window> GetInstances()
    {
        return new List<T3.Editor.Gui.Windows.Window>();
    }

}

