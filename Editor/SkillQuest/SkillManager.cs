#nullable enable
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.SkillQuest.Data;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.SkillQuest;

internal static partial class SkillManager
{
    internal static void Initialize()
    {
        InitializeLevels();

        SkillProgressUserData.LoadUserData();
        SkillProgressUserData.SaveUserData();
    }

    internal static void Update()
    {
        var playmodeEnded = _context.ProjectView?.GraphView is { Destroyed: true };
        if (_context.StateMachine.CurrentState != SkillQuestStates.Inactive && playmodeEnded)
        {
            _context.StateMachine.SetState(SkillQuestStates.Inactive, _context);
        }

        _context.StateMachine.UpdateAfterDraw(_context);
    }

    /// <summary>
    /// This is called after processing of a frame and can be used to access the output evaluation context
    /// </summary>
    public static void PostUpdate()
    {
        
        if (_context.StateMachine.CurrentState != SkillQuestStates.Playing)
            return;

        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            Log.Warning("Can't find output window for playmode?!");
            return;
        }

        if (!outputWindow.EvaluationContext.FloatVariables.TryGetValue(PlayModeProgressVariableId, out var progress))
        {
            Log.Warning($"Can't find progress variable '{PlayModeProgressVariableId}' after evaluation?");
            return;
        }

        if (_context.StateMachine.StateTime > 1 && progress >= 1.0f)
        {
            ExitPlayMode();
        }
    }


    private const string PlayModeProgressVariableId = "_PlayModeProgress";

    private static void InitializeLevels()
    {
        // TODO: Load from Json
        SkillQuestContext.Topics = CreateMockLevelStructure();
    }

    public static bool TryGetActiveTopic([NotNullWhen(true)] out QuestTopic? topic)
    {
        topic = null;

        if (SkillQuestContext.Topics.Count == 0)
            return false;

        topic = SkillQuestContext.Topics[0];
        return true;
    }

    public static bool TryGetActiveLevel([NotNullWhen(true)] out QuestLevel? level)
    {
        level = null;
        if (!TryGetActiveTopic(out var activeTopic))
            return false;

        if (activeTopic.Levels.Count == 0)
            return false;

        level = activeTopic.Levels[0];
        return true;
    }

    private static bool TryGetSkillsProject([NotNullWhen(true)] out EditableSymbolProject? skillProject)
    {
        skillProject = null;
        foreach (var p in EditableSymbolProject.AllProjects)
        {
            if (p.Alias == "Skills")
            {
                skillProject = p;
                return true;
            }
        }

        return false;
    }

    public static void StartPlayMode(GraphWindow graphWindow, QuestLevel activeLevel)
    {
        if (!TryGetSkillsProject(out var skillProject))
            return;

        if (!OpenedProject.TryCreateWithExplicitHome(skillProject,
                                                     activeLevel.SymbolId,
                                                     out var openedProject,
                                                     out var failureLog))
        {
            Log.Warning(failureLog);
            return;
        }

        graphWindow.TrySetToProject(openedProject);
        _context.OpenedProject = openedProject;

        // if (graphWindow.ProjectView?.GraphView is not MagGraphView magGraphView)
        //     return;

        _context.ProjectView = graphWindow.ProjectView;
        _context.StateMachine.SetState(SkillQuestStates.Playing, _context);
    }

    private static void ExitPlayMode()
    {
        Debug.Assert(_context.OpenedProject != null);
        
        if(!_context.OpenedProject.Package.SymbolUis.TryGetValue(_context.OpenedProject.Package.HomeSymbolId, out var homeSymbolId))
        {
            Log.Warning($"Can't find symbol to revert changes?");
            return;
        }
        _context.ProjectView?.Close();
        _context.OpenedProject.Package.Reload(homeSymbolId);
        _context.StateMachine.SetState(SkillQuestStates.Inactive, _context);
    }

    
    private static readonly SkillQuestContext _context = new()
                                                             {
                                                                 StateMachine = new
                                                                     StateMachine<SkillQuestContext>(typeof(SkillQuestStates),
                                                                                                     SkillQuestStates.Inactive
                                                                                                    ),
                                                             };
}