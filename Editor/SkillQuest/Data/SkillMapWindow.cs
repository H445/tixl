#nullable enable

using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.SkillQuest.Data;

internal static class SkillMapPopup
{
    private static bool _isOpen;

    internal static void ShowNextFrame()
    {
        _isOpen = true;
    }

    private static QuestZone? _activeZone;
    private static QuestTopic? _activeTopic;

    internal static void Draw()
    {
        //var result = ChangeSymbol.SymbolModificationResults.Nothing;

        if (!_isOpen)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1);
        if (ImGui.Begin("Edit skill map", ref _isOpen))
        {
            if (ImGui.IsWindowAppearing())
            {
                InitMap();
            }

            ImGui.BeginChild("LevelList", new Vector2(120 * T3Ui.UiScaleFactor, 0));
            {
                foreach (var zone in SkillMap.Data.Zones)
                {
                    ImGui.PushID(zone.Id.GetHashCode());
                    if (ImGui.Selectable($"{zone.Title}", zone == _activeZone))
                    {
                        _activeZone = zone;
                        _activeTopic = null;
                    }

                    ImGui.Indent(10);

                    for (var index = 0; index < zone.Topics.Count; index++)
                    {
                        var t = zone.Topics[index];
                        ImGui.PushID(index);
                        if (ImGui.Selectable($"{t.Title}", t == _activeTopic))
                        {
                            _activeTopic = t;
                        }

                        ImGui.PopID();
                    }

                    ImGui.Unindent(10);
                    FormInputs.AddVerticalSpace();

                    ImGui.PopID();
                }
            }
            ImGui.EndChild();

            ImGui.SameLine();

            //ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.WindowBackground.Fade(0.8f).Rgba);
            ImGui.BeginChild("Inner", new Vector2(-200, 0), false, ImGuiWindowFlags.NoMove);
            {
                if (ImGui.Button("Update"))
                {
                    InitMap();
                }

                if (ImGui.Button("Save"))
                {
                    SkillMap.Save();
                }

                var dl = ImGui.GetWindowDrawList();
                _canvas.UpdateCanvas(out _);
                // var c = _canvas.TransformPosition(Vector2.Zero);
                // var d = _canvas.TransformDirection(Vector2.One);

                DrawContent();
            }

            ImGui.EndChild();
            //ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.BeginChild("SidePanel", new Vector2(200, 0));
            {
                DrawSidebar();
            }
            ImGui.EndChild();
        }

        ImGui.PopStyleColor();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    private static Guid _draggedTopicId;

    private static void InitMap()
    {
        _gridTopics.Clear();
        foreach (var topic in SkillMap.AllTopics)
        {
            _gridTopics[topic.MapCellHash] = topic;
        }
    }

    private static void DrawSidebar()
    {
        if (_activeTopic == null)
            return;

        var autoFocus = false;
        if (_focusTopicNameInput)
        {
            autoFocus = true;
            _focusTopicNameInput = false;
        }

        ImGui.PushID(_activeTopic.Id.GetHashCode());
        FormInputs.AddStringInput("##Topic", ref _activeTopic.Title, autoFocus: autoFocus);

        if (FormInputs.AddDropdown<Type>(ref _activeTopic.Type,
                [
                    typeof(Texture2D),
                    typeof(float),
                    typeof(Command),
                    typeof(string),
                    typeof(BufferWithViews),
                ], "##Type", x => x.Name))
        {
        }

        ImGui.PopID();
    }

    private static void DrawItem(ImDrawListPtr dl, float rOnScreen, int x, int y)
    {
        var cellHash = y * 16384 + x;

        var scale = 136;
        var offSetX = (y % 2) * 0.5f;

        var mousePos = ImGui.GetMousePos();
        var radius = rOnScreen * 0.56f * scale;
        var posOnScreen = _canvas.TransformPosition(new Vector2(x + offSetX, y * 0.87f) * scale);
        var isMouseInside = Vector2.Distance(posOnScreen, mousePos) < radius * 0.7f && !ImGui.IsMouseDown(ImGuiMouseButton.Right);
        var isCellHovered = ImGui.IsWindowHovered() && !ImGui.IsMouseDown(ImGuiMouseButton.Right) && isMouseInside;

        _gridTopics.TryGetValue(cellHash, out var topic);

        // Handle empty cell
        if (topic == null)
        {
            if (!isCellHovered)
                return;

            var hoverColor = topic == null ? UiColors.ForegroundFull.Fade(0.05f) : Color.Orange;
            dl.AddVerticalHexagon(posOnScreen, radius, hoverColor.Fade(isMouseInside ? 0.3f : 0.15f));

            
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                var newTopic = new QuestTopic
                                   {
                                       Id = Guid.NewGuid(),
                                       MapCoordinate = new Vector2(x, y),
                                       Title = "New topic" + SkillMap.AllTopics.Count(),
                                       ZoneId = _activeTopic?.ZoneId ?? Guid.Empty,
                                       Type = _activeTopic?.Type ?? typeof(Texture2D),
                                       Status = _activeTopic?.Status ?? QuestTopic.Statuses.Locked,
                                       Requirement = _activeTopic?.Requirement ?? QuestTopic.Requirements.AllInputPaths,
                                   };

                var relevantZone = GetActiveZone();
                relevantZone.Topics.Add(newTopic);
                newTopic.ZoneId = relevantZone.Id;

                _gridTopics[cellHash] = newTopic;

                _activeTopic = newTopic;
                _focusTopicNameInput = true;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _activeTopic = null;
            }
            else if (ImGui.IsMouseDragging(ImGuiMouseButton.Left) && _draggedTopicId != Guid.Empty)
            {
                if (_activeTopic?.Id == _draggedTopicId)
                {
                    _gridTopics.Remove(_activeTopic.MapCellHash);
                    _activeTopic.MapCoordinate = new Vector2(x, y);
                    _gridTopics[_activeTopic.MapCellHash] = _activeTopic;
                }
            }

            return;
        }

        // Existing topic

        var typeColor = TypeUiRegistry.GetTypeOrDefaultColor(topic.Type);
        
        //UiColors.ForegroundFull.Fade(0.05f) : Color.Orange;
        dl.AddVerticalHexagon(posOnScreen, radius, typeColor.Fade(isMouseInside ? 0.3f : 0.15f));

        var isSelected = _activeTopic == topic;
        if (isSelected)
        {
            dl.AddVerticalHexagon(posOnScreen, radius, UiColors.StatusActivated, false);
        }

        if (!string.IsNullOrEmpty(topic.Title))
        {
            var labelAlpha = _canvas.Scale.X.RemapAndClamp(0.3f, 2f, 0, 1);
            if (labelAlpha > 0.01f)
            {
                var labelSize = ImGui.CalcTextSize(topic.Title);
                dl.AddText(posOnScreen - labelSize * 0.5f, UiColors.ForegroundFull.Fade(labelAlpha), topic.Title);
            }
        }

        if (isCellHovered)
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(topic.Title);
            ImGui.EndTooltip();

            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _activeTopic = topic;
                _draggedTopicId = topic.Id;
            }
        }
    }

    private static QuestZone GetActiveZone()
    {
        if (_activeZone != null)
            return _activeZone;

        if (_activeTopic == null)
            return SkillMap.FallbackZone;

        return SkillMap.TryGetZone(_activeTopic.Id, out var zone)
                   ? zone
                   : SkillMap.FallbackZone;
    }

    private static bool _focusTopicNameInput;
    private static Dictionary<int, QuestTopic> _gridTopics = new();

    private static void DrawContent()
    {
        var dl = ImGui.GetWindowDrawList();

        var rOnScreen = _canvas.TransformDirection(Vector2.One).X;
        for (int x = -15; x < 15; x++)
        {
            for (int y = -15; y < 15; y++)
            {
                DrawItem(dl, rOnScreen, x, y);
            }
        }

        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
        }

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _draggedTopicId = Guid.Empty;
        }

        DrawBackgroundGrids(dl);
    }

    private static void DrawBackgroundGrids(ImDrawListPtr drawList)
    {
        var minSize = MathF.Min(10, 10);
        var gridSize = Vector2.One * minSize;
        var maxOpacity = 0.25f;

        var fineGrid = _canvas.Scale.X.RemapAndClamp(0.5f, 2f, 0.0f, maxOpacity);
        if (fineGrid > 0.01f)
        {
            var color = UiColors.CanvasGrid.Fade(fineGrid);
            DrawBackgroundGrid(drawList, gridSize, color);
        }

        var roughGrid = _canvas.Scale.X.RemapAndClamp(0.1f, 2f, 0.0f, maxOpacity);
        if (roughGrid > 0.01f)
        {
            var color = UiColors.CanvasGrid.Fade(roughGrid);
            DrawBackgroundGrid(drawList, gridSize * 5, color);
        }
    }

    private static void DrawBackgroundGrid(ImDrawListPtr drawList, Vector2 gridSize, Color color)
    {
        var window = new ImRect(_canvas.WindowPos, _canvas.WindowPos + _canvas.WindowSize);

        var topLeftOnCanvas = _canvas.InverseTransformPositionFloat(_canvas.WindowPos);
        var alignedTopLeftCanvas = new Vector2((int)(topLeftOnCanvas.X / gridSize.X) * gridSize.X,
                                               (int)(topLeftOnCanvas.Y / gridSize.Y) * gridSize.Y);

        var topLeftOnScreen = _canvas.TransformPosition(alignedTopLeftCanvas);
        var screenGridSize = _canvas.TransformDirection(gridSize);

        var count = new Vector2(window.GetWidth() / screenGridSize.X, window.GetHeight() / screenGridSize.Y);

        for (int ix = 0; ix < 200 && ix <= count.X + 1; ix++)
        {
            var x = (int)(topLeftOnScreen.X + ix * screenGridSize.X);
            drawList.AddRectFilled(new Vector2(x, window.Min.Y),
                                   new Vector2(x + 1, window.Max.Y),
                                   color);
        }

        for (int iy = 0; iy < 200 && iy <= count.Y + 1; iy++)
        {
            var y = (int)(topLeftOnScreen.Y + iy * screenGridSize.Y);
            drawList.AddRectFilled(new Vector2(window.Min.X, y),
                                   new Vector2(window.Max.X, y + 1),
                                   color);
        }
    }

    private static readonly Vector2[] _pointsForHexagon = new Vector2[6];

    private static void AddVerticalHexagon(this ImDrawListPtr dl, Vector2 center, float radius, uint color, bool filled = true)
    {
        var startAngle = -MathF.PI / 2f;

        for (var i = 0; i < 6; i++)
        {
            var angle = startAngle + i * (MathF.PI / 3f); // 60° steps
            _pointsForHexagon[i] = new Vector2(
                                               center.X + MathF.Cos(angle) * radius,
                                               center.Y + MathF.Sin(angle) * radius
                                              );
        }

        if (filled)
        {
            dl.AddConvexPolyFilled(ref _pointsForHexagon[0], _pointsForHexagon.Length, color);
        }
        else
        {
            dl.AddPolyline(ref _pointsForHexagon[0], _pointsForHexagon.Length, color, ImDrawFlags.Closed, 5);
        }
    }

    private static readonly ScalableCanvas _canvas = new();
}