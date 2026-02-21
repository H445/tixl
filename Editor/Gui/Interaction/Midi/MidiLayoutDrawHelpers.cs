using ImGuiNET;
using System;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;
using Operators.Utils; // for MidiConnectionManager.TryGetMidiOut
using NAudio.Midi;
using System.Collections.Generic;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Shared drawing primitives, color helpers and utility methods for MIDI controller layout views.
/// All members are static so any controller layout class can reuse them without inheritance.
/// </summary>
internal static class MidiLayoutDrawHelpers
{
    // Persisted local state for knob interaction (avoid creating these inside the method)
    private static readonly Dictionary<string, int> _lastSentCc = new();
    private static readonly Dictionary<string, float> _localControllerOverrides = new();
    private static readonly Dictionary<string, int> _overrideSetTimeMs = new();
    private static readonly Dictionary<string, float> _lastSeenDeviceValue = new();

    /// <summary>True for 500 ms, false for the next 500 ms, driven purely by ImGui time.</summary>
    private static bool BlinkOn => (int)(ImGui.GetTime() / 0.5) % 2 == 0;

    #region Color Constants

    internal static readonly Vector4 GreenColor  = new(0.1f,  0.85f, 0.2f,  1f);
    internal static readonly Vector4 RedColor    = new(0.9f,  0.15f, 0.1f,  1f);
    internal static readonly Vector4 YellowColor = new(0.95f, 0.75f, 0.05f, 1f);
    internal static readonly Vector4 OffColor    = new(0.25f, 0.25f, 0.25f, 1f);
    internal static readonly Vector4 DimColor    = new(0.15f, 0.15f, 0.15f, 0.8f);

    #endregion

    #region Color Mapping

    internal static Vector4 ColorForClipLaunch(int colorCode)
    {
        return colorCode switch
        {
            0    => OffColor,
            1    => GreenColor,
            2    => BlinkOn ? GreenColor  : DimColor,
            3    => RedColor,
            4    => BlinkOn ? RedColor    : DimColor,
            5    => YellowColor,
            6    => BlinkOn ? YellowColor : DimColor,
            >= 7 => GreenColor,
            _    => OffColor
        };
    }

    internal static Vector4 ColorForSimpleLed(int colorCode)
    {
        return colorCode switch
        {
            0    => OffColor,
            2    => BlinkOn ? GreenColor : DimColor,
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
    internal static void DrawSimpleButton(MidiDeviceStatus s, string label, int noteId, Vector2 size, string tooltipLabel)
    {
        var col = ColorForSimpleLed(GetColorCode(s, noteId));
        DrawLedButton(label, noteId, col, size, $"{tooltipLabel} (Note {noteId})");
    }

    /// <summary>Draws an icon button with background color based on LED state.</summary>
    internal static void DrawIconButton(MidiDeviceStatus s, Icon icon, int noteId, Vector2 size, string tooltip)
    {
        var colorCode = GetColorCode(s, noteId);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = ColorForSimpleLed(colorCode);
        DrawIconButtonWithBg(icon, size, bgCol, state);
        DrawTooltipIfHovered(tooltip);
    }

    /// <summary>Draws an icon button with an explicit background color.</summary>
    internal static void DrawIconButtonWithBg(Icon icon, Vector2 size, Vector4 bgCol, CustomComponents.ButtonStates state)
    {
        DrawStyledButton((int)icon, bgCol, state, size, () => Icons.DrawIconOnLastItem(icon, GetStateColorVec(state)));
    }

    /// <summary>Draws a transport Stop button (square shape).</summary>
    internal static void DrawStopButton(int noteId, MidiDeviceStatus s, Vector2 size)
    {
        var colorCode = GetColorCode(s, noteId);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = ColorForSimpleLed(colorCode);

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
    internal static void DrawRecordButton(int noteId, MidiDeviceStatus s, Vector2 size)
    {
        var colorCode = GetColorCode(s, noteId);
        var state     = colorCode > 0 ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;
        var bgCol     = ColorForSimpleLed(colorCode);

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

    // ---- Per-knob LED Ring Mode state (persisted across frames) ----
    // Key: "prefix_cc", Value: 0=Off, 1=Single, 2=Volume, 3=Pan  (APC40 protocol)
    private static readonly Dictionary<string, int> _knobRingModes = new();
    private static readonly string[] _ringModeNames = { "Off", "Single", "Volume", "Pan" };

    /// <summary>Gets the ring mode for a specific knob, defaulting to Single (1).</summary>
    private static int GetKnobRingMode(string key) =>
        _knobRingModes.TryGetValue(key, out var mode) ? mode : 1;

    /// <summary>
    /// Resolves the display value for a knob by reading the device CC value,
    /// applying any active UI override, and clearing stale overrides when the
    /// hardware regains control. Keeps DrawKnobGrid focused on rendering.
    /// </summary>
    private static float ResolveKnobValue(string overrideKey, int cc, MidiDeviceStatus s)
    {
        const float eps = 1f / 127f;
        var isActive = ImGui.IsItemActive();

        // 1. Read base value from device (channel 1)
        float value = 0f;
        var valIdx = cc; // channel 0 * 128 + cc
        if (s.ControllerValues != null && valIdx >= 0 && valIdx < s.ControllerValues.Length)
            value = s.ControllerValues[valIdx];

        // 2. Find canonical device value (first channel that has data)
        var deviceVal = float.NaN;
        if (s.ControllerValues != null)
        {
            for (var ch = 0; ch < 16; ch++)
            {
                var idx = ch * 128 + cc;
                if (idx >= 0 && idx < s.ControllerValues.Length)
                {
                    deviceVal = s.ControllerValues[idx];
                    break;
                }
            }
        }

        // 3. Clear override if hardware moved
        if (!float.IsNaN(deviceVal))
        {
            if (_lastSeenDeviceValue.TryGetValue(overrideKey, out var prev) &&
                Math.Abs(deviceVal - prev) > eps &&
                _localControllerOverrides.ContainsKey(overrideKey) && !isActive)
            {
                ClearOverride(overrideKey);
            }
            _lastSeenDeviceValue[overrideKey] = deviceVal;
        }

        // 4. Clear override if device disagrees and user isn't dragging, or after timeout
        if (_localControllerOverrides.TryGetValue(overrideKey, out var ov) && s.ControllerValues != null)
        {
            var differs = false;
            for (var ch = 0; ch < 16; ch++)
            {
                var idx = ch * 128 + cc;
                if (idx < 0 || idx >= s.ControllerValues.Length) continue;
                if (Math.Abs(s.ControllerValues[idx] - ov) > eps) { differs = true; break; }
            }

            if (!isActive && differs)
            {
                ClearOverride(overrideKey);
            }
            else if (!isActive && _overrideSetTimeMs.TryGetValue(overrideKey, out var setMs) &&
                     Math.Abs(Environment.TickCount - setMs) > 200)
            {
                ClearOverride(overrideKey);
            }
        }

        // 5. Apply surviving override
        if (_localControllerOverrides.TryGetValue(overrideKey, out var ov2))
            value = ov2;

        // 6. Clear on mouse release
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && !isActive &&
            _localControllerOverrides.ContainsKey(overrideKey))
        {
            ClearOverride(overrideKey);
        }

        return value;
    }

    /// <summary>Removes all cached state for a knob override key.</summary>
    private static void ClearOverride(string key)
    {
        _localControllerOverrides.Remove(key);
        _lastSentCc.Remove(key);
        _overrideSetTimeMs.Remove(key);
    }

    /// <summary>Draws a knob grid (rows × cols) reading CC values starting at <paramref name="ccStart"/>.</summary>
    internal static void DrawKnobGrid(string idPrefix, int ccStart, int cols, int rows, Vector2 size,
                                      MidiDeviceStatus s)
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

                var overrideKey = idPrefix + "_" + cc;
                var value = ResolveKnobValue(overrideKey, cc, s);

                // ---- Draw knob indicator (position dot) ----
                var startAngle     = -MathF.PI * 0.75f;
                var endAngle       = MathF.PI  * 0.75f;
                var angle          = startAngle + (endAngle - startAngle) * ClampF(value, 0f, 1f) - MathF.PI * 0.5f;
                var indicatorLen   = radius * 0.5f;
                var indicatorPos   = new Vector2(
                    center.X + MathF.Cos(angle) * indicatorLen,
                    center.Y + MathF.Sin(angle) * indicatorLen);
                dl.AddCircleFilled(indicatorPos, radius * 0.12f, ImGui.GetColorU32(UiColors.Text.Rgba));

                // ---- Draw LED ring segments per APC40 protocol ring mode ----
                var knobRingMode = GetKnobRingMode(overrideKey);
                DrawEncoderRing(dl, center, radius, value, knobRingMode);

                var isHovered = ImGui.IsItemHovered();
                var io = ImGui.GetIO();

                // ---- Right-click context menu to change LED ring type ----
                if (isHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                    ImGui.OpenPopup($"ringCtx_{overrideKey}");

                if (ImGui.BeginPopup($"ringCtx_{overrideKey}"))
                {
                    ImGui.TextUnformatted($"{idPrefix} {idx + 1} (CC {cc})");
                    ImGui.Separator();
                    for (var mi = 0; mi < _ringModeNames.Length; mi++)
                    {
                        var selected = knobRingMode == mi;
                        if (ImGui.Selectable(_ringModeNames[mi], selected))
                        {
                            _knobRingModes[overrideKey] = mi;
                            // Send ring type CC to hardware for this specific knob
                            SendKnobRingTypeToHardware(s, idPrefix, idx, mi);
                        }
                    }
                    ImGui.EndPopup();
                }

                // Interaction: allow dragging to change the controller value.
                if (ImGui.IsItemActive() || (isHovered && ImGui.IsMouseDown(ImGuiMouseButton.Left)))
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"{idPrefix} {idx + 1} (CC {cc})");
                    ImGui.TextUnformatted($"Value: {Math.Round(value * 100)}%  ({(int)MathF.Round(value * 127f)}/127)");
                    ImGui.EndTooltip();

                    var sensitivity = 0.005f * MathF.Max(1f, size.X / 40f);
                    var delta = -io.MouseDelta.Y * sensitivity;
                    if (Math.Abs(delta) > 0)
                    {
                        var newVal = ClampF(value + delta, 0f, 1f);
                        _localControllerOverrides[overrideKey] = newVal;
                        _overrideSetTimeMs[overrideKey] = Environment.TickCount;

                        var intVal = (int)MathF.Round(newVal * 127f);
                        if (!_lastSentCc.TryGetValue(overrideKey, out var last) || last != intVal)
                        {
                            _lastSentCc[overrideKey] = intVal;
                            if (MidiConnectionManager.TryGetMidiOut(s.ProductName, out var midiOut))
                            {
                                try
                                {
                                    var ccEvt = new ControlChangeEvent(0, 1, (MidiController)cc, intVal);
                                    midiOut.Send(ccEvt.GetAsShortMessage());
                                }
                                catch (Exception e)
                                {
                                    Log.Warning($"Failed to send CC for {overrideKey}: {e.Message}");
                                }
                            }
                        }
                    }
                }

                ImGui.PopID();
            }
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// Draws the APC40 encoder LED ring around the knob center using arcs.
    /// Implements the exact LED segment patterns from the APC40 Communications Protocol.
    /// ringMode: 0=Off, 1=Single, 2=Volume, 3=Pan
    /// </summary>
    private static void DrawEncoderRing(ImDrawListPtr dl, Vector2 center, float radius,
                                        float value01, int ringMode)
    {
        const int ledCount = 15;
        // The ring arc spans 270° (from -135° to +135°), rotated -90° so 0 is at the top.
        var arcStart = -MathF.PI * 0.75f - MathF.PI * 0.5f; // -225° = 135° (top-left)
        var arcEnd   =  MathF.PI * 0.75f - MathF.PI * 0.5f; //  45° (top-right)
        var arcSpan  = arcEnd - arcStart;
        var segAngle = arcSpan / ledCount;
        var gap      = segAngle * 0.15f; // small gap between LED segments

        var intVal = (int)MathF.Round(ClampF(value01, 0f, 1f) * 127f);

        // Compute which LEDs are ON based on ring mode (bool array, 15 LEDs left-to-right)
        Span<bool> leds = stackalloc bool[ledCount];
        switch (ringMode)
        {
            case 0: // Off - no LEDs
                break;
            case 1: // Single - one or two LEDs lit at position
                ComputeSingleRing(intVal, leds);
                break;
            case 2: // Volume - fill from left
                ComputeVolumeRing(intVal, leds);
                break;
            case 3: // Pan - from center outward
                ComputePanRing(intVal, leds);
                break;
            default: // treat 4-127 as Single per protocol
                ComputeSingleRing(intVal, leds);
                break;
        }

        var onColor  = ImGui.GetColorU32(new Vector4(0.2f, 0.9f, 0.3f, 1f)); // green LED
        var offColor = ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.18f, 0.6f)); // dim off

        for (var i = 0; i < ledCount; i++)
        {
            var a0 = arcStart + i * segAngle + gap * 0.5f;
            var a1 = arcStart + (i + 1) * segAngle - gap * 0.5f;
            var col = leds[i] ? onColor : offColor;
            dl.PathArcTo(center, radius, a0, a1, 4);
            dl.PathStroke(col, ImDrawFlags.None, 2.5f);
        }
    }

    /// <summary>Single mode: one or two adjacent LEDs per the APC40 PDF table A.</summary>
    private static void ComputeSingleRing(int v, Span<bool> leds)
    {
        // Exact breakpoints from the APC40 protocol PDF (29 ranges for 15 LEDs)
        // Each entry: (min, max, led0, led1) where led1=-1 means single LED
        ReadOnlySpan<int> table = stackalloc int[]
        {
        //  min, max, led0, led1
            0,   3,   0, -1,
            4,   8,   0,  1,
            9,  12,   1, -1,
           13,  17,   1,  2,
           18,  21,   2, -1,
           22,  25,   2,  3,
           26,  30,   3, -1,
           31,  34,   3,  4,
           35,  38,   4, -1,
           39,  43,   4,  5,
           44,  47,   5, -1,
           48,  52,   5,  6,
           53,  56,   6, -1,
           57,  60,   6,  7,
           61,  65,   7, -1,
           66,  69,   7,  8,
           70,  73,   8, -1,
           74,  78,   8,  9,
           79,  82,   9, -1,
           83,  87,   9, 10,
           88,  91,  10, -1,
           92,  95,  10, 11,
           96, 100,  11, -1,
          101, 104,  11, 12,
          105, 108,  12, -1,
          109, 113,  12, 13,
          114, 117,  13, -1,
          118, 122,  13, 14,
          123, 127,  14, -1,
        };
        for (var i = 0; i < table.Length; i += 4)
        {
            if (v >= table[i] && v <= table[i + 1])
            {
                leds[table[i + 2]] = true;
                if (table[i + 3] >= 0) leds[table[i + 3]] = true;
                return;
            }
        }
    }

    /// <summary>Volume mode: fill from left per the APC40 PDF table B.</summary>
    private static void ComputeVolumeRing(int v, Span<bool> leds)
    {
        // 16 ranges: 0=none, 1-9=1 LED, ... 127=all 15
        int litCount;
        if (v == 0) litCount = 0;
        else if (v <= 9) litCount = 1;
        else if (v <= 18) litCount = 2;
        else if (v <= 27) litCount = 3;
        else if (v <= 36) litCount = 4;
        else if (v <= 45) litCount = 5;
        else if (v <= 54) litCount = 6;
        else if (v <= 63) litCount = 7;
        else if (v <= 71) litCount = 8;
        else if (v <= 80) litCount = 9;
        else if (v <= 89) litCount = 10;
        else if (v <= 98) litCount = 11;
        else if (v <= 107) litCount = 12;
        else if (v <= 116) litCount = 13;
        else if (v <= 126) litCount = 14;
        else litCount = 15;

        for (var i = 0; i < litCount && i < leds.Length; i++)
            leds[i] = true;
    }

    /// <summary>Pan mode: center-outward per the APC40 PDF table C.</summary>
    private static void ComputePanRing(int v, Span<bool> leds)
    {
        // Center LED = index 7 (0-based). Pan spreads outward from center.
        // Left of center: value 0..62 fills LEDs 0..7 (center always lit when <=64)
        // Right of center: value 65..127 fills LEDs 7..14
        // Center (63-64): only LED 7
        if (v <= 8)       { for (var i = 0; i <= 7; i++) leds[i] = true; } // 8 LEDs (0..7)
        else if (v <= 17) { for (var i = 1; i <= 7; i++) leds[i] = true; }
        else if (v <= 26) { for (var i = 2; i <= 7; i++) leds[i] = true; }
        else if (v <= 35) { for (var i = 3; i <= 7; i++) leds[i] = true; }
        else if (v <= 44) { for (var i = 4; i <= 7; i++) leds[i] = true; }
        else if (v <= 53) { for (var i = 5; i <= 7; i++) leds[i] = true; }
        else if (v <= 62) { leds[6] = true; leds[7] = true; }
        else if (v <= 64) { leds[7] = true; }
        else if (v <= 73) { leds[7] = true; leds[8] = true; }
        else if (v <= 82) { for (var i = 7; i <= 9;  i++) leds[i] = true; }
        else if (v <= 91) { for (var i = 7; i <= 10; i++) leds[i] = true; }
        else if (v <= 100){ for (var i = 7; i <= 11; i++) leds[i] = true; }
        else if (v <= 109){ for (var i = 7; i <= 12; i++) leds[i] = true; }
        else if (v <= 118){ for (var i = 7; i <= 13; i++) leds[i] = true; }
        else              { for (var i = 7; i <= 14; i++) leds[i] = true; }
    }

    /// <summary>
    /// Sends a ring type CC to the hardware for a single knob.
    /// Track knobs: CC 0x38+knobIndex on channel 1 (all share channel 1 per protocol)
    /// Device knobs: CC 0x18+knobIndex on channel 1 (channel selects track; we target Track 1)
    /// The protocol's channel column for device knobs means "which track's device knobs",
    /// not "which knob". All 8 device knobs in a single view are on the same channel.
    /// </summary>
    private static void SendKnobRingTypeToHardware(MidiDeviceStatus s, string idPrefix, int knobIndex, int ringType)
    {
        if (!MidiConnectionManager.TryGetMidiOut(s.ProductName, out var midiOut))
            return;
        if (knobIndex < 0 || knobIndex > 7)
            return;

        var isDevice = idPrefix.Contains("device", StringComparison.OrdinalIgnoreCase);
        var ctrlBase = isDevice ? 0x18 : 0x38;
        var controlId = ctrlBase + knobIndex;

        try
        {
            var cc = new ControlChangeEvent(0, 1, (MidiController)controlId, ringType);
            midiOut.Send(cc.GetAsShortMessage());
        }
        catch { /* ignore */ }
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
    internal static void DrawCrossfader(string id, float scale, float targetWidthPx,
                                        ref float value,
                                        ref bool  isDragging,
                                        ref float dragStartX,
                                        ref float dragStartVal,
                                        MidiDeviceStatus s,
                                        int cc)
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














