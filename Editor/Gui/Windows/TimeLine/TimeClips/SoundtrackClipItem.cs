#nullable enable
using T3.Core.Utils;
using ImGuiNET;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Audio;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Animation;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Windows.TimeLine.TimeClips;

/// <summary>
/// Draws and handles interaction for SoundtrackClip timeline items.
/// Separated from <see cref="TimeClipItem"/> because SoundtrackClip has distinct
/// audio-specific rendering (waveform background, volume display) and will gain
/// further audio-specific features (inline volume automation, waveform zoom, etc.).
/// </summary>
internal static class SoundtrackClipItem
{
    /// <summary>
    /// Returns true if the given <see cref="TimeClip"/> belongs to a SoundtrackClip operator symbol.
    /// Used by <see cref="LayersArea"/> to dispatch to this class instead of <see cref="TimeClipItem"/>.
    /// </summary>
    internal static bool IsSoundtrackClip(TimeClip timeClip, SymbolUi compositionSymbolUi)
    {
        if (!compositionSymbolUi.ChildUis.TryGetValue(timeClip.Id, out var childUi))
            return false;

        var symbol = childUi.SymbolChild?.Symbol;
        if (symbol == null)
            return false;

        return string.Equals(symbol.Name, "SoundtrackClip", StringComparison.Ordinal)
               && string.Equals(symbol.Namespace, "Lib.io.audio", StringComparison.Ordinal);
    }

    internal static void DrawClip(TimeClip timeClip, ref TimeClipItem.ClipDrawingAttributes attr)
    {
        var xStartTime = attr.LayerContext.TimeCanvas.TransformX(timeClip.TimeRange.Start) + 1;
        var xEndTime = attr.LayerContext.TimeCanvas.TransformX(timeClip.TimeRange.End) + 1;
        var position = new Vector2(xStartTime,
                                   attr.LayerContext.TimeCanvas.TransformY(timeClip.LayerIndex) + 1);

        var clipWidth = xEndTime - xStartTime;
        var showSizeHandles = clipWidth > 4 * HandleWidth;
        var bodyWidth = showSizeHandles
                            ? (clipWidth - 2 * HandleWidth)
                            : clipWidth;

        var bodySize = new Vector2(bodyWidth, LayersArea.LayerHeight - 2);
        var clipSize = new Vector2(clipWidth, LayersArea.LayerHeight - 2);

        var symbolChildUi = attr.CompositionSymbolUi.ChildUis[timeClip.Id];

        ImGui.PushID(symbolChildUi.Id.GetHashCode());

        var isSelected = attr.LayerContext.ClipSelection.SelectedClipsIds.Contains(timeClip.Id);
        var itemRectMax = position + clipSize - new Vector2(1, 0);

        var rounding = 4.5f;

        var isConnected = attr.CompositionSymbolUi.Symbol.Connections.Any(c => c.SourceParentOrChildId == timeClip.Id);
        var isWithinPlaybackTime = timeClip.TimeRange.Contains(attr.LayerContext.TimeCanvas.Playback.TimeInBars);
        var isDisabled = symbolChildUi.SymbolChild.IsDisabled;
        var fadeIfInActive = (isConnected && isWithinPlaybackTime) ? 1 : 0.4f;
        var fadeIfNotConnected = isConnected ? 1f : 0.2f;
        var fadeIfDisabled = isDisabled ? 0.3f : 1f;
        var combinedFade = fadeIfNotConnected * fadeIfInActive * fadeIfDisabled;

        var sourceDuration = Math.Abs(timeClip.SourceRange.Duration);
        var visibleDuration = Math.Abs(timeClip.TimeRange.Duration);
        var isTimeStretched = sourceDuration > 0.0001f && Math.Abs(visibleDuration - sourceDuration) > 0.001f;
        var stretchPercent = isTimeStretched
                                 ? sourceDuration / visibleDuration * 100f
                                 : 100f;

        // --- Waveform background ---
        var drewWaveform = TryDrawWaveformBackground(symbolChildUi.SymbolChild,
                                                     attr.CompositionOp,
                                                     attr.DrawList,
                                                     position,
                                                     itemRectMax,
                                                     combinedFade);

        if (!drewWaveform)
        {
            // Fallback: solid audio-themed background
            attr.DrawList.AddRectFilled(position, itemRectMax, AudioClipColor.Fade(0.4f * combinedFade), rounding);
        }
        else
        {
            // Subtle tint over waveform
            attr.DrawList.AddRectFilled(position, itemRectMax, AudioClipColor.Fade(0.12f * combinedFade), rounding);
        }

        // Selection outline
        if (isSelected)
            attr.DrawList.AddRect(position, itemRectMax, UiColors.Selection, rounding);

        // Disabled indicator (diagonal cross lines)
        if (isDisabled)
        {
            DrawUtils.DrawOverlayLine(attr.DrawList, combinedFade, Vector2.Zero, Vector2.One, position, itemRectMax);
            DrawUtils.DrawOverlayLine(attr.DrawList, combinedFade, new Vector2(1, 0), new Vector2(0, 1), position, itemRectMax);
        }

        // --- Audio icon indicator (small speaker glyph on left) ---
        // Resolve audio file path once and conditionally draw the icon if an audio file is present
        var hasAudioFile = TryGetSoundtrackAudioFilePath(symbolChildUi.SymbolChild, out var audioFilePathForIcon);
        var iconDrawn = false;
        var audioIconPos = (position + new Vector2(4, 1)).Floor();
        if (hasAudioFile && clipWidth > 30 && LayersArea.LayerHeight > Fonts.FontSmall.FontSize)
        {
            // Use the shared icon atlas (same icon as in Asset Library) for consistent visuals
            Icons.DrawIconAtScreenPosition(Icon.FileAudio, audioIconPos, attr.DrawList, AudioIconColor.Fade(combinedFade));
            iconDrawn = true;
        }

        if (isTimeStretched && iconDrawn && clipWidth > 30)
        {
            // Draw stretch glyph at the bottom-left of the clip for clearer alignment.
            var stretchIconSize = 14f;
            var stretchIconPos = new Vector2(position.X + 4, itemRectMax.Y - stretchIconSize - 2).Floor();

            // Add a subtle dark backing to improve icon contrast over waveform peaks.
            attr.DrawList.AddRectFilled(stretchIconPos - new Vector2(1, 1),
                                        stretchIconPos + new Vector2(stretchIconSize + 1, stretchIconSize + 1),
                                        UiColors.BackgroundFull.Fade(0.45f * combinedFade),
                                        3f);

            Icons.DrawIconAtScreenPosition(Icon.Scale,
                                           stretchIconPos,
                                           new Vector2(stretchIconSize, stretchIconSize),
                                           attr.DrawList,
                                           UiColors.Selection.Fade(0.95f * combinedFade));
        }

        // --- Label ---
        if (LayersArea.LayerHeight > Fonts.FontSmall.FontSize)
        {
            var displayName = !string.IsNullOrEmpty(audioFilePathForIcon)
                                  ? System.IO.Path.GetFileNameWithoutExtension(audioFilePathForIcon)
                                  : symbolChildUi.SymbolChild.ReadableName;

            ImGui.PushFont(Fonts.FontSmall);
            var labelSize = ImGui.CalcTextSize(displayName);
            // Pixel offset: leave more space if icon was drawn, otherwise small padding
            var labelOffset = iconDrawn ? (4f + Icons.FontSize) : 4f;
            var labelMax = itemRectMax - new Vector2(3, 0);
            var needsClipping = (labelSize.X + labelOffset) > clipSize.X;

            if (needsClipping)
                ImGui.PushClipRect(position, labelMax, true);

            attr.DrawList.AddText(position + new Vector2(labelOffset, 1),
                                  isSelected ? UiColors.Selection : AudioLabelColor.Fade(combinedFade),
                                  displayName);

            if (needsClipping)
                ImGui.PopClipRect();

            ImGui.PopFont();
        }

        if (isTimeStretched && LayersArea.LayerHeight > Fonts.FontSmall.FontSize)
        {
            ImGui.PushFont(Fonts.FontSmall);
            var stretchLabel = $"{stretchPercent:0.#}%";
            var stretchLabelSize = ImGui.CalcTextSize(stretchLabel);
            var stretchLabelPos = (itemRectMax - stretchLabelSize - new Vector2(3, 2)).Floor();
            if (stretchLabelPos.X > position.X + 2 && stretchLabelPos.Y > position.Y)
            {
                var textColor = isSelected ? UiColors.Selection : AudioLabelColor.Fade(combinedFade);
                // Shadow pass for readability on bright waveforms.
                attr.DrawList.AddText(stretchLabelPos + new Vector2(1, 1),
                                      UiColors.BackgroundFull.Fade(0.8f * combinedFade),
                                      stretchLabel);
                attr.DrawList.AddText(stretchLabelPos,
                                      textColor,
                                      stretchLabel);
            }
            ImGui.PopFont();
        }


        // --- Interaction and dragging (reuses TimeClipItem patterns) ---
        ImGui.SetCursorScreenPos(showSizeHandles ? (position + _handleOffset) : position);
        var wasClickedDown = ImGui.InvisibleButton("body", bodySize);

        if (ImGui.IsItemHovered())
        {
            DrawSoundtrackTooltip(symbolChildUi, timeClip, isConnected);
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(0))
        {
            if (Structure.TryGetUiAndInstanceInComposition(timeClip.Id, attr.CompositionOp, out _, out var instance))
            {
                if (instance.Symbol.Children.Count > 0)
                    attr.LayerContext.RequestChildComposition(instance.SymbolChildId);
            }
        }

        if (ImGui.IsItemHovered())
        {
            FrameStats.AddHoveredId(symbolChildUi.Id);
        }

        var notClickingOrDragging = !ImGui.IsItemActive() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left);
        if (notClickingOrDragging && attr.MoveClipsCommand != null)
        {
            attr.LayerContext.TimeCanvas.CompleteDragCommand();
        }

        if (wasClickedDown)
        {
            FitViewToSelectionHandling.FitViewToSelection();
        }

        HandleDragging(attr, timeClip, isSelected, wasClickedDown, HandleDragMode.Body);

        // --- Resize handles ---
        var handleSize = showSizeHandles ? new Vector2(HandleWidth, LayersArea.LayerHeight) : Vector2.One;

        ImGui.SetCursorScreenPos(position);
        var aHandleClicked = ImGui.InvisibleButton("startHandle", handleSize);
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(2, 3),
                                        ImGui.GetItemRectMax() - new Vector2(1, 4),
                                        UiColors.ForegroundFull.Fade(0.3f), 5);
        }

        HandleDragging(attr, timeClip, isSelected, false, HandleDragMode.Start);

        ImGui.SetCursorScreenPos(position + new Vector2(bodyWidth + HandleWidth, 0));
        aHandleClicked |= ImGui.InvisibleButton("endHandle", handleSize);
        if (ImGui.IsItemHovered() || ImGui.IsItemActive())
        {
            attr.DrawList.AddRectFilled(ImGui.GetItemRectMin() + new Vector2(0, 3),
                                        ImGui.GetItemRectMax() - new Vector2(3, 4),
                                        UiColors.ForegroundFull.Fade(0.3f), 5);
        }

        HandleDragging(attr, timeClip, isSelected, false, HandleDragMode.End);

        if (aHandleClicked)
        {
            attr.LayerContext.TimeCanvas.CompleteDragCommand();

            if (attr.MoveClipsCommand != null)
            {
                attr.MoveClipsCommand.StoreCurrentValues();
                UndoRedoStack.Add(attr.MoveClipsCommand);
                attr.MoveClipsCommand = null;
            }
        }

        ImGui.PopID();
    }

    #region Waveform Background

    private static bool TryDrawWaveformBackground(Symbol.Child symbolChild,
                                                  Instance compositionOp,
                                                  ImDrawListPtr drawList,
                                                  Vector2 min,
                                                  Vector2 max,
                                                  float fade)
    {
        if (!TryGetSoundtrackAudioFilePath(symbolChild, out var audioFilePath))
            return false;

        var clipDefinition = new SoundtrackClipDefinition
                             {
                                 FilePath = audioFilePath,
                                 Id = Guid.NewGuid(),
                             };

        var handle = new AudioClipResourceHandle(clipDefinition, compositionOp);
        if (!AudioWaveformTextureCache.TryGetShaderResourceView(handle, out var srv) || srv is null)
            return false;

        drawList.AddImage((IntPtr)srv, min, max, Vector2.Zero, Vector2.One, UiColors.ForegroundFull.Fade(fade));
        return true;
    }

    #endregion

    #region Audio File / Volume Helpers

    private static bool TryGetSoundtrackAudioFilePath(Symbol.Child symbolChild, out string filePath)
    {
        filePath = string.Empty;

        if (symbolChild.Inputs.TryGetValue(AudioFileInputId, out var inputById)
            && inputById.Value is InputValue<string> typedById
            && !string.IsNullOrWhiteSpace(typedById.Value))
        {
            filePath = typedById.Value;
            return true;
        }

        foreach (var childInput in symbolChild.Inputs.Values)
        {
            if (!string.Equals(childInput.Name, "AudioFile", StringComparison.Ordinal))
                continue;

            if (childInput.Value is InputValue<string> typedByName && !string.IsNullOrWhiteSpace(typedByName.Value))
            {
                filePath = typedByName.Value;
                return true;
            }

            break;
        }

        return false;
    }

    private static float TryGetSoundtrackVolume(Symbol.Child symbolChild)
    {
        if (symbolChild.Inputs.TryGetValue(VolumeInputId, out var volumeInput)
            && volumeInput.Value is InputValue<float> typedVolume)
        {
            return typedVolume.Value;
        }

        foreach (var childInput in symbolChild.Inputs.Values)
        {
            if (!string.Equals(childInput.Name, "Volume", StringComparison.Ordinal))
                continue;

            if (childInput.Value is InputValue<float> typedByName)
                return typedByName.Value;

            break;
        }

        return 1f;
    }

    private static bool TryGetSoundtrackMute(Symbol.Child symbolChild)
    {
        if (symbolChild.Inputs.TryGetValue(MuteInputId, out var muteInput)
            && muteInput.Value is InputValue<bool> typedMute)
        {
            return typedMute.Value;
        }

        return false;
    }

    #endregion

    #region Tooltip

    private static void DrawSoundtrackTooltip(SymbolUi.Child symbolChildUi, TimeClip timeClip, bool isConnected)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
        ImGui.BeginTooltip();
        {
            ImGui.PushFont(Fonts.FontSmall);

            // Title
            ImGui.TextUnformatted(symbolChildUi.SymbolChild.ReadableName);

            if (!isConnected)
            {
                ImGui.TextUnformatted("(Not connected?)");
            }

            if (symbolChildUi.SymbolChild.IsDisabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextUnformatted("(DISABLED)");
                ImGui.PopStyleColor();
            }

            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

            // Audio file
            if (TryGetSoundtrackAudioFilePath(symbolChildUi.SymbolChild, out var audioPath))
            {
                ImGui.TextUnformatted($"Audio: {System.IO.Path.GetFileName(audioPath)}");
            }

            // Volume / Mute
            var volume = TryGetSoundtrackVolume(symbolChildUi.SymbolChild);
            var muted = TryGetSoundtrackMute(symbolChildUi.SymbolChild);
            var volumeLabel = muted ? "Muted" : $"Volume: {volume * 100:0}%";
            ImGui.TextUnformatted(volumeLabel);

            // Time range
            ImGui.TextUnformatted($"Range: {timeClip.TimeRange.Start:0.00} \u2013 {timeClip.TimeRange.End:0.00}");

            var sourceDuration = Math.Abs(timeClip.SourceRange.Duration);
            var visibleDuration = Math.Abs(timeClip.TimeRange.Duration);
            var isTimeStretched = sourceDuration > 0.0001f && Math.Abs(visibleDuration - sourceDuration) > 0.001f;
            if (isTimeStretched)
            {
                var stretchPercent = sourceDuration / visibleDuration * 100f;
                ImGui.TextUnformatted($"Source: {timeClip.SourceRange.Start:0.00} \u2013 {timeClip.SourceRange.End:0.00}");
                ImGui.TextUnformatted($"Playback speed from stretch: {stretchPercent:0.#}%");
            }

            ImGui.PopStyleColor();
            ImGui.PopFont();
        }
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    #endregion

    #region Dragging (mirrors TimeClipItem logic)

    private enum HandleDragMode
    {
        Body = 0,
        Start,
        End,
    }

    private static void HandleDragging(TimeClipItem.ClipDrawingAttributes attr, TimeClip timeClip, bool isSelected, bool _, HandleDragMode mode)
    {
        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(mode == HandleDragMode.Body
                                     ? ImGuiMouseCursor.Hand
                                     : ImGuiMouseCursor.ResizeEW);
        }

        var isDeactivated = ImGui.IsItemDeactivated();
        var isActive = ImGui.IsItemActive();
        if (!isActive && !isDeactivated)
            return;

        var wasClickRelease = isDeactivated && ImGui.GetMouseDragDelta().Length() < UserSettings.Config.ClickThreshold;
        if (wasClickRelease)
        {
            if (ImGui.GetIO().KeyCtrl)
            {
                if (isSelected)
                {
                    attr.LayerContext.ClipSelection.Deselect(timeClip);
                }

                return;
            }

            if (!isSelected)
            {
                if (!ImGui.GetIO().KeyShift)
                {
                    attr.LayerContext.TimeCanvas.ClearSelection();
                }

                attr.LayerContext.ClipSelection.Select(timeClip);
            }

            return;
        }

        var mousePos = ImGui.GetIO().MousePos;
        var currentDragTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);

        if (attr.MoveClipsCommand == null)
        {
            if (!isSelected)
            {
                if (ImGui.GetIO().KeyShift)
                {
                    attr.LayerContext.ClipSelection.AddSelection(timeClip);
                }
                else
                {
                    attr.LayerContext.ClipSelection.Select(timeClip);
                }
            }

            _timeWithinDraggedClip = currentDragTime - timeClip.TimeRange.Start;
            _posPosYOnDragStart = mousePos.Y;
            _dragStartTime = currentDragTime;
            _lastAppliedDeltaTime = currentDragTime;
            attr.LayerContext.TimeCanvas.StartDragCommand(attr.CompositionOp.Symbol.Id);
        }

        if (!ImGui.IsMouseDragging(0, UserSettings.Config.ClickThreshold))
            return;

        var allowSnapping = !ImGui.GetIO().KeyShift && !(ImGui.GetIO().KeyAlt && ImGui.GetIO().KeyCtrl);
        switch (mode)
        {
            case HandleDragMode.Body:
                var dy = _posPosYOnDragStart - mousePos.Y;

                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(currentDragTime - _timeWithinDraggedClip,
                                                                                       out var snappedClipStartTime,
                                                                                       attr.LayerContext.TimeCanvas.Scale.X))
                {
                    currentDragTime = (float)snappedClipStartTime + _timeWithinDraggedClip;
                }
                else if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(currentDragTime - _timeWithinDraggedClip + timeClip.TimeRange.Duration,
                                                                                            out var snappedClipEndTime,
                                                                                            attr.LayerContext.TimeCanvas.Scale.X))
                {
                    currentDragTime = (float)snappedClipEndTime + _timeWithinDraggedClip - timeClip.TimeRange.Duration;
                }

                attr.LayerContext.TimeCanvas.UpdateDragCommand(GetIncrement(currentDragTime), dy);
                break;

            case HandleDragMode.Start:
                var newDragStartTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);
                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(newDragStartTime, out var snappedValue3, attr.LayerContext.TimeCanvas.Scale.X))
                {
                    newDragStartTime = (float)snappedValue3;
                }

                attr.LayerContext.TimeCanvas.UpdateDragAtStartPointCommand(newDragStartTime - timeClip.TimeRange.Start, 0);
                break;

            case HandleDragMode.End:
                var newDragTime = attr.LayerContext.TimeCanvas.InverseTransformX(mousePos.X);
                if (allowSnapping && attr.LayerContext.SnapHandler.TryCheckForSnapping(newDragTime, out var snappedValue4, attr.LayerContext.TimeCanvas.Scale.X))
                {
                    newDragTime = (float)snappedValue4;
                }

                attr.LayerContext.TimeCanvas.UpdateDragAtEndPointCommand(newDragTime - timeClip.TimeRange.End, 0);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    private static double GetIncrement(double snappedTotalDelta)
    {
        var dt = snappedTotalDelta - _lastAppliedDeltaTime;
        _lastAppliedDeltaTime = snappedTotalDelta;
        return dt;
    }

    #endregion

    #region Constants and State

    private const float HandleWidth = 7;
    private static readonly Vector2 _handleOffset = new(HandleWidth, 0);
    private static float _timeWithinDraggedClip;
    private static double _dragStartTime;
    private static double _lastAppliedDeltaTime;
    private static float _posPosYOnDragStart;

    // SoundtrackClip input GUIDs (must match operator definition)
    private static readonly Guid AudioFileInputId = new("c4a1d5e2-8f3b-4c6a-9e1d-7b2a5f8c3d4e");
    private static readonly Guid VolumeInputId = new("b8e2f1a9-4d7c-4e5b-a2f3-8c6d1e9a4b7f");
    private static readonly Guid MuteInputId = new("a3f7c1e8-5d2b-4a9f-1c6e-2b8a3f5d7c1e");

    // Distinctive audio-themed colors
    private static readonly Color AudioClipColor = new(0.15f, 0.55f, 0.75f, 1.0f);
    private static readonly Color AudioLabelColor = new(0.75f, 0.9f, 1.0f, 1.0f);
    private static readonly Color AudioIconColor = new(0.5f, 0.8f, 1.0f, 1.0f);
    private static readonly Color AudioVolumeBarColor = new(0.3f, 0.7f, 1.0f, 1.0f);

    #endregion
}

