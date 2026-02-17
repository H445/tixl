#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
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
    /// Renders a two-column scrollable list showing operators on the left and assets on the right.
    /// </summary>
    private void DrawOperatorsAndAssetsList(EditableSymbolProject project)
    {
        var fontSize = ImGui.GetFontSize();
        const int maxVisibleItems = 8;
        var itemHeight = fontSize + 4.0f;
        var scrollHeight = itemHeight * maxVisibleItems;

        if (ImGui.BeginChild("OperatorsAndAssets", new Vector2(0, scrollHeight), true))
        {
            var contentWidth = ImGui.GetContentRegionAvail().X;
            var columnWidth = (contentWidth - 16) / 2;

            // Two columns side by side
            ImGui.Columns(2, "##OperatorsAssetsColumns", true);
            ImGui.SetColumnWidth(0, columnWidth);

            // Left column: Operators
            DrawColumnHeader("Operators");
            DrawOperatorsList();

            ImGui.NextColumn();

            // Right column: Assets
            DrawColumnHeader("Assets");
            DrawAssetsList(project);

            ImGui.Columns(1);
        }

        ImGui.EndChild();
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

        foreach (var opName in operatorNames)
        {
            CustomComponents.StylizedText("  " + opName, Fonts.FontSmall, UiColors.Text);
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

        foreach (var asset in displayed)
        {
            var relativePath = asset.Replace(project.Folder + "\\", "").Replace(project.Folder + "/", "");
            CustomComponents.StylizedText("  " + relativePath, Fonts.FontSmall, UiColors.Text);
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
}
