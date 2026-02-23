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
    private static readonly float[] _channelFaderValues   = new float[9];
    private static readonly bool[]  _faderDragging        = new bool[9];
    private static readonly float[] _faderDragStartY      = new float[9];
    private static readonly float[] _faderDragStartVal    = new float[9];

    // Pre-built fader ID strings – reused every frame to avoid per-frame allocations.
    private static readonly string[] _faderIds = Enumerable.Range(0, 8).Select(i => $"fader{i}").Append("faderM").ToArray();

    // Crossfader UI value (CC 15, channel 0).
    private static float _crossfaderValue;
    private static bool  _crossfaderDragging;
    private static float _crossfaderDragStartX;   // screen-X where drag started
    private static float _crossfaderDragStartVal; // _crossfaderValue at drag start

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
    internal static void Draw(MidiDeviceStatus s)
    {
        // Simplified layout: move math to a small local function for clarity
        var leftColumns = Apc40Mk1.ClipGridColumns + 1; // clip columns + scene/control column
        var scale = T3Ui.UiScaleFactor;

        // Compute and return commonly used layout values. Returning a named tuple keeps the call site clean.
        (Vector2 clipBtnSize, Vector2 smallBtnSize, float btnW, float columnWidth, float baseSpacing, float framePadding, float cellPad, float innerBorder, float interPanelPadding, float minRightPanel) ComputeLayout(float contentWidth, float scaleFactor)
        {
            var baseSpacingLocal   = 3f * scaleFactor;          // gap between cells
            var framePaddingLocal  = 2f * scaleFactor;          // inner padding for buttons
            var cellPadLocal       = baseSpacingLocal * 0.5f;
            var innerBorderLocal   = 4f * scaleFactor;          // inset from panel edges
            var interPanelLocal    = 8f * scaleFactor;          // gap between left and right panels
            var minRightLocal      = 150f * scaleFactor;        // reserve for right panel
            var minBtnLocal        = 14f * scaleFactor;
            var maxBtnLocal        = 34f * scaleFactor;

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
            var smallSize = new Vector2(btnWidth, MathF.Max(11f * scaleFactor, btnWidth * 0.45f));

            return (clipSize, smallSize, btnWidth, cellW, baseSpacingLocal, framePaddingLocal, cellPadLocal, innerBorderLocal, interPanelLocal, minRightLocal);
        }

        var contentWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;
        var layout = ComputeLayout(contentWidth, scale);

        var clipBtnSize  = layout.clipBtnSize;
        var smallBtnSize = layout.smallBtnSize;
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
        // Render all text in this layout using the project's small font so labels stay compact
        ImGui.PushFont(Fonts.FontSmall);

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
            DrawLeftPanel(s, clipBtnSize, smallBtnSize, columnWidth, baseSpacing, T3Ui.UiScaleFactor);
            ImGui.EndGroup();

            ImGui.TableSetColumnIndex(1);
            ImGui.BeginGroup();
            DrawRightPanel(s, clipBtnSize, smallBtnSize, T3Ui.UiScaleFactor);
            ImGui.EndGroup();

            ImGui.EndTable();
        }

        // Add a small bottom border so subsequent widgets don't overlap the panels
        ImGui.Dummy(new Vector2(0f, innerBorder));

        ImGui.PopStyleVar(3);
        ImGui.PopFont();
    }

    // -------------------------------------------------------------------------
    // Left panel
    // -------------------------------------------------------------------------

    private static void DrawLeftPanel(MidiDeviceStatus s,
                                      Vector2 clipBtnSize, Vector2 smallBtnSize,
                                      float cellWidth, float baseSpacing, float scale)
    {
        var clipCols = Apc40Mk1.ClipGridColumns;
        var columns  = clipCols + 1; // extra scene/control column
        var clipRows = Apc40Mk1.ClipGridRows;

        if (!ImGui.BeginTable("left_panel_table_" + s.ProductName, columns, ImGuiTableFlags.SizingFixedFit))
            return;

        // cellWidth is the full cell column width; clip buttons are slightly smaller (clipBtnSize)
        var cellW   = cellWidth;
        var clipW   = clipBtnSize.X;
        var clipH   = clipBtnSize.Y;
        var smallW  = smallBtnSize.X;
        var smallH  = smallBtnSize.Y;
        // Make faders taller
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
                var col       = ColorForClipLaunch(colorCode);

                ImGui.PushStyleColor(ImGuiCol.Button,        col);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
                ImGui.Button($"##clip{idx}", new Vector2(clipW, clipH));
                DrawTooltipIfHovered($"Clip Launch R{r + 1}C{c + 1} (idx {idx})", $"Color: {ColorCodeName(colorCode)}");
                ImGui.PopStyleColor(2);
            }

            ImGui.TableSetColumnIndex(clipCols);
            var sceneDef = Apc40Mk1.SceneLaunchDefs[r];
            var sceneCol = ColorForSimpleLed(GetColorCode(s, sceneDef.Id));
            DrawLedButton(sceneDef.Label, sceneDef.Id, sceneCol, new Vector2(clipW, clipH), sceneDef.Tip);
        }

        // --- Clip Stop row ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var def = Apc40Mk1.ClipStopDefs[c];
            var col = ColorForSimpleLed(GetColorCode(s, def.Id));
            DrawLedButton(def.Label, def.Id, col, new Vector2(clipW, clipH), def.Tip);
        }
        ImGui.TableSetColumnIndex(clipCols);
        var stopAllDef = Apc40Mk1.ClipStopAllDef;
        ImGui.PushStyleColor(ImGuiCol.Button, ColorForSimpleLed(GetColorCode(s, stopAllDef.Id)));
        ImGui.Button($"{stopAllDef.Label}##{stopAllDef.Id}", new Vector2(clipW, clipH));
        DrawTooltipIfHovered(stopAllDef.Tip);
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
        ImGui.Button($"{Apc40Mk1.MasterTrackDef.Label}##trkselM", new Vector2(smallW, smallH));
        DrawTooltipIfHovered(Apc40Mk1.MasterTrackDef.Tip);
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
            var def = Apc40Mk1.ActivatorDefs[c];
            var col = ColorForSimpleLed(GetColorCode(s, def.Id));
            DrawLedButton(def.Label, def.Id, col, new Vector2(smallW, smallH), def.Tip);
        }
        ImGui.TableSetColumnIndex(clipCols);
        // remember the top-left of the cell so we can draw the larger cue knob later
        var cueCellPos = ImGui.GetCursorScreenPos();
        // place a small dummy to keep this cell height equal to the small rows
        ImGui.Dummy(new Vector2(smallW, smallH));

        // --- Solo/Cue row (cue knob occupies the scene column) ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var def = Apc40Mk1.SoloCueDefs[c];
            var col = ColorForSimpleLed(GetColorCode(s, def.Id));
            DrawLedButton(def.Label, def.Id, col, new Vector2(smallW, smallH), def.Tip);
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.Dummy(new Vector2(smallW, smallH));

        // --- Record Arm row ---
        ImGui.TableNextRow();
        for (var c = 0; c < clipCols; c++)
        {
            ImGui.TableSetColumnIndex(c);
            var def = Apc40Mk1.RecordArmDefs[c];
            var col = ColorForSimpleLed(GetColorCode(s, def.Id));
            DrawLedButton(def.Label, def.Id, col, new Vector2(smallW, smallH), def.Tip);
        }
        ImGui.TableSetColumnIndex(clipCols);
        ImGui.Dummy(new Vector2(smallW, smallH));

        // Now draw the cue knob at the recorded absolute position so it visually spans the three small rows
        if (cueCellPos != default)
        {
            var prevCursor = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(cueCellPos);
            // Use the shared knob grid helper to draw a single standard knob (no encoder LED ring)
            DrawKnobGrid("cue", Apc40Mk1.CueLevelDef.Id, 1, 1, new Vector2(smallW, combinedCueHeight), s, drawRing: false);
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
            var ch  = Math.Max(0, Math.Min(7, c));
            var idx = ch * 128 + Apc40Mk1.FaderDef.Id;
            if (!_faderDragging[c] && s.ControllerValues != null && idx >= 0 && idx < s.ControllerValues.Length)
                _channelFaderValues[c] = s.ControllerValues[idx];

            var cc2 = c; // capture for lambda
            DrawVerticalFader(_faderIds[c], new Vector2(smallW, faderH),
                              ref _channelFaderValues[c],
                              ref _faderDragging[c],
                              ref _faderDragStartY[c],
                              ref _faderDragStartVal[c],
                              () => $"{Apc40Mk1.FaderDef.Tip} {cc2 + 1}: {Math.Round(_channelFaderValues[cc2] * 100)}%");
        }

        ImGui.TableSetColumnIndex(clipCols);
        {
            const int mi = 8;
            var idxM = 0 * 128 + Apc40Mk1.MasterFaderDef.Id;
            if (!_faderDragging[mi] && s.ControllerValues != null && idxM >= 0 && idxM < s.ControllerValues.Length)
                _channelFaderValues[mi] = s.ControllerValues[idxM];

            DrawVerticalFader("faderM", new Vector2(smallW, faderH),
                              ref _channelFaderValues[mi],
                              ref _faderDragging[mi],
                              ref _faderDragStartY[mi],
                              ref _faderDragStartVal[mi],
                              () => $"{Apc40Mk1.MasterFaderDef.Tip}: {Math.Round(_channelFaderValues[mi] * 100)}%");
        }

        ImGui.EndTable();
    }

    // -------------------------------------------------------------------------
    // Right panel
    // -------------------------------------------------------------------------

    private static void DrawRightPanel(MidiDeviceStatus s,
                                       Vector2 clipBtnSize, Vector2 smallBtnSize,
                                       float scale)
    {
        var btnW = clipBtnSize.X;

        // Remember the left edge (screen X) of the right-panel content so we can align
        // elements (like the crossfader) to the same column grid later.
        var rightPanelStartScreen = ImGui.GetCursorScreenPos();

        // Expand the four columns on the right panel to fill available horizontal space.
        var style = ImGui.GetStyle();
        var avail = ImGui.GetContentRegionAvail().X;
        const int cols = 4;
        // account for spacing between columns
        var totalSpacing = style.ItemSpacing.X * (cols - 1);

        var minKnob = Math.Max(28f * scale, btnW);
        var computedColumnW = MathF.Floor(Math.Max(minKnob, (avail - totalSpacing) / cols));
        var knobRender = new Vector2(computedColumnW, computedColumnW);

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1.5f * scale, 4f * scale));

        // Use computedColumnW for the knob grid and for aligning other right-panel controls.
        DrawKnobGrid("track",  Apc40Mk1.TrackKnobDefs[0].Id, cols, 2, knobRender, s);
        DrawModeKnobLabels(new Vector2(computedColumnW, smallBtnSize.Y), s, computedColumnW);

        ImGui.Spacing();

        DrawBankSelectNavigation(s, scale);

        ImGui.Spacing();

        DrawKnobGrid("device", Apc40Mk1.DeviceKnobDefs[0].Id, cols, 2, knobRender, s);


        ImGui.Spacing();

        // Device control buttons should use the computed column width so columns stay aligned
        DrawDeviceControlButtons(s, new Vector2(computedColumnW, smallBtnSize.Y), computedColumnW);

        ImGui.Spacing();

        DrawTransportButtons(s, scale);
        ImGui.Spacing();
        // Use the centralized crossfader helper (from MidiLayoutDrawHelpers)
        // Compute a target width that covers the four columns plus inter-column spacing so the
        // crossfader visually spans the knob/device columns.
        var crossfaderTargetWidth = computedColumnW * cols + totalSpacing;
        // Align crossfader to the earlier right-panel start X so it spans the exact columns
        var curScreenPos = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(new Vector2(rightPanelStartScreen.X, curScreenPos.Y));
        DrawCrossfader("apc40_xfader", scale, crossfaderTargetWidth,
                       ref _crossfaderValue,
                       ref _crossfaderDragging,
                       ref _crossfaderDragStartX,
                       ref _crossfaderDragStartVal,
                       s,
                       Apc40Mk1.CrossfaderDef.Id);
        // Restore cursor X to the left edge of the right panel content so subsequent items
        // continue at the expected column alignment.
        ImGui.SetCursorScreenPos(new Vector2(rightPanelStartScreen.X, ImGui.GetCursorScreenPos().Y));

        ImGui.PopStyleVar();
    }

    // -------------------------------------------------------------------------
    // Right-panel sub-sections
    // -------------------------------------------------------------------------

    /// <summary>Draws PAN / SEND A / SEND B / SEND C mode-select buttons aligned to knob columns.</summary>
    private static void DrawModeKnobLabels(Vector2 btnSize, MidiDeviceStatus s, float columnWidth)
    {
        var defs = Apc40Mk1.ModeKnobDefs;

        if (!ImGui.BeginTable("mode_labels_table_" + s.ProductName, 4, ImGuiTableFlags.SizingFixedFit))
            return;

        for (var c = 0; c < 4; c++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, columnWidth);

        ImGui.TableNextRow();
        for (var i = 0; i < 4 && i < defs.Length; i++)
        {
            ImGui.TableSetColumnIndex(i);
            var def       = defs[i];
            var colorCode = GetColorCode(s, def.Id);
            var col       = colorCode > 0 ? GreenColor : OffColor;
            DrawLedButton(def.Label, def.Id, col, btnSize, def.Tip);
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws the Bank Select section:
    ///   Col 0: SHIFT (centered)
    ///   Col 1-3: ← ↑↓ →
    ///   Col 4: TAP TEMPO, NUD-, NUD+
    /// </summary>
    private static void DrawBankSelectNavigation(MidiDeviceStatus s, float scale)
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
        ImGui.TableSetColumnIndex(2); DrawIconButton(s, Icon.ArrowUp, Apc40Mk1.BankUpDef.Id, btn, Apc40Mk1.BankUpDef.Tip);
        ImGui.TableSetColumnIndex(3); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(4); DrawSimpleButton(s, Apc40Mk1.TapTempoDef.Label, Apc40Mk1.TapTempoDef.Id, btn, Apc40Mk1.TapTempoDef.Tip);

        // Row 1: SHIFT | ← | empty | → | NUD-
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); DrawSimpleButton(s, Apc40Mk1.ShiftDef.Label, Apc40Mk1.ShiftDef.Id, btn, Apc40Mk1.ShiftDef.Tip);
        ImGui.TableSetColumnIndex(1); DrawIconButton(s, Icon.ArrowLeft, Apc40Mk1.BankLeftDef.Id, btn, Apc40Mk1.BankLeftDef.Tip);
        ImGui.TableSetColumnIndex(2); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(3); DrawIconButton(s, Icon.ArrowRight, Apc40Mk1.BankRightDef.Id, btn, Apc40Mk1.BankRightDef.Tip);
        ImGui.TableSetColumnIndex(4); DrawSimpleButton(s, Apc40Mk1.NudgeMinusDef.Label, Apc40Mk1.NudgeMinusDef.Id, btn, Apc40Mk1.NudgeMinusDef.Tip);

        // Row 2: empty | empty | ↓ | empty | NUD+
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(1); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(2); DrawIconButton(s, Icon.ArrowDown, Apc40Mk1.BankDownDef.Id, btn, Apc40Mk1.BankDownDef.Tip);
        ImGui.TableSetColumnIndex(3); ImGui.Dummy(btn);
        ImGui.TableSetColumnIndex(4); DrawSimpleButton(s, Apc40Mk1.NudgePlusDef.Label, Apc40Mk1.NudgePlusDef.Id, btn, Apc40Mk1.NudgePlusDef.Tip);

        ImGui.EndTable();
    }

    /// <summary>Draws Device Control buttons (2 rows × 4) aligned to knob columns.</summary>
    private static void DrawDeviceControlButtons(MidiDeviceStatus s,
                                                 Vector2 btnSize, float columnWidth)
    {
        var defs = Apc40Mk1.DeviceControlDefs;
        const int cols = 4;
        if (!ImGui.BeginTable("deviceCtrlBtnTable_" + s.ProductName, cols, ImGuiTableFlags.SizingFixedFit))
            return;

        for (var c = 0; c < cols; c++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, columnWidth);

        for (var i = 0; i < defs.Length; i++)
        {
            if (i % cols == 0) ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(i % cols);

            var def      = defs[i];
            var cellSize = new Vector2(columnWidth, btnSize.Y);

            if (def.Id == Apc40Mk1.DeviceLeftId)
                DrawIconButton(s, Icon.ChevronLeft,  def.Id, cellSize, def.Tip);
            else if (def.Id == Apc40Mk1.DeviceRightId)
                DrawIconButton(s, Icon.ChevronRight, def.Id, cellSize, def.Tip);
            else
                DrawSimpleButton(s, def.Label, def.Id, cellSize, def.Tip);
        }

        ImGui.EndTable();
    }

    /// <summary>Draws Play / Stop / Record transport buttons.</summary>
    private static void DrawTransportButtons(MidiDeviceStatus s, float scale)
    {
        var size = new Vector2(40 * scale, 22 * scale);

        var style = ImGui.GetStyle();
        var itemSpacing = style.ItemSpacing.X;
        var totalButtons = 3;
        var totalWidth = size.X * totalButtons + itemSpacing * (totalButtons - 1);
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > totalWidth)
        {
            var curX = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(curX + (avail - totalWidth) * 0.5f);
        }

        var colorCode = GetColorCode(s, Apc40Mk1.PlayDef.Id);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = colorCode > 0 ? new Vector4(0.1f, 0.85f, 0.2f, 1f) : OffColor;
        DrawIconButtonWithBg(Icon.PlayForwards, size, bgCol, state);
        DrawTooltipIfHovered(Apc40Mk1.PlayDef.Tip);

        ImGui.SameLine();
        DrawStopButton(Apc40Mk1.StopDef.Id, s, size);
        DrawTooltipIfHovered(Apc40Mk1.StopDef.Tip);

        ImGui.SameLine();
        DrawRecordButton(Apc40Mk1.RecordDef.Id, s, size);
        DrawTooltipIfHovered(Apc40Mk1.RecordDef.Tip);
    }


    // -------------------------------------------------------------------------
    // Shared sub-widgets
    // -------------------------------------------------------------------------


}
