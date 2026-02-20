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
        ImGui.Button($"{label}##{noteId}", size);
        DrawTooltipIfHovered(tooltip);
        ImGui.PopStyleColor(2);
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
        var thumbXL  = min.X + 3f;
        var thumbXR  = max.X - 3f;
        dl.AddRectFilled(
            new Vector2(thumbXL, thumbY - tHalf),
            new Vector2(thumbXR, thumbY + tHalf),
            thumbCol, 2f);

        if (!string.IsNullOrEmpty(tooltip))
            DrawTooltipIfHovered(tooltip);

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


