#nullable enable
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Window;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.SkillQuest.Data;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.SkillQuest;

internal sealed class SkillQuestContext // can't be generic because it's used for generic state
{
    internal OpenedProject? OpenedProject;
    internal UiState.UiElementsVisibility? PreviousUiState;
    //internal MagGraphView? GraphView;
    internal ProjectView? ProjectView;
    internal required StateMachine<SkillQuestContext> StateMachine;
    public QuestLevel? ActiveLevel;
    public QuestTopic? ActiveTopic;
    public GraphWindow? GraphWindow;
}