#nullable enable
using ImGuiNET;
using System;
using System.Collections.Generic;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

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

        // Show dependencies warning first (like DeleteSymbolDialog)
        if (!info.DependingSymbols.IsEmpty)
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
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
            ImGui.TextWrapped($"Project [{projectName}] is not used by other projects and can be safely deleted.");
            ImGui.PopStyleColor();
        }

        // Show what will be deleted - operators and assets in two columns
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.TextWrapped($"This will permanently delete {info.OperatorCount} operators and {info.AssetCount} assets:");
        ImGui.PopStyleColor();

        DrawOperatorsAndAssetsList(project);
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
        DrawColumnHeader("Operators");
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
        DrawColumnHeader("Assets");
        ImGui.PushID("AssetsListChild");
        if (ImGui.BeginChild("AssetsList", new Vector2(columnWidth, listHeight), true, ImGuiWindowFlags.None))
        {
            DrawAssetsList(project);
        }
        ImGui.EndChild();
        ImGui.PopID();
        ImGui.EndGroup();
    }

    private static void DrawColumnHeader(string title)
    {
        var avail = ImGui.GetContentRegionAvail();
        var cursorPos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var rectMax = new Vector2(cursorPos.X + avail.X, cursorPos.Y + Fonts.FontSmall.FontSize + 4);
        drawList.AddRectFilled(cursorPos, rectMax, UiColors.BackgroundFull.Fade(0.3f), 0.0f);

        CustomComponents.StylizedText(title, Fonts.FontSmall, UiColors.Text);
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

        // Update the selection helper with visible items
        _operatorSelection.SetVisibleItems(operatorNames);

        for (var i = 0; i < operatorNames.Count; i++)
        {
            var opName = operatorNames[i];
            var label = "  " + opName;
            var isSelected = _operatorSelection.IsSelected(opName);

            ImGui.PushID($"op_{i}");

            var pressed = ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.None, new Vector2(0, 0));

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
}
