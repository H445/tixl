using T3.Editor.Gui.Interaction.Midi.CommandProcessing;

namespace T3.Editor.Gui.Interaction.Midi
{
    // Enums shared across all controller implementations

    /// <summary>Physical control type - determines how the layout view renders it.</summary>
    public enum ControlType
    {
        Pad,
        Button,
        Fader,
        Knob,
        Encoder,
    }

    /// <summary>What kind of LED feedback a control supports.</summary>
    public enum ColorCapability
    {
        None,
        SingleColor,
        BiColor,
        Rgb,
    }

    /// <summary>MIDI message type used to communicate with this control.</summary>
    public enum MidiMessageType
    {
        Note,
        ControlChange,
    }

    /// <summary>Identifies the color palette family a device belongs to.</summary>
    public enum ColorPaletteFamily
    {
        SevenState,
        Rgb128,
        LaunchpadRgb,
    }

    /// <summary>Describes a single physical control on a MIDI device.</summary>
    public readonly record struct ControlDef(
        int Id,
        string Label,
        string Tooltip = "",
        ControlType Type = ControlType.Button,
        ColorCapability Color = ColorCapability.None,
        bool SupportsBehavior = false,
        MidiMessageType MidiType = MidiMessageType.Note,
        int? Channel = null,
        bool IsVirtual = false)
    {
        public string Tip => string.IsNullOrEmpty(Tooltip) ? Label : Tooltip;
        public bool SupportsColor => Color != ColorCapability.None;
        public static implicit operator ButtonRange(ControlDef def) => new(def.Id);
        public static ButtonRange Range(ControlDef[] defs) => new(defs[0].Id, defs[^1].Id);
        public static ButtonRange Range(ReadOnlySpan<ControlDef> defs) => new(defs[0].Id, defs[^1].Id);
    }

    /// <summary>
    /// Immutable descriptor holding device-wide metadata for a MIDI controller.
    /// Each CompatibleMidiDevice subclass exposes a static instance so that
    /// layout views and UI code can query capabilities without reflection.
    /// </summary>
    public sealed class DeviceDescriptor
    {
        public DeviceDescriptor(
            int clipGridRows,
            int clipGridColumns,
            ColorPaletteFamily palette,
            bool supportsBehavior = false,
            bool hasShift = true,
            byte[] sysExInit = null,
            byte defaultModeByte = 0x40,
            bool supportsMultipleModes = false)
        {
            ClipGridRows = clipGridRows;
            ClipGridColumns = clipGridColumns;
            Palette = palette;
            SupportsBehavior = supportsBehavior;
            HasShift = hasShift;
            SysExInit = sysExInit;
            DefaultModeByte = defaultModeByte;
            SupportsMultipleModes = supportsMultipleModes;
        }

        public int ClipGridRows { get; }
        public int ClipGridColumns { get; }
        public ColorPaletteFamily Palette { get; }
        public bool SupportsBehavior { get; }
        public bool HasShift { get; }
        public byte[] SysExInit { get; }
        public byte DefaultModeByte { get; }
        public bool SupportsMultipleModes { get; }
        public int ClipGridSize => ClipGridRows * ClipGridColumns;
    }
}
