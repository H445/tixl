using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Shared drawing primitives, color helpers and utility methods for MIDI controller layout views.
/// All members are static so any controller layout class can reuse them without inheritance.
/// </summary>
internal static class MidiLayoutDrawHelpers
{
    #region Color Constants

    internal static readonly Vector4 GreenColor  = new(0.1f,  0.85f, 0.2f,  1f);
    internal static readonly Vector4 RedColor    = new(0.9f,  0.15f, 0.1f,  1f);
    internal static readonly Vector4 YellowColor = new(0.95f, 0.75f, 0.05f, 1f);
    internal static readonly Vector4 OffColor    = new(0.25f, 0.25f, 0.25f, 1f);
    internal static readonly Vector4 DimColor    = new(0.15f, 0.15f, 0.15f, 0.8f);

    #endregion

    #region Color Mapping

    internal static Vector4 ColorForClipLaunch(int colorCode, bool blinkOn)
    {
        return colorCode switch
        {
            0    => OffColor,
            1    => GreenColor,
            2    => blinkOn ? GreenColor  : DimColor,
            3    => RedColor,
            4    => blinkOn ? RedColor    : DimColor,
            5    => YellowColor,
            6    => blinkOn ? YellowColor : DimColor,
            >= 7 => GreenColor,
            _    => OffColor
        };
    }

    internal static Vector4 ColorForSimpleLed(int colorCode, bool blinkOn)
    {
        return colorCode switch
        {
            0    => OffColor,
            2    => blinkOn ? GreenColor : DimColor,
            >= 1 => GreenColor,
            _    => OffColor
        };
    }

    internal static string ColorCodeName(int code)
    {
        return code switch
        {
            0    => "Off",
            1    => "Green",
            2    => "Green Blink",
            3    => "Red",
            4    => "Red Blink",
            5    => "Yellow",
            6    => "Yellow Blink",
            >= 7 => $"Green ({code})",
            _    => $"Unknown ({code})"
        };
    }

    internal static Vector4 BrightenColor(Vector4 col, float factor)
    {
        return new Vector4(
            Math.Min(col.X * factor, 1f),
            Math.Min(col.Y * factor, 1f),
            Math.Min(col.Z * factor, 1f),
            col.W);
    }

    #endregion

    #region Shared Draw Helpers

    /// <summary>Safe color code lookup from ControllerColors array.</summary>
    internal static int GetColorCode(MidiDeviceStatus s, int noteId)
        => noteId >= 0 && noteId < s.ControllerColors.Length ? s.ControllerColors[noteId] : -1;

    /// <summary>Draws a tooltip on the last item if hovered.</summary>
    internal static void DrawTooltipIfHovered(string text)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    /// <summary>Draws a tooltip with two lines on the last item if hovered.</summary>
    internal static void DrawTooltipIfHovered(string line1, string line2)
    {
        if (!ImGui.IsItemHovered()) return;
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(line1);
        ImGui.TextUnformatted(line2);
        ImGui.EndTooltip();
    }

    /// <summary>Draws a section label in muted text.</summary>
    internal static void DrawSectionLabel(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
    }

    /// <summary>Draws a colored LED button with tooltip.</summary>
    internal static void DrawLedButton(string label, int noteId, Vector4 col, Vector2 size, string tooltip)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, col);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, BrightenColor(col, 1.2f));
        // Reduce top padding for compact/small buttons so they don't appear vertically offset
        // compared to other compact elements. Keep horizontal padding from current style.
        var currentPadX = ImGui.GetStyle().FramePadding.X;
        // Use a small fraction of the button height for vertical padding (rounded down)
        var reducedPadY = MathF.Floor(Math.Max(0f, size.Y * 0.12f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(currentPadX, reducedPadY));

        ImGui.Button($"{label}##{noteId}", size);
        DrawTooltipIfHovered(tooltip);

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar();
    }

    /// <summary>Draws a simple LED button whose color is derived from the device color cache.</summary>
    internal static void DrawSimpleButton(MidiDeviceStatus s, string label, int noteId, Vector2 size, bool blinkOn, string tooltipLabel)
    {
        var col = ColorForSimpleLed(GetColorCode(s, noteId), blinkOn);
        DrawLedButton(label, noteId, col, size, $"{tooltipLabel} (Note {noteId})");
    }

    /// <summary>Draws an icon button with background color based on LED state.</summary>
    internal static void DrawIconButton(MidiDeviceStatus s, Icon icon, int noteId, Vector2 size, bool blinkOn, string tooltip)
    {
        var colorCode = GetColorCode(s, noteId);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = ColorForSimpleLed(colorCode, blinkOn);
        DrawIconButtonWithBg(icon, size, bgCol, state);
        DrawTooltipIfHovered(tooltip);
    }

    /// <summary>Draws an icon button with an explicit background color.</summary>
    internal static void DrawIconButtonWithBg(Icon icon, Vector2 size, Vector4 bgCol, CustomComponents.ButtonStates state)
    {
        DrawStyledButton((int)icon, bgCol, state, size, () => Icons.DrawIconOnLastItem(icon, GetStateColorVec(state)));
    }

    /// <summary>Draws a transport Stop button (square shape).</summary>
    internal static void DrawStopButton(int noteId, MidiDeviceStatus s, Vector2 size, bool blinkOn)
    {
        var colorCode = GetColorCode(s, noteId);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = ColorForSimpleLed(colorCode, blinkOn);

        DrawStyledButton(noteId, bgCol, state, size, () =>
        {
            var sysColor = GetStateColorVec(state);
            var min      = ImGui.GetItemRectMin();
            var max      = ImGui.GetItemRectMax();
            var center   = (min + max) / 2f;
            DrawStopShape(ImGui.GetWindowDrawList(), center, Icons.FontSize, ImGui.GetColorU32(sysColor));
        });
    }

    /// <summary>Draws a transport Record button (circle shape).</summary>
    internal static void DrawRecordButton(int noteId, MidiDeviceStatus s, Vector2 size, bool blinkOn)
    {
        var colorCode = GetColorCode(s, noteId);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = ColorForSimpleLed(colorCode, blinkOn);

        DrawStyledButton(noteId, bgCol, state, size, () =>
        {
            var sysColor = GetStateColorVec(state);
            var min      = ImGui.GetItemRectMin();
            var max      = ImGui.GetItemRectMax();
            var center   = (min + max) / 2f;
            DrawRecordShape(ImGui.GetWindowDrawList(), center, Icons.FontSize, ImGui.GetColorU32(sysColor));
        });
    }

    internal static void DrawStopShape(ImDrawListPtr dl, Vector2 center, float iconSize, uint color)
    {
        var iconMin = new Vector2(center.X - iconSize / 2f, center.Y - iconSize / 2f).Floor();
        var iconMax = iconMin + new Vector2(iconSize, iconSize);
        dl.AddRectFilled(iconMin, iconMax, color, 2f);
    }

    internal static void DrawRecordShape(ImDrawListPtr dl, Vector2 center, float iconSize, uint color)
    {
        dl.AddCircleFilled(center.Floor(), iconSize / 2f, color, 16);
    }

    /// <summary>
    /// Pushes common button styling, emits an invisible button, then invokes <paramref name="drawContent"/>
    /// to render an icon or custom shape on top.
    /// </summary>
    internal static void DrawStyledButton(object idKey, Vector4 bgCol, CustomComponents.ButtonStates state, Vector2 size, Action drawContent)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,   UiColors.BackgroundButtonActivated.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Button,         bgCol);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered,  BrightenColor(bgCol, 1.2f));

        ImGui.PushID(idKey?.ToString() ?? string.Empty);
        ImGui.Button(string.Empty, size);
        ImGui.PopID();

        drawContent();

        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(1);
    }

    /// <summary>Draws a knob grid (rows × cols) reading CC values starting at <paramref name="ccStart"/>.</summary>
    internal static void DrawKnobGrid(string idPrefix, int ccStart, int cols, int rows, Vector2 size,
                                      MidiDeviceStatus s, bool blinkOn)
    {
        var dl      = ImGui.GetWindowDrawList();
        var padding = 2f;

        if (!ImGui.BeginTable($"knobTable_{idPrefix}", cols, ImGuiTableFlags.SizingFixedFit))
            return;

        for (var tc = 0; tc < cols; tc++)
            ImGui.TableSetupColumn(null, ImGuiTableColumnFlags.WidthFixed, size.X);

        for (var r = 0; r < rows; r++)
        {
            ImGui.TableNextRow();
            for (var c = 0; c < cols; c++)
            {
                ImGui.TableSetColumnIndex(c);
                var idx = r * cols + c;
                var cc  = ccStart + idx;

                ImGui.PushID($"{idPrefix}_{cc}");
                ImGui.InvisibleButton($"##knob{cc}", size);

                var min    = ImGui.GetItemRectMin();
                var max    = ImGui.GetItemRectMax();
                var center = (min + max) / 2f;
                var radius = Math.Min(max.X - min.X, max.Y - min.Y) / 2f - padding;

                dl.AddCircleFilled(center, radius * 0.7f, ImGui.GetColorU32(UiColors.BackgroundFull.Rgba));

                float value = 0f;
                if (s.ControllerValues != null)
                {
                    var valIdx = 0 * 128 + cc;
                    if (valIdx >= 0 && valIdx < s.ControllerValues.Length)
                        value = s.ControllerValues[valIdx];
                }

                var startAngle     = -MathF.PI * 0.75f;
                var endAngle       = MathF.PI  * 0.75f;
                var angle          = startAngle + (endAngle - startAngle) * ClampF(value, 0f, 1f);
                var indicatorLen   = radius * 0.5f;
                var indicatorPos   = new Vector2(
                    center.X + MathF.Cos(angle) * indicatorLen,
                    center.Y + MathF.Sin(angle) * indicatorLen);
                dl.AddCircleFilled(indicatorPos, radius * 0.12f, ImGui.GetColorU32(UiColors.Text.Rgba));

                var colorCode = GetColorCode(s, cc);
                var ringCol   = ColorForSimpleLed(colorCode, blinkOn);
                dl.AddCircle(center, radius, ImGui.GetColorU32(ringCol), 32, 2f);

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"{idPrefix} {idx + 1} (CC {cc})");
                    ImGui.TextUnformatted($"Value: {Math.Round(value * 100)}%");
                    ImGui.EndTooltip();
                }

                ImGui.PopID();
            }
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws a realistic vertical fader that visually matches the crossfader style.
    /// Handles click-to-set and drag interaction; only syncs <paramref name="value"/>
    /// from the caller when <paramref name="isDragging"/> is false.
    /// Returns true while the user is actively dragging.
    /// </summary>
    /// <param name="id">Unique ImGui id string.</param>
    /// <param name="size">Widget size in pixels.</param>
    /// <param name="value">Current fader value in [0,1]; updated by interaction.</param>
    /// <param name="isDragging">Drag state persisted by the caller across frames.</param>
    /// <param name="dragStartY">Screen-Y captured at drag start (persisted by caller).</param>
    /// <param name="dragStartVal">Value captured at drag start (persisted by caller).</param>
    /// <param name="tooltip">Optional tooltip text shown on hover.</param>
    internal static void DrawVerticalFader(string id, Vector2 size,
                                           ref float value,
                                           ref bool  isDragging,
                                           ref float dragStartY,
                                           ref float dragStartVal,
                                           string    tooltip = "")
    {
        ImGui.PushID(id);
        ImGui.InvisibleButton($"##vf_{id}", size);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl  = ImGui.GetWindowDrawList();

        var grooveInset = 4f;
        var thumbH      = 12f;
        var thumbInset  = thumbH * 0.5f + grooveInset;
        var thumbTravel = size.Y - 2f * thumbInset;

        // ---- Interaction ----
        var isHovered = ImGui.IsItemHovered();
        var io        = ImGui.GetIO();
        var trackTopY = min.Y + thumbInset;

        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // fader value 0 = bottom, 1 = top → invert mouse delta
            var clickedVal = 1f - (io.MousePos.Y - trackTopY) / thumbTravel;
            value        = ClampF(clickedVal, 0f, 1f);
            isDragging   = true;
            dragStartY   = io.MousePos.Y;
            dragStartVal = value;
        }

        if (isDragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var delta  = io.MousePos.Y - dragStartY;
                var newVal = dragStartVal - delta / thumbTravel; // subtract: up = higher value
                value = ClampF(newVal, 0f, 1f);
            }
            else
            {
                isDragging = false;
            }
        }

        // ---- Drawing ----
        var bgColor      = ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.20f, 1f));
        var grooveColor  = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.14f, 1f));
        var tickMajorCol = ImGui.GetColorU32(new Vector4(0.55f, 0.55f, 0.58f, 1f));
        var tickMinorCol = ImGui.GetColorU32(new Vector4(0.38f, 0.38f, 0.40f, 1f));
        var thumbCol     = ImGui.GetColorU32(UiColors.Text.Rgba);

        // Background
        dl.AddRectFilled(min, max, bgColor, 3f);

        // Groove — narrow vertical slot down the centre
        var grooveX = (min.X + max.X) * 0.5f;
        var grooveW = 3f;
        dl.AddRectFilled(
            new Vector2(grooveX - grooveW * 0.5f, min.Y + grooveInset),
            new Vector2(grooveX + grooveW * 0.5f, max.Y - grooveInset),
            grooveColor, 1f);

        // Tick marks — major at 0 / 25 / 50 / 75 / 100 %, minor subdivisions between
        var majorHalfW = size.X * 0.28f;   // extends either side of groove
        var minorHalfW = size.X * 0.14f;

        for (var i = 0; i <= 8; i++)   // i=0..8 gives 0%, 12.5% … 100%
        {
            var t       = i / 8f;
            var ty      = trackTopY + thumbTravel * (1f - t);
            var isMajor = (i % 2 == 0);
            var hw      = isMajor ? majorHalfW : minorHalfW;
            var col     = isMajor ? tickMajorCol : tickMinorCol;
            dl.AddLine(new Vector2(grooveX - hw, ty), new Vector2(grooveX + hw, ty), col, 1f);
        }

        // Thumb — a short wide flat bar that slides along the groove
        var thumbY   = trackTopY + thumbTravel * (1f - ClampF(value, 0f, 1f));
        var tHalf    = 5f;    // half-height of the thumb bar
        var thumbXl  = min.X + 3f;
        var thumbXr  = max.X - 3f;
        dl.AddRectFilled(
            new Vector2(thumbXl, thumbY - tHalf),
            new Vector2(thumbXr, thumbY + tHalf),
            thumbCol, 2f);

        if (!string.IsNullOrEmpty(tooltip))
            DrawTooltipIfHovered(tooltip);

        ImGui.PopID();
    }

    /// <summary>Draws a realistic A/B crossfader with click and drag support.
    /// The caller owns persistent state (value + drag flags) and passes them by ref so
    /// the helper can update them during interaction. The helper will also sync from
    /// the device when the user is not dragging.
    /// </summary>
    /// <param name="id">Unique ImGui id string.</param>
    /// <param name="scale">Scale factor for overall size.</param>
    /// <param name="targetWidthPx">Target width in pixels (replaces fixed 140px).</param>
    /// <param name="value">Current fader value in [0,1]; updated by interaction.</param>
    /// <param name="isDragging">Drag state persisted by the caller across frames.</param>
    /// <param name="dragStartX">Screen-X captured at drag start (persisted by caller).</param>
    /// <param name="dragStartVal">Value captured at drag start (persisted by caller).</param>
    /// <param name="s">MIDI device status, for reading CC values.</param>
    /// <param name="cc">CC number this fader is controlling.</param>
    /// <param name="blinkOn">Whether to use blinking colors for LED feedback.</param>
    internal static void DrawCrossfader(string id, float scale, float targetWidthPx,
                                        ref float value,
                                        ref bool  isDragging,
                                        ref float dragStartX,
                                        ref float dragStartVal,
                                        MidiDeviceStatus s,
                                        int cc,
                                        bool blinkOn)
     {
         // Read from device when the user isn't dragging
         var valIdx = 0 * 128 + cc;
         if (!isDragging && s.ControllerValues != null && valIdx >= 0 && valIdx < s.ControllerValues.Length)
             value = s.ControllerValues[valIdx];

        // Default size previously was 140 x 22 multiplied by scale. Use the provided target width
        // but keep a sensible minimum so visuals don't collapse on very narrow panels.
        var minWidth = 80f * scale;
        var width = MathF.Max(targetWidthPx, minWidth);
        var height = 22f * scale;
        var size   = new Vector2(width, height);

        // Layout constants – must match the drawing below
        var grooveInset = 4f * scale;
        var thumbW      = 12f * scale;
        var thumbInset  = thumbW * 0.5f + grooveInset;
        var thumbTravel = width - 2f * thumbInset;

        ImGui.PushID(id);
        ImGui.InvisibleButton($"##xfader_{id}", size);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl  = ImGui.GetWindowDrawList();

        // ---- Interaction ----
        var isHovered = ImGui.IsItemHovered();
        var io        = ImGui.GetIO();

        var trackMinX = min.X + thumbInset;

        if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // Immediately jump to clicked position
            var clickedVal = (io.MousePos.X - trackMinX) / thumbTravel;
            value        = ClampF(clickedVal, 0f, 1f);
            isDragging   = true;
            dragStartX   = io.MousePos.X;
            dragStartVal = value;
        }

        if (isDragging)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var delta = io.MousePos.X - dragStartX;
                var newVal = dragStartVal + delta / thumbTravel;
                value = ClampF(newVal, 0f, 1f);
            }
            else
            {
                isDragging = false;
            }
        }

        // ---- Drawing ----
        var bgColor      = ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.20f, 1f));
        var grooveColor  = ImGui.GetColorU32(new Vector4(0.12f, 0.12f, 0.14f, 1f));
        var tickMajorCol = ImGui.GetColorU32(new Vector4(0.55f, 0.55f, 0.58f, 1f));
        var tickMinorCol = ImGui.GetColorU32(new Vector4(0.38f, 0.38f, 0.40f, 1f));
        var thumbCol     = ImGui.GetColorU32(UiColors.Text.Rgba);
        var labelColor   = ImGui.GetColorU32(new Vector4(0.5f,  0.5f,  0.55f, 1f));

        // Background
        dl.AddRectFilled(min, max, bgColor, 3f);

        // Groove — narrow horizontal slot through the centre
        var grooveY = (min.Y + max.Y) * 0.5f;
        var grooveH = 3f;
        dl.AddRectFilled(
            new Vector2(min.X + grooveInset, grooveY - grooveH * 0.5f),
            new Vector2(max.X - grooveInset, grooveY + grooveH * 0.5f),
            grooveColor, 1f);

        // Tick marks — major at 0 / 50 / 100 %, minor at 25 / 75 %
        var majorHalfH = height * 0.35f;
        var minorHalfH = height * 0.18f;

        foreach (var (t, isMajor) in new[] { (0.25f, false), (0.5f, true), (0.75f, false) })
        {
            var tx  = min.X + thumbInset + thumbTravel * t;
            var hh  = isMajor ? majorHalfH : minorHalfH;
            var col = isMajor ? tickMajorCol : tickMinorCol;
            dl.AddLine(new Vector2(tx, grooveY - hh), new Vector2(tx, grooveY + hh), col, 1f);
        }

        // Thumb — a short vertical flat bar that slides along the groove
        var thumbX  = min.X + thumbInset + thumbTravel * ClampF(value, 0f, 1f);
        var tHalf   = 4f;   // half-width of the thumb bar
        var thumbYT = min.Y + 3f;
        var thumbYB = max.Y - 3f;
        dl.AddRectFilled(
            new Vector2(thumbX - tHalf, thumbYT),
            new Vector2(thumbX + tHalf, thumbYB),
            thumbCol, 2f);

        // A / B labels
        ImGui.PushFont(Fonts.FontSmall);
        var aSize = ImGui.CalcTextSize("A");
        var bSize = ImGui.CalcTextSize("B");
        dl.AddText(new Vector2(min.X + grooveInset,         grooveY - aSize.Y * 0.5f), labelColor, "A");
        dl.AddText(new Vector2(max.X - grooveInset - bSize.X, grooveY - bSize.Y * 0.5f), labelColor, "B");
        ImGui.PopFont();

        DrawTooltipIfHovered($"A-B Crossfader: {Math.Round(value * 100)}%");

        ImGui.PopID();
    }

    #endregion

    #region State / Utility

    internal static Vector4 GetStateColorVec(CustomComponents.ButtonStates state)
    {
        return state switch
        {
            CustomComponents.ButtonStates.Dimmed        => UiColors.Text.Fade(0.8f).Rgba,
            CustomComponents.ButtonStates.Disabled      => UiColors.TextDisabled.Fade(0.6f).Rgba,
            CustomComponents.ButtonStates.Activated     => UiColors.StatusActivated.Rgba,
            CustomComponents.ButtonStates.NeedsAttention => UiColors.StatusAttention.Rgba,
            _                                           => UiColors.Text.Rgba
        };
    }

    internal static float ClampF(float v, float lo, float hi) => Math.Max(lo, Math.Min(hi, v));

    #endregion
}


