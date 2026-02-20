using ImGuiNET;
using T3.Editor.Gui.Interaction.Midi.CompatibleDevices;
using T3.Editor.Gui.Styling;
using static T3.Editor.Gui.Interaction.Midi.MidiLayoutDrawHelpers;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Renders the full APC40 MK1 physical hardware layout.
/// Uses <see cref="MidiLayoutDrawHelpers"/> for all shared drawing primitives.
/// </summary>
internal static class Apc40Mk1LayoutView
{
    // Per-channel fader UI values (8 tracks + master). Static because ImGui sliders
    // need a stable ref across frames and only one APC40 is rendered per frame.
    private static readonly float[] _channelFaderValues = new float[9];

    /// <summary>
    /// Entry point – draws the full APC40 MK1 physical layout.
    /// The physical APC40 has two side-by-side panels:
    ///
    /// LEFT PANEL (top to bottom):
    ///   Clip Launch Grid (8×5) + Scene Launch column
    ///   Clip Stop row + Stop All Clips
    ///   Track Selection row
    ///   Activator / Solo-Cue / Record Arm rows
    ///   Channel Faders (8 + Master)
    ///
    /// RIGHT PANEL (top to bottom):
    ///   Track Control knobs (2×4) + mode labels
    ///   Bank Select / Navigation
    ///   Device Control knobs (2×4)
    ///   Device Control buttons (2×4)
    ///   Transport (Play, Stop, Rec)
    ///   Crossfader
    /// </summary>
    internal static void Draw(MidiDeviceStatus s, bool blinkOn)
    {
        // Simplified layout: move math to a small local function for clarity
        const int leftColumns = 9; // 8 clip columns + 1 scene/control column
        var scale = T3Ui.UiScaleFactor;

        // Compute and return commonly used layout values. Returning a named tuple keeps the call site clean.
        (Vector2 clipBtnSize, Vector2 smallBtnSize, float btnW, float columnWidth, float baseSpacing, float framePadding, float cellPad, float innerBorder, float interPanelPadding, float minRightPanel) ComputeLayout(float contentWidth, float s)
        {
            var baseSpacingLocal   = 3f * s;          // gap between cells
            var framePaddingLocal  = 2f * s;          // inner padding for buttons
            var cellPadLocal       = baseSpacingLocal * 0.5f;
            var innerBorderLocal   = 4f * s;          // inset from panel edges
            var interPanelLocal    = 8f * s;          // gap between left and right panels
            var minRightLocal      = 150f * s;        // reserve for right panel
            var minBtnLocal        = 14f * s;
            var maxBtnLocal        = 34f * s;

            // available width for content (exclude outer inner borders on both sides)
            var avail = Math.Max(0f, contentWidth - 2f * innerBorderLocal);

            // Width that can be given to the left panel columns after reserving right panel
            var leftSpace = Math.Max(0f, avail - minRightLocal - interPanelLocal);

            // Account for horizontal gaps between columns before dividing
            var totalGaps = (leftColumns - 1) * baseSpacingLocal;
            var usable     = Math.Max(0f, leftSpace - totalGaps);
            var cellW      = usable / leftColumns;

            var btnWidth = MathF.Floor(ClampF(cellW - 2f * cellPadLocal, minBtnLocal, maxBtnLocal));

            var clipSize = new Vector2(MathF.Max(8f, btnWidth - 2f), MathF.Max(8f, btnWidth - 2f));
            var smallSize = new Vector2(btnWidth, MathF.Max(11f * s, btnWidth * 0.45f));

            return (clipSize, smallSize, btnWidth, cellW, baseSpacingLocal, framePaddingLocal, cellPadLocal, innerBorderLocal, interPanelLocal, minRightLocal);
        }

        var contentWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var windowPos = ImGui.GetWindowPos();
        var layout = ComputeLayout(contentWidth, scale);

        var clipBtnSize  = layout.clipBtnSize;
        var smallBtnSize = layout.smallBtnSize;
        var btnW         = layout.btnW;
        var columnWidth  = layout.columnWidth;
        var baseSpacing  = layout.baseSpacing;
        var framePadding = layout.framePadding;
        var cellPad      = layout.cellPad;
        var innerBorder  = layout.innerBorder;
        var interPanelPadding = layout.interPanelPadding;
        var minRightPanel     = layout.minRightPanel;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,  new Vector2(baseSpacing, baseSpacing));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(framePadding, framePadding));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding,  new Vector2(cellPad, cellPad));

        // --- Inner border offset ---
        var startPos = ImGui.GetCursorPos();
        startPos.X += innerBorder;
        startPos.Y += innerBorder;
        ImGui.SetCursorPos(startPos);

        // --- Left + Right panels using a parent table to avoid manual cursor positioning ---
        var leftPanelContentWidth = columnWidth * leftColumns + (leftColumns - 1) * baseSpacing;
        var rightPanelWidth = Math.Max(minRightPanel, contentWidth - leftPanelContentWidth - interPanelPadding);

        var tableFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX;
        if (ImGui.BeginTable("apc40_main_table", 2, tableFlags))
        {
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, leftPanelContentWidth);
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, rightPanelWidth);

            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.BeginGroup();
            
            // Pass both the clip button size, computed column width and base spacing
            DrawLeftPanel(s, blinkOn, clipBtnSize, smallBtnSize, columnWidth, baseSpacing, T3Ui.UiScaleFactor);
            ImGui.EndGroup();

            ImGui.TableSetColumnIndex(1);
            ImGui.BeginGroup();
            DrawRightPanel(s, blinkOn, clipBtnSize, smallBtnSize, T3Ui.UiScaleFactor);
            ImGui.EndGroup();

            ImGui.EndTable();
        }

        // Add a small bottom border so subsequent widgets don't overlap the panels
        ImGui.Dummy(new Vector2(0f, innerBorder));

        ImGui.PopStyleVar(3);
    }

    // -------------------------------------------------------------------------
    // Left panel
    // -------------------------------------------------------------------------

    private static void DrawLeftPanel(MidiDeviceStatus s, bool blinkOn,
                                      Vector2 clipBtnSize, Vector2 smallBtnSize,
                                      float cellWidth, float baseSpacing, float scale)
    {
        const int clipCols = 8;
        const int columns  = clipCols + 1; // extra scene/control column
        const int clipRows = 5;

        if (!ImGui.BeginTable("left_panel_table_" + s.ProductName, columns, ImGuiTableFlags.SizingFixedFit))
            return;

        // cellWidth is the full cell column width; clip buttons are slightly smaller (clipBtnSize)
        var cellW   = cellWidth;
        var clipW   = clipBtnSize.X;
        var clipH   = clipBtnSize.Y;
        var smallW  = smallBtnSize.X;
        var smallH  = smallBtnSize.Y;
        // Make faders taller to better fill taller windows
        var faderH  = smallH * 4.0f + 6f * scale;

        for (var cc = 0; cc < clipCols; cc++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, cellW);
        ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, cellW); // scene column

        // --- Clip grid rows with scene buttons ---
        for (var r = 0; r < clipRows; r++)
        {
            ImGui.TableNextRow();
            for (var c = 0; c < clipCols; c++)
            {
                ImGui.TableSetColumnIndex(c);
                var idx       = r * clipCols + c;
                var colorCode = GetColorCode(s, idx);
                var col       = ColorForClipLaunch(colorCode, blinkOn);

                ImGui.PushStyleColor(ImGuiCol.Button,        col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"##clip{idx}", new Vector2(clipW, clipH));
                DrawTooltipIfHovered($"Clip Launch R{r + 1}C{c + 1} (idx {idx})", $"Color: {ColorCodeName(colorCode)}");
                ImGui.PopStyleColor(2);
            }

            ImGui.TableSetColumnIndex(clipCols);
            var sceneIdx = 82 + r;
            var sceneCol = ColorForSimpleLed(GetColorCode(s, sceneIdx), blinkOn);
            DrawLedButton($"S{r + 1}", sceneIdx, sceneCol, new Vector2(clipW, clipH), $"Scene Launch {r + 1} (Note {sceneIdx})");
        }

        // --- Clip Stop row ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var note = 52 + c;
            var col  = ColorForSimpleLed(GetColorCode(s, note), blinkOn);
            // Use full clip button height for the stop row so they match the clip launch buttons
            DrawLedButton((c + 1).ToString(), note, col, new Vector2(clipW, clipH), $"Clip Stop Track {c + 1} (Note {note})");
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.PushStyleColor(ImGuiCol.Button, ColorForSimpleLed(GetColorCode(s, 81), blinkOn));
        // STOP ALL should also be full-height to align visually with the stop buttons
        ImGui.Button("STOP ALL", new Vector2(clipW, clipH));
        DrawTooltipIfHovered("Stop All Clips (Note 81)");
        ImGui.PopStyleColor();

        // Spacer between small control rows; use the same base spacing as the matrix buttons
        var smallSpacer = baseSpacing;
        ImGui.TableNextRow();
        for (var c = 0; c < columns; c++) { ImGui.TableSetColumnIndex(c); ImGui.Dummy(new Vector2(0, smallSpacer)); }

        // --- Track Selection row ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.25f, 1f));
            ImGui.Button($"{c + 1}##trksel{c}", new Vector2(smallW, smallH));
            ImGui.PopStyleColor();
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.4f, 1f));
        ImGui.Button("MST##trkselM", new Vector2(smallW, smallH));
        DrawTooltipIfHovered("Master Track Select");
        ImGui.PopStyleColor();

        // Spacer
        ImGui.TableNextRow();
        for (var c = 0; c < columns; c++) { ImGui.TableSetColumnIndex(c); ImGui.Dummy(new Vector2(0, smallSpacer)); }

        // --- Activator row ---
        ImGui.TableNextRow();
        // Use the same item spacing (baseSpacing) between the three small rows so they align with the matrix
        var combinedCueHeight = smallH * 3f + baseSpacing * 2f;
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var note = 66 + c;
            var col  = ColorForSimpleLed(GetColorCode(s, note), blinkOn);
            // activator should be smallH
            DrawLedButton($"A{c + 1}", note, col, new Vector2(smallW, smallH), $"Activator Track {c + 1}");
        }
        ImGui.TableSetColumnIndex(clipCols);
        // remember the top-left of the scene cell so we can draw the larger cue knob later
        var cueCellPos = ImGui.GetCursorScreenPos();
        // place a small dummy to keep this scene cell height equal to the small rows
        ImGui.Dummy(new Vector2(smallW, smallH));

        // --- Solo/Cue row (cue knob occupies the scene column) ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var col = ColorForSimpleLed(GetColorCode(s, 49), blinkOn);
            // solo/cue track buttons should be smallH
            DrawLedButton($"S{c + 1}", 49, col, new Vector2(smallW, smallH), $"Solo/Cue Track {c + 1}");
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.Dummy(new Vector2(smallW, smallH));

        // --- Record Arm row ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var note = 48 + c;
            var col  = ColorForSimpleLed(GetColorCode(s, note), blinkOn);
            // match height for alignment
            DrawLedButton($"R{c + 1}", note, col, new Vector2(smallW, smallH), $"Record Arm Track {c + 1}");
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.Dummy(new Vector2(smallW, smallH));

        // Now draw the cue knob at the recorded absolute position so it visually spans the three small rows
        if (cueCellPos != default)
        {
            var prevCursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(cueCellPos);
            DrawCueKnob(s, new Vector2(smallW, combinedCueHeight));
            ImGui.SetCursorScreenPos(prevCursor);
        }

        // --- Spacer before the Faders row (same baseSpacing used for section gaps) ---
        ImGui.TableNextRow();
        for (var sc3 = 0; sc3 < columns; sc3++) { ImGui.TableSetColumnIndex(sc3); ImGui.Dummy(new Vector2(0f, baseSpacing)); }

         // --- Faders row ---
         ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            ImGui.PushID($"fader{c}");
            var ch  = Math.Max(0, Math.Min(7, c));
            var idx = ch * 128 + 7;
            if (s.ControllerValues != null && idx >= 0 && idx < s.ControllerValues.Length)
                _channelFaderValues[c] = s.ControllerValues[idx];

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, ImGui.GetStyle().ItemSpacing.Y));
            ImGui.VSliderFloat($"##fader{c}", new Vector2(smallW, faderH), ref _channelFaderValues[c], 0f, 1f, "");
            ImGui.PopStyleVar(2); 
            DrawTooltipIfHovered($"Track Fader {c + 1}: {Math.Round(_channelFaderValues[c] * 100)}%");

            ImGui.PopID();
        }

        ImGui.TableSetColumnIndex(clipCols);
        ImGui.PushID("faderM");
        var idxM = 0 * 128 + 14;
        if (s.ControllerValues != null && idxM >= 0 && idxM < s.ControllerValues.Length)
            _channelFaderValues[8] = s.ControllerValues[idxM];

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.VSliderFloat("##faderM", new Vector2(smallW, faderH), ref _channelFaderValues[8], 0f, 1f, "");
        ImGui.PopStyleVar();
        DrawTooltipIfHovered($"Master Fader: {Math.Round(_channelFaderValues[8] * 100)}%");

        ImGui.PopID();

        ImGui.EndTable();
    }

    // -------------------------------------------------------------------------
    // Right panel
    // -------------------------------------------------------------------------

    private static void DrawRightPanel(MidiDeviceStatus s, bool blinkOn,
                                       Vector2 clipBtnSize, Vector2 smallBtnSize,
                                       float scale)
    {
        var btnW        = clipBtnSize.X;
        var knobDim     = Math.Max(28f * scale, btnW);
        var knobRender  = new Vector2(knobDim, knobDim);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1.5f * scale, 4f * scale));

        DrawKnobGrid("track",  48, 4, 2, knobRender, s, blinkOn);
        DrawModeKnobLabels(smallBtnSize, s, knobRender.X);

        ImGui.Spacing();

        DrawBankSelectNavigation(s, blinkOn, scale);

        ImGui.Spacing();

        DrawKnobGrid("device", 16, 4, 2, knobRender, s, blinkOn);

        ImGui.Spacing();

        DrawDeviceControlButtons(s, blinkOn, smallBtnSize, knobRender.X);

        ImGui.Spacing();

        DrawTransportButtons(s, blinkOn, scale);
        ImGui.Spacing();
        DrawCrossfader(scale);

        ImGui.PopStyleVar();
    }

    // -------------------------------------------------------------------------
    // Right-panel sub-sections
    // -------------------------------------------------------------------------

    /// <summary>Draws PAN / SEND A / SEND B / SEND C mode-select buttons aligned to knob columns.</summary>
    private static void DrawModeKnobLabels(Vector2 btnSize, MidiDeviceStatus s, float columnWidth)
    {
        var isApc40 = s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isApc40) return;

        var modeNotes  = Apc40Mk1.ModeButtonNoteOrder;
        var modeLabels = Apc40Mk1.ModeButtonLabels;

        if (!ImGui.BeginTable("mode_labels_table_" + s.ProductName, 4, ImGuiTableFlags.SizingFixedFit))
            return;

        for (var c = 0; c < 4; c++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, columnWidth);

        ImGui.TableNextRow();
        for (var i = 0; i < 4; i++)
        {
            ImGui.TableSetColumnIndex(i);
            if (i < modeNotes.Length && i < modeLabels.Length)
            {
                var noteId    = modeNotes[i];
                var colorCode = GetColorCode(s, noteId);
                var col       = colorCode > 0 ? GreenColor : OffColor;
                DrawLedButton(modeLabels[i], noteId, col, btnSize, $"{modeLabels[i]} (Note {noteId})");
            }
            else
            {
                ImGui.TextUnformatted("");
            }
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws the Bank Select section:
    ///   Col 0: SHIFT (centered)
    ///   Col 1-3: ← ↑↓ →
    ///   Col 4: TAP TEMPO, NUD-, NUD+
    /// </summary>
    private static void DrawBankSelectNavigation(MidiDeviceStatus s, bool blinkOn, float scale)
    {
        var bw  = MathF.Floor(28f * scale);
        var bh  = MathF.Floor(18f * scale);
        var btn = new Vector2(bw, bh);

        if (!ImGui.BeginTable("bank_nav_" + s.ProductName, 5,
                ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoHostExtendX))
            return;

        for (var c = 0; c < 5; c++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, bw);

        // Row 0: empty | empty | ↑ | empty | TAP TEMPO
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(1); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(2); DrawIconButton(s, Icon.ArrowUp,    94, btn, blinkOn, "Bank Up (Note 94)");
        ImGui.TableSetColumnIndex(3); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(4); DrawSimpleButton(s, "TAP", 99, btn, blinkOn, "Tap Tempo");

        // Row 1: SHIFT | ← | empty | → | NUD-
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); DrawSimpleButton(s, "SHIFT", 98, btn, blinkOn, "Shift");
        ImGui.TableSetColumnIndex(1); DrawIconButton(s, Icon.ArrowLeft,  97, btn, blinkOn, "Bank Left (Note 97)");
        ImGui.TableSetColumnIndex(2); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(3); DrawIconButton(s, Icon.ArrowRight, 96, btn, blinkOn, "Bank Right (Note 96)");
        ImGui.TableSetColumnIndex(4); DrawSimpleButton(s, "NUD-", 100, btn, blinkOn, "Nudge -");

        // Row 2: empty | empty | ↓ | empty | NUD+
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(1); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(2); DrawIconButton(s, Icon.ArrowDown,  95, btn, blinkOn, "Bank Down (Note 95)");
        ImGui.TableSetColumnIndex(3); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(4); DrawSimpleButton(s, "NUD+", 101, btn, blinkOn, "Nudge +");

        ImGui.EndTable();
    }

    /// <summary>Draws Device Control buttons (2 rows × 4) aligned to knob columns.</summary>
    private static void DrawDeviceControlButtons(MidiDeviceStatus s, bool blinkOn,
                                                 Vector2 btnSize, float columnWidth)
    {
        var isApc40 = s.DeviceTypeName?.IndexOf("Apc40", StringComparison.OrdinalIgnoreCase) >= 0;
        // For non-APC40 devices we don't provide APC40 fallbacks here. A generic
        // fallback layout view will render appropriate controls for other devices.
        if (!isApc40) return;

        var notes   = Apc40Mk1.DeviceControlNoteOrder;
        var labels  = Apc40Mk1.DeviceControlLabels;

        const int cols = 4;
        if (!ImGui.BeginTable("deviceCtrlBtnTable_" + s.ProductName, cols, ImGuiTableFlags.SizingFixedFit))
            return;

        for (var c = 0; c < cols; c++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, columnWidth);

        var total = Math.Min(notes.Length, labels.Length);
        for (var i = 0; i < total; i++)
        {
            if (i % cols == 0) ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(i % cols);

            var noteId   = notes[i];
            var label    = labels[i];
            var cellSize = new Vector2(columnWidth, btnSize.Y);

            switch (noteId)
            {
                case 60: DrawIconButton(s, Icon.ChevronLeft,  noteId, cellSize, blinkOn, $"Device Left (Note {noteId})");  break;
                case 61: DrawIconButton(s, Icon.ChevronRight, noteId, cellSize, blinkOn, $"Device Right (Note {noteId})"); break;
                default: DrawSimpleButton(s, label, noteId, cellSize, blinkOn, label);                                     break;
            }
        }

        ImGui.EndTable();
    }

    /// <summary>Draws Play / Stop / Record transport buttons.</summary>
    private static void DrawTransportButtons(MidiDeviceStatus s, bool blinkOn, float scale)
    {
        var size = new Vector2(40 * scale, 22 * scale);

        var colorCode = GetColorCode(s, 91);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = colorCode > 0 ? new Vector4(0.1f, 0.85f, 0.2f, 1f) : OffColor;
        DrawIconButtonWithBg(Icon.PlayForwards, size, bgCol, state);
        DrawTooltipIfHovered("Play (Note 91)");

        ImGui.SameLine();
        DrawStopButton(92, s, size, blinkOn);
        DrawTooltipIfHovered("Stop (Note 92)");

        ImGui.SameLine();
        DrawRecordButton(93, s, size, blinkOn);
        DrawTooltipIfHovered("Record (Note 93)");
    }

    /// <summary>Draws the A-B Crossfader indicator.</summary>
    private static void DrawCrossfader(float scale)
    {
        var size = new Vector2(140 * scale, 18 * scale);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.35f, 0.35f, 0.4f, 1f));
        ImGui.Button("A \u25c4\u2500\u2500 CROSSFADER \u2500\u2500\u25ba B##xfader", size);
        DrawTooltipIfHovered("A-B Crossfader (CC 15)");
        ImGui.PopStyleColor();
    }

    // -------------------------------------------------------------------------
    // Shared sub-widgets
    // -------------------------------------------------------------------------

    /// <summary>Draws the compact Cue Level knob (CC 47) into the given cell size.</summary>
    private static void DrawCueKnob(MidiDeviceStatus s, Vector2 size)
    {
        ImGui.PushID("cue_knob");
        ImGui.InvisibleButton("##cueKnob", size);

        var min    = ImGui.GetItemRectMin();
        var max    = ImGui.GetItemRectMax();
        var center = (min + max) / 2f;
        var radius = Math.Min(max.X - min.X, max.Y - min.Y) / 2f - 3f;

        float cueVal = 0f;
        if (s.ControllerValues != null)
        {
            var valIdx = 0 * 128 + 47;
            if (valIdx >= 0 && valIdx < s.ControllerValues.Length)
                cueVal = s.ControllerValues[valIdx];
        }

        var dl = ImGui.GetWindowDrawList();
        dl.AddCircleFilled(center, radius * 0.9f, ImGui.GetColorU32(UiColors.BackgroundFull.Rgba));

        var angle  = -MathF.PI * 0.75f + MathF.PI * 1.5f * ClampF(cueVal, 0f, 1f);
        var dotPos = new Vector2(
            center.X + MathF.Cos(angle) * radius * 0.55f,
            center.Y + MathF.Sin(angle) * radius * 0.55f);
        dl.AddCircleFilled(dotPos, radius * 0.18f, ImGui.GetColorU32(UiColors.Text.Rgba));
        DrawTooltipIfHovered($"Cue Level (CC 47): {Math.Round(cueVal * 100)}%");


        ImGui.PopID();
    }
}
