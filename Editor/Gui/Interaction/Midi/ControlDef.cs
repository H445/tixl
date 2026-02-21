using T3.Editor.Gui.Interaction.Midi.CommandProcessing;

namespace T3.Editor.Gui.Interaction.Midi
{
    /// <summary>
    /// Associates a MIDI note or CC number with a label and tooltip.
    /// Shared helper usable by all controller implementations.
    /// </summary>
    public readonly record struct ControlDef(int Id, string Label, string Tooltip = "")
    {
        /// <summary>Returns Tooltip if set, otherwise falls back to Label.</summary>
        public string Tip => string.IsNullOrEmpty(Tooltip) ? Label : Tooltip;

        /// <summary>Converts a single ControlDef to a single-value ButtonRange.</summary>
        public static implicit operator ButtonRange(ControlDef def) => new(def.Id);

        /// <summary>Creates a contiguous ButtonRange spanning first..last IDs from a ControlDef array.</summary>
        public static ButtonRange Range(ControlDef[] defs) => new(defs[0].Id, defs[^1].Id);
    }
}

