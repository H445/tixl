using System.Diagnostics;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.SkillQuest;

internal static class SkillQuestStates
{
    internal static State<SkillQuestContext> Inactive
        = new(
              Enter: context =>
                     {
                         if (context.PreviousUiState != null)
                             UiState.ApplyUiState(context.PreviousUiState);
                     },
              Update: context => { },
              Exit:
              _ => { }
             );

    internal static State<SkillQuestContext> Playing
        = new(
              Enter: context =>
                     {
                         Debug.Assert(context.OpenedProject != null);
                         
                         if (ProjectView.Focused == null)
                         {
                             context.StateMachine.SetState(Inactive,context);
                             return;
                         }

                         // Keep and apply a new UI state
                         context.PreviousUiState = UiState.KeepUiState();
                         LayoutHandling.LoadAndApplyLayoutOrFocusMode(LayoutHandling.Layouts.SkillQuest);

                         if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
                         {
                             UiState.ApplyUiState(context.PreviousUiState);
                             context.StateMachine.SetState(Inactive,context);
                         }
                         
                         UiState.HideAllUiElements();
                         
                         // Pin output
                         var rootInstance = context.OpenedProject.Structure.GetRootInstance();
                         outputWindow.Pinning.PinInstance(rootInstance);
                     },
              
              Update: context => { },
              Exit: _ => { }
             );

    internal static State<SkillQuestContext> Completed
        = new(
              Enter: _ => { },
              Update: context => { },
              Exit: _ => { }
             );
}