#nullable enable
using System.Collections.Immutable;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

internal sealed partial class DeleteProjectDialog : ModalDialog
{
    /// <summary>
    /// Renders the main dialog content including dependency analysis and delete/cancel buttons.
    /// </summary>
    /// <param name="project">The editable project currently selected for deletion.</param>
    internal void Draw(EditableSymbolProject project)
    {
        if (!BeginDialog("Delete Project"))
        {
            EndDialog();
            return;
        }

        var dialogJustOpened = _project == null;
        var projectChanged = _project != null && (dialogJustOpened || _project.CsProjectFile.RootNamespace != project.CsProjectFile.RootNamespace);

        if (dialogJustOpened || projectChanged)
        {
            _project = project;
            _cachedDependencies = null;
            _cachedAssets = null;
            _cachedOperatorFiles = null;
            _allowDeletion = false;
            _lastAnalysis = null;
        }

        LocalProjectInfo? info = null;

        if (_lastAnalysis == null)
        {
            if (!TryAnalyzeProject(project, out var analyzedInfo))
            {
                ImGui.Separator();
                ImGui.TextColored(UiColors.TextMuted, "Could not analyze dependencies for this project.");
                _allowDeletion = false;
            }
            else
            {
                _lastAnalysis = analyzedInfo;
            }
        }

        if (_lastAnalysis != null)
        {
            info = new LocalProjectInfo(_lastAnalysis);
            DrawAnalysisUi(project, info);
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace();

        if (_allowDeletion)
        {
            var buttonLabel = info is { DependingSymbols.IsEmpty: false } ? "Force delete" : "Delete";

            if (ImGui.Button(buttonLabel))
            {
                if (info == null)
                {
                    BlockingWindow.Instance.ShowMessageBox("Internal error: missing project analysis", "Could not delete project");
                }
                else
                {
                    var success = info is { DependingSymbols.IsEmpty: false }
                                      ? DeleteProject(project, info.DependingSymbols.ToHashSet(), out var reason)
                                      : DeleteProject(project, null, out reason);

                    if (!success)
                    {
                        BlockingWindow.Instance.ShowMessageBox(reason, "Could not delete project");
                    }
                    else
                    {
                        Close();
                    }
                }
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Cancel"))
        {
            Close();
        }

        EndDialogContent();
        EndDialog();
    }

    /// <summary>
    /// Lightweight container for project dependency and asset data used by the dialog.
    /// </summary>
    private sealed class LocalProjectInfo(ProjectAnalysisResult source)
    {
        public ImmutableHashSet<Guid> DependingSymbols { get; } = ImmutableHashSet.CreateRange(source.DependingSymbolIds);
        public int DependingProjectCount { get; } = source.DependingProjectCount;
        public int AssetCount { get; } = source.AssetCount;
        public int OperatorCount { get; } = source.OperatorCount;
    }

    private void Close()
    {
        ImGui.CloseCurrentPopup();
        _project = null;
        _allowDeletion = false;
        _cachedDependencies = null;
        _cachedAssets = null;
        _cachedOperatorFiles = null;
        _lastAnalysis = null;
    }

    private EditableSymbolProject? _project;
    private bool _allowDeletion;
    private List<(string ProjectName, List<string> Symbols)>? _cachedDependencies;
    private List<string>? _cachedAssets;
    private List<string>? _cachedOperatorFiles;
    private ProjectAnalysisResult? _lastAnalysis;
}
