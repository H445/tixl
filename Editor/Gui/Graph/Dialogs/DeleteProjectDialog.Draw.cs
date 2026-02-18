#nullable enable
using ImGuiNET;
using System;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Dialogs;

internal sealed partial class DeleteProjectDialog
{
    /// <summary>
    /// Renders the main body of the delete confirmation dialog after project dependency analysis
    /// has completed successfully.
    /// </summary>
    private void DrawAnalysisUi(EditableSymbolProject project, LocalProjectInfo info)
    {
        var projectName = project.DisplayName;
        _allowDeletion = !project.IsReadOnly;

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.TextWrapped(_allowDeletion
                              ? $"Are you sure you want to delete project [{projectName}]?"
                              : $"Can not delete [{projectName}]");
        ImGui.PopStyleColor();

        if (project.IsReadOnly)
        {
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextColored(UiColors.StatusAttention, "This project is read-only and can not be deleted.");
            ImGui.PopFont();
            return;
        }

        // Show what will be deleted - operators and assets in two columns
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.TextWrapped($"This will permanently delete {info.OperatorCount} operators and {info.AssetCount} assets:");
        ImGui.PopStyleColor();

        DrawOperatorsAndAssetsList(project);

        // Show dependencies warning first (like DeleteSymbolDialog)
        if (info.DependingSymbols.IsEmpty)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
            ImGui.TextWrapped($"Project [{projectName}] is not used by other projects and can be safely deleted.");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            ImGui.TextWrapped($"[{projectName}] is used by [{info.DependingSymbols.Count}] symbols in [{info.DependingProjectCount}] other projects:");
            ImGui.PopStyleColor();

            ListDependingSymbols();
            
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            ImGui.TextWrapped("Clicking Force delete will automatically disconnect/clean all usages. " +
                              "This may completely break these projects/symbols, and can *NOT* be undone.");
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Renders a two-column layout showing operators on the left and assets on the right.
    /// Each column is individually scrollable.
    /// </summary>
    private void DrawOperatorsAndAssetsList(EditableSymbolProject project)
    {
        var fontSize = ImGui.GetFontSize();
        const int maxVisibleItems = 8;
        var itemHeight = fontSize + 4.0f;
        var scrollHeight = itemHeight * maxVisibleItems;

        var contentWidth = ImGui.GetContentRegionAvail().X;
        var columnWidth = (contentWidth - 8) / 2; // 8 for spacing between columns
        var headerHeight = Fonts.FontSmall.FontSize + 6;
        var listHeight = Math.Max(0, scrollHeight - headerHeight);

        // Left column: Operators
        ImGui.BeginGroup();
        DrawColumnHeader("Operators", columnWidth);
        ImGui.PushID("OperatorsListChild");
        if (ImGui.BeginChild("OperatorsList", new Vector2(columnWidth, listHeight), true, ImGuiWindowFlags.None))
        {
            DrawOperatorsList();
        }
        ImGui.EndChild();
        ImGui.PopID();
        ImGui.EndGroup();

        ImGui.SameLine(0, 8);

        // Right column: Assets
        ImGui.BeginGroup();
        DrawColumnHeader("Assets", columnWidth);
        ImGui.PushID("AssetsListChild");
        if (ImGui.BeginChild("AssetsList", new Vector2(columnWidth, listHeight), true, ImGuiWindowFlags.None))
        {
            DrawAssetsList(project);
        }
        ImGui.EndChild();
        ImGui.PopID();
        ImGui.EndGroup();
    }

    private static void DrawColumnHeader(string title, float width)
    {
        var cursorPos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var headerHeight = Fonts.FontSmall.FontSize + 4;
        var rectMax = new Vector2(cursorPos.X + width, cursorPos.Y + headerHeight);
        drawList.AddRectFilled(cursorPos, rectMax, UiColors.BackgroundFull.Fade(0.7f), 0.0f);

        // Center the text horizontally and vertically within the header rect
        var textSize = ImGui.CalcTextSize(title);
        var textX = cursorPos.X + Math.Max(0, (width - textSize.X) * 0.5f);
        var textY = cursorPos.Y + Math.Max(0, (headerHeight - textSize.Y) * 0.5f);

        ImGui.SetCursorScreenPos(new Vector2(textX, textY));
        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.Text);

        // Ensure subsequent items start below the header
        ImGui.SetCursorScreenPos(new Vector2(cursorPos.X, cursorPos.Y + headerHeight));
    }

    private void DrawOperatorsList()
    {
        if (_cachedOperatorFiles == null || _cachedOperatorFiles.Count == 0)
        {
            CustomComponents.StylizedText("  (none)", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        // Get unique operator names from the operator files
        var operatorNames = _cachedOperatorFiles
                            .Select(p => System.IO.Path.GetFileNameWithoutExtension(p))
                            .Distinct()
                            .OrderBy(n => n)
                            .ToList();

        // Build a lookup of symbols in this project by name for quick access
        var projectNamespace = _project?.CsProjectFile.RootNamespace ?? string.Empty;
        var projectSymbolsByName = EditorSymbolPackage.AllSymbols
                                                      .Where(s => s.SymbolPackage.RootNamespace == projectNamespace)
                                                      .ToDictionary(s => s.Name, s => s);

        // Update the selection helper with visible items
        _operatorSelection.SetVisibleItems(operatorNames);

        // Calculate available width for layout
        var availWidth = ImGui.GetContentRegionAvail().X;

        for (var i = 0; i < operatorNames.Count; i++)
        {
            var opName = operatorNames[i];
            var label = "  " + opName;
            var isSelected = _operatorSelection.IsSelected(opName);

            ImGui.PushID($"op_{i}");

            var pressed = ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.None, new Vector2(0, 0));

            // Draw "used by" badge if symbol analysis is available
            if (projectSymbolsByName.TryGetValue(opName, out var symbol))
            {
                DrawUsedByBadge(symbol, availWidth);
            }

            // Right-click context menu for move
            CustomComponents.ContextMenuForItem(() =>
            {
                if (ImGui.BeginMenu("Move Operator(s) To..."))
                {
                    foreach (var target in EditableSymbolProject.AllProjects)
                    {
                        if (target.CsProjectFile.RootNamespace == _project?.CsProjectFile.RootNamespace)
                            continue;

                        if (ImGui.MenuItem(target.DisplayName))
                        {
                            var toMove = _operatorSelection.GetSelectedOrFallback(opName);
                            MoveOperatorsToProject(target, toMove);
                        }
                    }

                    ImGui.EndMenu();
                }
            }, title: GetContextMenuTitle(opName, _operatorSelection));

            // Use SelectionHelper for click handling
            if (pressed)
            {
                _operatorSelection.HandleClick(opName, i);
            }

            ImGui.PopID();
        }
    }

    private void DrawAssetsList(EditableSymbolProject project)
    {
        if (_cachedAssets == null || _cachedAssets.Count == 0)
        {
            CustomComponents.StylizedText("  (none)", Fonts.FontSmall, UiColors.TextMuted);
            return;
        }

        const int maxToShow = 50;
        var displayed = _cachedAssets.Take(maxToShow).ToList();

        // Update the selection helper with visible items
        _assetSelection.SetVisibleItems(displayed);

        for (var i = 0; i < displayed.Count; i++)
        {
            var asset = displayed[i];
            var relativePath = asset.Replace(project.Folder + "\\", "").Replace(project.Folder + "/", "");
            var label = "  " + relativePath;
            var isSelected = _assetSelection.IsSelected(asset);

            ImGui.PushID($"asset_{i}");

            var pressed = ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.None, new Vector2(0, 0));

            // Right-click context menu for move
            CustomComponents.ContextMenuForItem(() =>
            {
                if (ImGui.BeginMenu("Move Asset(s) To..."))
                {
                    foreach (var target in EditableSymbolProject.AllProjects)
                    {
                        if (target.CsProjectFile.RootNamespace == _project?.CsProjectFile.RootNamespace)
                            continue;

                        if (ImGui.MenuItem(target.DisplayName))
                        {
                            var toMove = _assetSelection.GetSelectedOrFallback(asset);
                            MoveAssetsToProject(target, toMove);
                        }
                    }

                    ImGui.EndMenu();
                }
            }, title: GetContextMenuTitle(relativePath, _assetSelection));

            // Use SelectionHelper for click handling
            if (pressed)
            {
                _assetSelection.HandleClick(asset, i);
            }

            ImGui.PopID();
        }

        if (_cachedAssets.Count > maxToShow)
        {
            CustomComponents.StylizedText($"  ...and {_cachedAssets.Count - maxToShow} more", Fonts.FontSmall, UiColors.TextMuted);
        }
    }

    /// <summary>
    /// Renders a scrollable list of symbol names and their project namespaces for all
    /// symbols that reference the project being deleted.
    /// </summary>
    private void ListDependingSymbols()
    {
        if (_cachedDependencies == null || _cachedDependencies.Count == 0)
            return;

        var fontSize = ImGui.GetFontSize();
        const int maxVisibleItems = 5;
        var itemHeight = fontSize + 4.0f;
        var scrollHeight = itemHeight * maxVisibleItems;

        if (ImGui.BeginChild("DependingSymbolsList", new Vector2(0, scrollHeight), true))
        {
            foreach (var group in _cachedDependencies)
            {
                // Project header with background
                var avail = ImGui.GetContentRegionAvail();
                var cursorPos = ImGui.GetCursorScreenPos();
                var drawList = ImGui.GetWindowDrawList();
                var rectMax = new Vector2(cursorPos.X + avail.X, cursorPos.Y + Fonts.FontSmall.FontSize + 4);
                drawList.AddRectFilled(cursorPos, rectMax, UiColors.BackgroundFull.Fade(0.3f), 0.0f);

                CustomComponents.StylizedText(group.ProjectName, Fonts.FontSmall, UiColors.Text);

                // Symbol names
                foreach (var symbolName in group.Symbols)
                {
                    CustomComponents.StylizedText("  " + symbolName, Fonts.FontSmall, UiColors.Text);
                }
            }
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// Generates context menu title showing the item name with a "+X" counter if multiple items are selected.
    /// </summary>
    private static string GetContextMenuTitle<TKey>(string displayName, UiMultiSelectionHelper<TKey> selection) where TKey : notnull
    {
        var count = selection.Count;
        if (count <= 1)
            return displayName;

        return $"{displayName} ...and {count - 1} more";
    }

    /// <summary>
    /// Draws "used by" badge with icon and tooltip showing which symbols depend on this operator.
    /// </summary>
    private static void DrawUsedByBadge(Symbol symbol, float availWidth)
    {
        if (!SymbolAnalysis.DetailsInitialized)
            return;

        if (!SymbolAnalysis.InformationForSymbolIds.TryGetValue(symbol.Id, out var info))
            return;

        var dependingCount = info.DependingSymbols.Count;
        if (dependingCount == 0)
        {
            // Show "NOT USED" indicator for unused operators
            ImGui.SameLine(availWidth - 60);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            ImGui.TextUnformatted("");
            ImGui.PopStyleColor();
            return;
        }

        // Build tooltip matches here so the local DrawTooltip can reference them
        var allSymbolUis = EditorSymbolPackage.AllSymbolUis;
        var matches = allSymbolUis
                     .Where(s => info.DependingSymbols.Contains(s.Symbol.Id))
                     .OrderBy(s => s.Symbol.Namespace)
                     .ThenBy(s => s.Symbol.Name);

        // Position badge on the right side - use absolute X position like SymbolLibrary
        ImGui.SameLine(availWidth - 25);

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

        // Draw icon
        Icon.Referenced.DrawAtCursor();

        // Tooltip for icon: use same pattern as SymbolLibrary (local DrawTooltip uses BeginTooltip/ListSymbols style)
        void DrawTooltip()
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("used by...");
            FormInputs.AddVerticalSpace();

            // Reuse the same grouping and layout logic as SymbolLibrary.ListSymbols
            var lastGroupName = string.Empty;
            ColumnLayout.StartLayout(25);
            foreach (var required in matches)
            {
                var projectName = required.Symbol.SymbolPackage.RootNamespace;
                if (projectName != lastGroupName)
                {
                    lastGroupName = projectName;
                    FormInputs.AddVerticalSpace(5);
                    ImGui.PushFont(Fonts.FontSmall);
                    ImGui.TextUnformatted(projectName);
                    ImGui.PopFont();
                }

                var hasIssues = required.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                                | required.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix);
                var color = hasIssues ? UiColors.StatusAttention : UiColors.Text;
                ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                ColumnLayout.StartGroupAndWrapIfRequired(1);
                ImGui.TextUnformatted(required.Symbol.Name);
                ColumnLayout.ExtendWidth(ImGui.GetItemRectSize().X);
                ImGui.PopStyleColor();
            }

            ImGui.EndTooltip();
        }

        CustomComponents.TooltipForLastItem(DrawTooltip);

        ImGui.SameLine(0, 2);

        // Draw count
        ImGui.TextUnformatted($"{dependingCount}");

        // Tooltip for count
        CustomComponents.TooltipForLastItem(DrawTooltip);

        ImGui.PopStyleColor();
    }
}
