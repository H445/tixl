#nullable enable
using System.IO;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

internal sealed partial class DeleteProjectDialog
{
    /// <summary>
    /// Lightweight result from project analysis.
    /// </summary>
    private sealed class ProjectAnalysisResult
    {
        public IEnumerable<Guid> DependingSymbolIds = new List<Guid>();
        public int DependingProjectCount;
        public int AssetCount;
        public int OperatorCount;
    }

    private bool TryAnalyzeProject(EditableSymbolProject project, out ProjectAnalysisResult info)
    {
        info = new ProjectAnalysisResult();
        try
        {
            var projectRoot = project.CsProjectFile.RootNamespace;

            // Find all symbols belonging to this project
            var projectSymbolIds = EditorSymbolPackage.AllSymbols
                                                      .Where(s => s.SymbolPackage.RootNamespace == projectRoot)
                                                      .Select(s => s.Id)
                                                      .ToHashSet();

            var dependingSymbolIds = new HashSet<Guid>();
            var dependingProjectNames = new HashSet<string>();

            // Find all symbols that reference symbols from this project
            foreach (var container in EditorSymbolPackage.AllSymbols)
            {
                foreach (var child in container.Children.Values)
                {
                    if (projectSymbolIds.Contains(child.Symbol.Id))
                    {
                        dependingSymbolIds.Add(container.Id);
                        dependingProjectNames.Add(container.SymbolPackage.RootNamespace);
                    }
                }
            }

            info.DependingSymbolIds = dependingSymbolIds;
            info.DependingProjectCount = dependingProjectNames.Count;

            // Collect assets
            var assets = CollectProjectAssets(project);

            // Define operator file extensions
            var operatorExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                EditorSymbolPackage.SourceCodeExtension,
                SymbolPackage.SymbolExtension,
                EditorSymbolPackage.SymbolUiExtension
            };

            var excludeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".csproj" };

            // Filter assets (exclude .temp folders, operator files, and project files)
            _cachedAssets = assets
                            .Where(p => !PathContainsTempFolder(p))
                            .Where(p => !operatorExtensions.Contains(Path.GetExtension(p)))
                            .Where(p => !excludeExtensions.Contains(Path.GetExtension(p)))
                            .OrderBy(p => p)
                            .ToList();

            // Collect operator files separately
            _cachedOperatorFiles = assets
                                   .Where(p => !PathContainsTempFolder(p))
                                   .Where(p => operatorExtensions.Contains(Path.GetExtension(p)))
                                   .OrderBy(p => p)
                                   .ToList();

            // Count operators (groups of .cs/.t3/.t3ui with same base name)
            var operatorNames = _cachedOperatorFiles
                                .Select(p => Path.GetFileNameWithoutExtension(p))
                                .Distinct()
                                .ToList();

            info.AssetCount = _cachedAssets.Count;
            info.OperatorCount = operatorNames.Count;

            // Build grouped symbol lists for UI
            var allSymbolUis = EditorSymbolPackage.AllSymbolUis;
            var matches = allSymbolUis
                          .Where(su => dependingSymbolIds.Contains(su.Symbol.Id))
                          .OrderBy(su => su.Symbol.Namespace)
                          .ThenBy(su => su.Symbol.Name)
                          .ToList();

            _cachedDependencies = matches
                                  .GroupBy(su => su.Symbol.SymbolPackage.RootNamespace)
                                  .OrderBy(g => g.Key)
                                  .Select(g => (ProjectName: g.Key, Symbols: g.Select(su => su.Symbol.Name).ToList()))
                                  .ToList();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathContainsTempFolder(string path)
    {
        return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   .Any(segment => segment.StartsWith(".temp", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> CollectProjectAssets(EditableSymbolProject project)
    {
        var results = new List<string>();
        try
        {
            var root = project.Folder;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return results;

            var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "dependencies" };

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file);
                var parts = relative.Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => exclusions.Contains(p)))
                    continue;

                results.Add(file);
            }
        }
        catch
        {
            // ignore and return partial results
        }

        return results.OrderBy(p => p).ToList();
    }

    #region Project Deletion Helpers

    private bool DeleteProject(EditableSymbolProject project, HashSet<Guid>? dependingSymbols, out string reason)
    {
        if (dependingSymbols is { Count: > 0 })
        {
            if (!CleanUsagesForProject(project, dependingSymbols, out reason))
                return false;
        }

        if (!DeleteProjectFiles(project, out reason))
            return false;

        ForceUnloadProject(project, dependingSymbols);
        return true;
    }

    private static bool DeleteProjectFiles(EditableSymbolProject project, out string reason)
    {
        if (project.IsReadOnly)
        {
            reason = $"Could not delete project [{project.DisplayName}] because it is read-only";
            return false;
        }

        try
        {
            var folder = project.Folder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                reason = $"Could not locate project folder: {folder}";
                return false;
            }

            Directory.Delete(folder, true);
            reason = string.Empty;
            return true;
        }
        catch (IOException ioEx)
        {
            reason = $"I/O error while deleting project [{project.DisplayName}]: {ioEx.Message}";
            return false;
        }
        catch (UnauthorizedAccessException authEx)
        {
            reason = $"Access denied while deleting project [{project.DisplayName}]: {authEx.Message}";
            return false;
        }
        catch (Exception ex)
        {
            reason = $"Unexpected error while deleting project [{project.DisplayName}]: {ex.Message}";
            return false;
        }
    }

    private static bool CleanUsagesForProject(EditableSymbolProject projectToDelete, HashSet<Guid> dependingSymbols, out string reason)
    {
        var projectSymbolIds = EditorSymbolPackage.AllSymbols
                                                  .Where(s => s.SymbolPackage.RootNamespace == projectToDelete.CsProjectFile.RootNamespace)
                                                  .Select(s => s.Id)
                                                  .ToHashSet();

        foreach (var dependingId in dependingSymbols)
        {
            if (!SymbolRegistry.TryGetSymbol(dependingId, out var dependingSymbol))
            {
                Log.Warning($"Could not find depending symbol {dependingId} while cleaning usages of project {projectToDelete.DisplayName}");
                continue;
            }

            var childrenUsingProject = dependingSymbol.Children
                                                      .Where(kvp => projectSymbolIds.Contains(kvp.Value.Symbol.Id))
                                                      .Select(kvp => kvp.Value)
                                                      .ToList();

            if (childrenUsingProject.Count == 0)
                continue;

            foreach (var child in childrenUsingProject)
            {
                var childId = child.Id;
                var connections = dependingSymbol.Connections.ToList();
                foreach (var c in connections)
                {
                    if (c.SourceParentOrChildId == childId || c.TargetParentOrChildId == childId)
                    {
                        dependingSymbol.RemoveConnection(c);
                    }
                }
            }

            foreach (var child in childrenUsingProject)
            {
                dependingSymbol.RemoveChild(child.Id);
            }

            Log.Debug($"Disconnected and removed {childrenUsingProject.Count} usages of project [{projectToDelete.DisplayName}] from [{dependingSymbol.Name}]");
        }

        reason = string.Empty;
        return true;
    }

    private void ForceUnloadProject(EditableSymbolProject project, HashSet<Guid>? dependingSymbolIds)
    {
        var affectedProjects = new HashSet<EditableSymbolProject> { project };

        if (dependingSymbolIds is { Count: > 0 })
        {
            foreach (var depId in dependingSymbolIds)
            {
                if (!SymbolRegistry.TryGetSymbol(depId, out var depSymbol))
                    continue;

                var depProject = EditableSymbolProject.AllProjects
                                                      .FirstOrDefault(p => p.CsProjectFile.RootNamespace == depSymbol.SymbolPackage.RootNamespace);
                if (depProject != null)
                {
                    affectedProjects.Add(depProject);
                }
            }
        }

        foreach (var p in affectedProjects)
        {
            p.MarkCodeExternallyModified();
        }

        project.Dispose();
    }

    #endregion
}
