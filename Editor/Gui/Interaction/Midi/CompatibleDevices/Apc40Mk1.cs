using NAudio.Midi;
using T3.Editor.Gui.Interaction.Midi.CommandProcessing;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;

namespace T3.Editor.Gui.Interaction.Midi.CompatibleDevices;

// ReSharper disable InconsistentNaming, UnusedMember.Local, CommentTypo, StringLiteralTypo

/// <summary>
/// MIDI controller implementation for the Akai APC40 (original/Mk1).
/// 
/// The APC40 uses a simpler LED control scheme compared to Mk2 with only
/// 7 color states (off, green, green blinking, red, red blinking, orange, orange blinking).
/// 
/// The device is initialized to "Generic" mode (0x40) which allows basic LED control.
/// </summary>
[MidiDeviceProduct("Akai APC40")]
public sealed class Apc40Mk1 : CompatibleMidiDevice
{
    public Apc40Mk1()
    {
        CommandTriggerCombinations =
            [
                // Snapshot activate/create - press clip button to activate or create snapshot
                new CommandTriggerCombination(SnapshotActions.ActivateOrCreateSnapshotAtIndex, InputModes.Default, [SceneTrigger1To40],
                                              CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                // Snapshot save - hold Shift + press clip button to save
                new CommandTriggerCombination(SnapshotActions.SaveSnapshotAtIndex, InputModes.Save, [SceneTrigger1To40],
                                              CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                // Snapshot delete - hold Scene Launch 1 + press clip button to delete
                new CommandTriggerCombination(SnapshotActions.RemoveSnapshotAtIndex, InputModes.Delete, [SceneTrigger1To40],
                                              CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                // Blend between two snapshots - press two clip buttons simultaneously
                new CommandTriggerCombination(BlendActions.StartBlendingSnapshots, InputModes.Default, [SceneTrigger1To40],
                                              CommandTriggerCombination.ExecutesAt.AllCombinedButtonsReleased),

                // Start blend towards - hold Scene Launch 2 + press clip button to start blend
                new CommandTriggerCombination(BlendActions.StartBlendingTowardsSnapshot, InputModes.BlendTo, [SceneTrigger1To40],
                                              CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed),

                // Stop blending - press Stop All Clips to stop blend operation
                new CommandTriggerCombination(BlendActions.StopBlendingTowards, InputModes.Default, [ClipStopAllDef],
                                              CommandTriggerCombination.ExecutesAt.SingleActionButtonPressed),

                // Update blend progress with crossfader
                new CommandTriggerCombination(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, [CrossfaderDef],
                                              CommandTriggerCombination.ExecutesAt.ControllerChange),

                // Update blend progress with Master Fader
                new CommandTriggerCombination(BlendActions.UpdateBlendingTowardsProgress, InputModes.Default, [MasterFaderDef],
                                              CommandTriggerCombination.ExecutesAt.ControllerChange),

                // Update blend values with channel faders
                new CommandTriggerCombination(BlendActions.UpdateBlendValues, InputModes.Default, [FaderDef],
                                              CommandTriggerCombination.ExecutesAt.ControllerChange),

                // Mode switching - Shift + Record/Arm 1/2/3 to switch between modes
                // Record/Arm 1 = Generic passthrough (0x40), Record/Arm 2 = Ableton passthrough (0x41), Record/Arm 3 = Ableton control (0x41)
                new CommandTriggerCombination(HandleModeSwitch, InputModes.Save, [RecordArmButtons],
                                              CommandTriggerCombination.ExecutesAt.SingleRangeButtonPressed)

            ];

        ModeButtons =
            [
                new ModeButton(SceneLaunchDefs[0], InputModes.Delete),
                new ModeButton(SceneLaunchDefs[1], InputModes.BlendTo),
                new ModeButton(ShiftDef, InputModes.Save)
            ];
    }

    /// <summary>
    /// Handles mode switching based on which Record/Arm button was pressed (with Shift held).
    /// Index 0 = Generic passthrough (0x40), Index 1 = Ableton passthrough (0x41), Index 2 = Ableton control (0x41)
    /// </summary>
    private void HandleModeSwitch(int index)
    {
        switch (index)
        {
            case 0: // Record/Arm 1 - Generic passthrough mode (0x40)
                Log.Gated.MidiController("APC40 Mk1: Setting GENERIC PASSTHROUGH mode (0x40)");
                _useGenericMode = true;
                SendModeInitSysEx();
                SetControlMode(false);
                break;

            case 1: // Record/Arm 2 - Ableton passthrough mode (0x41)
                Log.Gated.MidiController("APC40 Mk1: Setting ABLETON PASSTHROUGH mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(false);
                break;

            case 2: // Record/Arm 3 - Ableton control mode (0x41)
                Log.Gated.MidiController("APC40 Mk1: Setting ABLETON CONTROL mode (0x41)");
                _useGenericMode = false;
                SendModeInitSysEx();
                SetControlMode(true);
                break;

            default:
                Log.Gated.MidiController($"APC40 Mk1: Ignoring mode switch for index {index}");
                return; // Don't clear signals for invalid index
        }

        // Clear button signals after mode switch to prevent stale signals from
        // blocking subsequent mode switches. The button mapping changes between
        // Generic and Ableton modes, so old signals may not match new button IDs.
        ClearButtonSignals();
    }

    /// <summary>
    /// Sends the SysEx initialization message to set the APC40 mode.
    /// Uses 0x40 (Generic) or 0x41 (Ableton Live) based on _useGenericMode flag.
    /// </summary>
    private void SendModeInitSysEx()
    {
        if (MidiOutConnection == null)
            return;

        // Clear all LEDs BEFORE mode switch
        // This clears using BOTH mode mappings to ensure all LEDs are off
        ClearAllLedsRaw();

        var modeIdentifier = _useGenericMode ? (byte)0x40 : (byte)0x41;
        Log.Gated.MidiController($"APC40 Mk1: Sending mode SysEx (0x{modeIdentifier:X2})...");

        var buffer = new byte[]
                         {
                             0xF0, // MIDI exclusive start
                             0x47, // Manufacturers ID Byte (Akai)
                             0x00, // System Exclusive Device ID
                             0x73, // Product model ID (APC40)
                             0x60, // Message type identifier (Introduction message)
                             0x00, // Number of data bytes to follow (most significant)
                             0x04, // Number of data bytes to follow (least significant) = 4 bytes
                             modeIdentifier, // Application/Configuration Identifier (0x40=Generic, 0x41=Ableton Live mode)
                             0x08, // PC application Software version major
                             0x01, // PC application Software version minor
                             0x01, // PC application Software version bug-fix level
                             0xF7 // MIDI exclusive end
                         };

        try
        {
            MidiOutConnection.SendBuffer(buffer);
            _initialized = true;
            Log.Gated.MidiController($"APC40 Mk1: Mode switch complete (0x{modeIdentifier:X2})");
        }
        catch (Exception e)
        {
            Log.Warning($"APC40 Mk1: Failed to send mode SysEx: {e.Message}");
        }

        // Only update the mode indicator LED (Record/Arm 1, 2, or 3)
        // Don't update any other LEDs - let the normal update cycle handle that
        UpdateRecordArmModeLeds();
    }

    /// <summary>
    /// Clears all LEDs on the device by sending direct MIDI messages.
    /// Bypasses cache and clears using BOTH Generic and Ableton mode mappings.
    /// </summary>
    private void ClearAllLedsRaw()
    {
        if (MidiOutConnection == null)
            return;

        Array.Fill(CacheControllerColors, -1);

        // Clear clip grid - Generic mode (Notes 0-39 on Channel 1)
        foreach (var note in GenericClipGridNotes.Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);

        // Clear clip grid - Ableton mode (Notes 53-57 on Channels 1-8)
        foreach (var note in AbletonClipGridNotes.Indices())
        foreach (var ch in MidiChannels1To8.Indices())
            SendNoteRaw(ch, note, 0);

        // Clear scene launch LEDs
        foreach (var note in ControlDef.Range(SceneLaunchDefs).Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);

        // Clear Record/Arm LEDs - Generic mode (Notes 48-55 on Channel 1)
        foreach (var note in GenericRecordArmNotes.Indices())
            SendNoteRaw(MidiChannels1To8.StartIndex, note, 0);

        // Clear Record/Arm LEDs - Ableton mode (Note 48 on Channels 1-8)
        foreach (var ch in MidiChannels1To8.Indices())
            SendNoteRaw(ch, AbletonRecordArmNote, 0);
    }

    /// <summary>
    /// Sends a raw MIDI note, bypassing cache. Used for LED clearing.
    /// </summary>
    private void SendNoteRaw(int channel, int note, int velocity)
    {
        try
        {
            var evt = new NoteOnEvent(0, channel, note, velocity, 0);
            MidiOutConnection?.Send(evt.GetAsShortMessage());
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to send MIDI note (ch={channel}, note={note}): {e.Message}");
        }
    }

    /// <summary>
    /// Called when control mode changes. Reinitialize device when entering control mode.
    /// </summary>
    protected override void OnControlModeChanged(bool isNowInControlMode)
    {
        base.OnControlModeChanged(isNowInControlMode);

        if (isNowInControlMode)
        {
            // Re-entering control mode - reinitialize the device
            _initialized = false;
        }

        // Update Record/Arm LEDs to show current mode
        UpdateRecordArmModeLeds();
    }

    /// <summary>
    /// Updates Record/Arm 1, 2, and 3 LEDs to show current mode.
    /// Green = active mode, Off = inactive
    /// </summary>
    private void UpdateRecordArmModeLeds()
    {
        if (MidiOutConnection == null)
            return;

        var modeLedColors = new[]
                                {
                                    _useGenericMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off, // Generic passthrough
                                    !_useGenericMode && !IsInControlMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off, // Ableton passthrough
                                    !_useGenericMode && IsInControlMode ? Apc40Mk1Colors.Green : Apc40Mk1Colors.Off // Ableton control
                                };

        for (var i = 0; i < modeLedColors.Length; i++)
        {
            var channel = _useGenericMode ? MidiChannels1To8.StartIndex : MidiChannels1To8.StartIndex + i;
            var note = _useGenericMode ? GenericRecordArmNotes.StartIndex + i : AbletonRecordArmNote;
            SendNoteRaw(channel, note, (int)modeLedColors[i]);
        }
    }

    /// <summary>
    /// Clears all LEDs when in passthrough mode. Shows mode highlighting when Shift is held.
    /// </summary>
    protected override void ClearDeviceLeds()
    {
        if (MidiOutConnection == null)
            return;

        _updateCount++;

        // Clear clip launch grid LEDs or show mode highlight
        for (var i = 0; i < ClipGridSize; i++)
        {
            if (ActiveMode != InputModes.Default)
                CacheControllerColors[i] = -1;

            SendColor(MidiOutConnection, i, AddModeHighlight(i, (int)Apc40Mk1Colors.Off));
        }

        // Clear scene launch LEDs
        foreach (var i in ControlDef.Range(SceneLaunchDefs).Indices())
        {
            CacheControllerColors[i] = -1;
            SendColor(MidiOutConnection, i, (int)Apc40Mk1Colors.Off);
        }
    }

    protected override void UpdateVariationVisualization()
    {
        _updateCount++;
        if (!_initialized)
            SendModeInitSysEx();

        // Update clip launch button LEDs (5x8 grid)
        UpdateRangeLeds(SceneTrigger1To40, mappedIndex =>
                                           {
                                               if (!SymbolVariationPool.TryGetSnapshot(mappedIndex, out var v))
                                                   return AddModeHighlight(mappedIndex, (int)Apc40Mk1Colors.Off);

                                               var isBlendTarget = BlendActions.BlendTowardsIndex == mappedIndex;
                                               var color = v.State switch
                                                               {
                                                                   Variation.States.Active    => Apc40Mk1Colors.Red,
                                                                   Variation.States.Modified  => Apc40Mk1Colors.Orange,
                                                                   Variation.States.IsBlended => Apc40Mk1Colors.OrangeBlinking,
                                                                   Variation.States.InActive => isBlendTarget
                                                                                                    ? Apc40Mk1Colors.OrangeBlinking
                                                                                                    : Apc40Mk1Colors.Green,
                                                                   _ => Apc40Mk1Colors.Off
                                                               };

                                               return AddModeHighlight(mappedIndex, (int)color);
                                           });

        // Update scene launch LEDs only in Ableton control mode
        if (IsInControlMode && !_useGenericMode)
            UpdateSceneLaunchLeds();
    }

    /// <summary>
    /// Updates the scene launch button LEDs to indicate current input mode.
    /// </summary>
    private void UpdateSceneLaunchLeds()
    {
        if (MidiOutConnection == null)
            return;

        var colors = new[]
                         {
                             ActiveMode == InputModes.Delete ? Apc40Mk1Colors.RedBlinking : Apc40Mk1Colors.Red,
                             ActiveMode == InputModes.BlendTo ? Apc40Mk1Colors.OrangeBlinking : Apc40Mk1Colors.Orange,
                             Apc40Mk1Colors.Off,
                             Apc40Mk1Colors.Off,
                             Apc40Mk1Colors.Off
                         };

        for (var i = 0; i < colors.Length; i++)
            SendColor(MidiOutConnection, SceneLaunchDefs[i].Id, (int)colors[i]);
    }

    private int AddModeHighlight(int index, int orgColor)
    {
        // Software-based flashing using solid colors
        var indicatedStatus = (_updateCount + index / AbletonClipGridColumns) % 30 < 4;
        if (!indicatedStatus)
        {
            return orgColor;
        }

        return ActiveMode switch
                   {
                       InputModes.Save    => (int)Apc40Mk1Colors.Green,
                       InputModes.BlendTo => (int)Apc40Mk1Colors.Orange,
                       InputModes.Delete  => (int)Apc40Mk1Colors.Red,
                       _                  => orgColor
                   };
    }

    /// <summary>
    /// Sends LED color using APC40 Mk1 specific channel mapping.
    /// Generic mode: Notes 0-39 on Channel 1. Ableton mode: Notes 53-57 on Channels 1-8.
    /// </summary>
    protected override void SendColor(MidiOut midiOut, int apcControlIndex, int colorCode)
    {
        // colorCode is Mk1 color (velocity). No behavior is encoded for Mk1.
        if (apcControlIndex < 0 || apcControlIndex >= CacheControllerColors.Length)
            return;

        var color = (Apc40Mk1Colors)(colorCode & 0xFF);
        SendLedState(midiOut, apcControlIndex, new LedState(color));
    }

    /// <summary>
    /// Converts APC40 Mk1 MIDI channel/note to button index.
    /// Mapping differs between Generic Mode (0x40) and Ableton Live Mode (0x41).
    /// </summary>
    protected override int ConvertNoteToButtonId(int channel, int noteNumber)
    {
        // Shift button - same in both modes
        if (noteNumber == ShiftButtonNote && channel == MidiChannels1To8.StartIndex)
        {
            Log.Gated.MidiController($"ConvertNoteToButtonId: Shift button Note={noteNumber}, Channel={channel} -> ButtonId={ShiftButtonNote}");
            return ShiftButtonNote;
        }

        // Clip launch grid
        if (_useGenericMode)
        {
            // Generic: Notes 0-39 on Channel 1
            if (GenericClipGridNotes.IncludesButtonIndex(noteNumber) && channel == MidiChannels1To8.StartIndex)
            {
                Log.Gated.MidiController($"ConvertNoteToButtonId [Generic]: Clip grid Note={noteNumber}, Channel={channel} -> ButtonId={noteNumber}");
                return noteNumber;
            }
        }
        else
        {
            // Ableton: Notes 53-57 on Channels 1-8 (5x8 grid)
            if (AbletonClipGridNotes.IncludesButtonIndex(noteNumber) && MidiChannels1To8.IncludesButtonIndex(channel))
            {
                var row = AbletonClipGridNotes.GetMappedIndex(noteNumber);
                var col = MidiChannels1To8.GetMappedIndex(channel);
                var buttonId = row * AbletonClipGridColumns + col;
                Log.Gated.MidiController($"ConvertNoteToButtonId [Ableton]: Clip grid Note={noteNumber}, Channel={channel} -> row={row}, col={col}, ButtonId={buttonId}");
                return buttonId;
            }
        }

        // Record/Arm buttons - must work in both modes for switching
        if (noteNumber == AbletonRecordArmNote && MidiChannels1To8.IncludesButtonIndex(channel))
        {
            var buttonId = RecordArmBaseId + MidiChannels1To8.GetMappedIndex(channel);
            Log.Gated.MidiController($"ConvertNoteToButtonId: Record/Arm (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
            return buttonId;
        }

        switch (_useGenericMode)
        {
            case true when channel == MidiChannels1To8.StartIndex:
            {
                // Generic Record/Arm: Notes 49-55 on Channel 1 (48 handled above)
                if (GenericRecordArmNotes.IncludesButtonIndex(noteNumber) && noteNumber != AbletonRecordArmNote)
                {
                    var buttonId = RecordArmBaseId + GenericRecordArmNotes.GetMappedIndex(noteNumber);
                    Log.Gated.MidiController($"ConvertNoteToButtonId: Record/Arm (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                    return buttonId;
                }

                // Generic Track Select: Notes 58-65 on Channel 1
                if (GenericTrackSelectNotes.IncludesButtonIndex(noteNumber))
                {
                    var buttonId = TrackSelectBaseId + GenericTrackSelectNotes.GetMappedIndex(noteNumber);
                    Log.Gated.MidiController($"ConvertNoteToButtonId: Track Select (Generic mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                    return buttonId;
                }

                break;
            }
            // Ableton Track Select: Note 51 on Channels 1-8
            case false when noteNumber == AbletonTrackSelectNote && MidiChannels1To8.IncludesButtonIndex(channel):
            {
                var buttonId = TrackSelectBaseId + MidiChannels1To8.GetMappedIndex(channel);
                Log.Gated.MidiController($"ConvertNoteToButtonId: Track Select (Ableton mapping) Note={noteNumber}, Channel={channel} -> ButtonId={buttonId}");
                return buttonId;
            }
        }

        // Default fallback - use note number directly
        return noteNumber;
    }

    // Base ID for track select buttons to avoid collision with other button IDs
    private const int TrackSelectBaseId = 1000;


    #region Device Descriptor

    /// <summary>
    /// Device-wide metadata for the APC40 Mk1.
    /// Exposes grid dimensions, palette family, and capability flags without reflection.
    /// </summary>
    internal static readonly DeviceDescriptor Descriptor = new(
        clipGridRows: 5,
        clipGridColumns: 8,
        palette: ColorPaletteFamily.SevenState,
        supportsBehavior: false,
        hasShift: true,
        sysExInit: new byte[] { 0xF0, 0x47, 0x00, 0x73, 0x60, 0x00, 0x04 },
        defaultModeByte: 0x40,
        supportsMultipleModes: true
    );

    protected internal override DeviceDescriptor GetDescriptor() => Descriptor;
    protected internal override bool? GetUseGenericMode() => _useGenericMode;

    #endregion

    #region Control Definitions — every physical APC40 control

    // ---- Grid dimensions (derived from Descriptor) ----
    internal static int ClipGridSize    => Descriptor.ClipGridSize;   // 40
    internal static int ClipGridColumns => Descriptor.ClipGridColumns; // 8
    internal static int ClipGridRows    => Descriptor.ClipGridRows;    // 5

    // ---- Clip Launch Grid (5 × 8 = 40 pads) ----
    // IDs 0–39 are logical indices; physical MIDI mapping depends on Generic vs Ableton mode.
    internal static readonly ControlDef[] ClipGridDefs = CreateClipGridDefs();

    private static ControlDef[] CreateClipGridDefs()
    {
        var defs = new ControlDef[Descriptor.ClipGridSize];
        for (var i = 0; i < defs.Length; i++)
        {
            var row = i / Descriptor.ClipGridColumns;
            var col = i % Descriptor.ClipGridColumns;
            defs[i] = new ControlDef(
                Id: i,
                Label: $"",
                Tooltip: $"Clip Launch R{row + 1}C{col + 1}",
                Type: ControlType.Pad,
                Color: ColorCapability.BiColor,
                MidiType: MidiMessageType.Note);
        }
        return defs;
    }

    // ---- Scene Launch (right side of clip grid) ----
    internal static readonly ControlDef[] SceneLaunchDefs =
    [
        new(82, "S1", "Scene Launch 1", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(83, "S2", "Scene Launch 2", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(84, "S3", "Scene Launch 3", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(85, "S4", "Scene Launch 4", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(86, "S5", "Scene Launch 5", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(87, "S6", "Scene Launch 6", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(88, "S7", "Scene Launch 7", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(89, "S8", "Scene Launch 8", Type: ControlType.Button, Color: ColorCapability.SingleColor),
    ];

    // ---- Clip Stop (one per track) ----
    internal static readonly ControlDef[] ClipStopDefs =
    [
        new(52, "1", "Clip Stop Track 1", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(53, "2", "Clip Stop Track 2", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(54, "3", "Clip Stop Track 3", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(55, "4", "Clip Stop Track 4", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(56, "5", "Clip Stop Track 5", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(57, "6", "Clip Stop Track 6", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(58, "7", "Clip Stop Track 7", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(59, "8", "Clip Stop Track 8", Type: ControlType.Button, Color: ColorCapability.SingleColor),
    ];

    internal static readonly ControlDef ClipStopAllDef = new(81, "STOP ALL", "Stop All Clips",
        Type: ControlType.Button, Color: ColorCapability.SingleColor);

    // ---- Track Select row ----
    internal static readonly ControlDef MasterTrackDef = new(0,  "MST", "Master Track Select",
        Type: ControlType.Button);
    internal static readonly ControlDef TrackSelectDef = new(51, "SEL", "Track Select",
        Type: ControlType.Button);

    // ---- Activator / Solo-Cue / Record Arm rows ----
    internal static readonly ControlDef[] ActivatorDefs =
    [
        new(66, "A1", "Activator Track 1", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(67, "A2", "Activator Track 2", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(68, "A3", "Activator Track 3", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(69, "A4", "Activator Track 4", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(70, "A5", "Activator Track 5", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(71, "A6", "Activator Track 6", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(72, "A7", "Activator Track 7", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(73, "A8", "Activator Track 8", Type: ControlType.Button, Color: ColorCapability.SingleColor),
    ];

    // Note: SoloCueDefs all share MIDI note 50 (channel-differentiated in Ableton mode).
    // IDs here are for layout display only; they are not used as ButtonRange sources.
    internal static readonly ControlDef[] SoloCueDefs =
    [
        new(50, "S1", "Solo/Cue Track 1", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S2", "Solo/Cue Track 2", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S3", "Solo/Cue Track 3", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S4", "Solo/Cue Track 4", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S5", "Solo/Cue Track 5", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S6", "Solo/Cue Track 6", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S7", "Solo/Cue Track 7", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(50, "S8", "Solo/Cue Track 8", Type: ControlType.Button, Color: ColorCapability.SingleColor),
    ];

    // Record Arm buttons use virtual IDs (RecordArmBaseId + 0..7) because the physical
    // MIDI mapping differs between Generic mode (Notes 48-55 on Ch1) and Ableton mode
    // (Note 48 on Ch1-8). ConvertNoteToButtonId maps both to these virtual IDs.
    internal static readonly ControlDef[] RecordArmDefs =
    [
        new(RecordArmBaseId + 0, "R1", "Record Arm Track 1", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 1, "R2", "Record Arm Track 2", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 2, "R3", "Record Arm Track 3", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 3, "R4", "Record Arm Track 4", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 4, "R5", "Record Arm Track 5", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 5, "R6", "Record Arm Track 6", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 6, "R7", "Record Arm Track 7", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
        new(RecordArmBaseId + 7, "R8", "Record Arm Track 8", Type: ControlType.Button, Color: ColorCapability.SingleColor, IsVirtual: true),
    ];

    // ---- Navigation / Bank Select ----
    internal static readonly ControlDef ShiftDef      = new(98,  "SHIFT",  "Shift",   Type: ControlType.Button);
    internal static readonly ControlDef BankUpDef     = new(94,  "▲",      "Bank Up", Type: ControlType.Button);
    internal static readonly ControlDef BankDownDef   = new(95,  "▼",      "Bank Down", Type: ControlType.Button);
    internal static readonly ControlDef BankLeftDef   = new(97,  "◄",      "Bank Left", Type: ControlType.Button);
    internal static readonly ControlDef BankRightDef  = new(96,  "►",      "Bank Right", Type: ControlType.Button);
    internal static readonly ControlDef TapTempoDef   = new(99,  "TAP",    "Tap Tempo", Type: ControlType.Button);
    internal static readonly ControlDef NudgeMinusDef = new(100, "NU-",    "Nudge -", Type: ControlType.Button);
    internal static readonly ControlDef NudgePlusDef  = new(101, "NU+",    "Nudge +", Type: ControlType.Button);

    // ---- Transport ----
    internal static readonly ControlDef PlayDef    = new(91,  "PLAY",    "Play",    Type: ControlType.Button, Color: ColorCapability.SingleColor);
    internal static readonly ControlDef StopDef    = new(92,  "STOP",    "Stop",    Type: ControlType.Button, Color: ColorCapability.SingleColor);
    internal static readonly ControlDef RecordDef  = new(93,  "REC",     "Record",  Type: ControlType.Button, Color: ColorCapability.SingleColor);
    internal static readonly ControlDef SessionDef = new(102, "SESSION", "Session / Clip Track", Type: ControlType.Button);

    // ---- Mode knob select buttons (PAN / SEND) ----
    internal static readonly ControlDef[] ModeKnobDefs =
    [
        new(87, "PAN",   "Pan Mode",    Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(88, "Snd A", "Send A Mode", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(89, "Snd B", "Send B Mode", Type: ControlType.Button, Color: ColorCapability.SingleColor),
        new(90, "Snd C", "Send C Mode", Type: ControlType.Button, Color: ColorCapability.SingleColor),
    ];

    // ---- Device control buttons (2 rows × 4) ----
    // Per APC40 Communications Protocol: notes 0x3A..0x41
    internal const int DeviceLeftId  = 60;
    internal const int DeviceRightId = 61;

    internal static readonly ControlDef[] DeviceControlDefs =
    [
        new(58,            "CLIP/DEV", "Clip / Device View", Type: ControlType.Button),
        new(59,            "DEVI",     "Device On/Off",      Type: ControlType.Button),
        new(DeviceLeftId,  "◄",        "Device Left",        Type: ControlType.Button),
        new(DeviceRightId, "►",        "Device Right",       Type: ControlType.Button),
        new(62,            "DETAIL",   "Detail View",        Type: ControlType.Button),
        new(63,            "REC Q",    "Rec Quantization",   Type: ControlType.Button),
        new(64,            "MIDI",     "MIDI Overdub",       Type: ControlType.Button),
        new(65,            "METRO",    "Metronome",          Type: ControlType.Button),
    ];

    // ---- Faders and knobs (CC-based) ----
    internal static readonly ControlDef FaderDef       = new(7,  "FADER",  "Track Fader",    Type: ControlType.Fader,  MidiType: MidiMessageType.ControlChange);
    internal static readonly ControlDef MasterFaderDef = new(14, "MASTER", "Master Fader",   Type: ControlType.Fader,  MidiType: MidiMessageType.ControlChange);
    internal static readonly ControlDef CrossfaderDef  = new(15, "A/B",    "A-B Crossfader", Type: ControlType.Fader,  MidiType: MidiMessageType.ControlChange);
    internal static readonly ControlDef CueLevelDef    = new(47, "CUE",    "Cue Level",      Type: ControlType.Knob,   MidiType: MidiMessageType.ControlChange);
    internal static readonly ControlDef TempoDef       = new(13, "TEMPO",  "Tempo",          Type: ControlType.Knob,   MidiType: MidiMessageType.ControlChange);

    // ---- Track knobs (CC 0x30..0x37 = 48..55) ----
    internal static readonly ControlDef[] TrackKnobDefs =
    [
        new(48, "TK1", "Track Knob 1", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(49, "TK2", "Track Knob 2", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(50, "TK3", "Track Knob 3", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(51, "TK4", "Track Knob 4", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(52, "TK5", "Track Knob 5", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(53, "TK6", "Track Knob 6", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(54, "TK7", "Track Knob 7", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(55, "TK8", "Track Knob 8", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
    ];

    // ---- Device knobs (CC 0x10..0x17 = 16..23) ----
    internal static readonly ControlDef[] DeviceKnobDefs =
    [
        new(16, "DK1", "Device Knob 1", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(17, "DK2", "Device Knob 2", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(18, "DK3", "Device Knob 3", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(19, "DK4", "Device Knob 4", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(20, "DK5", "Device Knob 5", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(21, "DK6", "Device Knob 6", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(22, "DK7", "Device Knob 7", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
        new(23, "DK8", "Device Knob 8", Type: ControlType.Knob, MidiType: MidiMessageType.ControlChange),
    ];

    #endregion

    #region ButtonRanges — derived from ControlDefs where possible, protocol internals otherwise

    private const int ShiftButtonNote = 98;
    private static readonly ButtonRange MidiChannels1To8 = new(1, 8);

    // Generic Mode (0x40) MIDI mappings — protocol-internal, not physical controls
    private static readonly ButtonRange GenericClipGridNotes = new(0, 39);
    private static readonly ButtonRange GenericRecordArmNotes = new(48, 55);
    private static readonly ButtonRange GenericTrackSelectNotes = new(58, 65);

    // Ableton Live Mode (0x41) MIDI mappings — protocol-internal
    private static readonly ButtonRange AbletonClipGridNotes = new(53, 57);
    private const int AbletonClipGridColumns = 8;
    private const int AbletonRecordArmNote = 48;
    private const int AbletonTrackSelectNote = 51;

    // Clip grid trigger range — derived from ClipGridDefs (single source of truth)
    private static readonly ButtonRange SceneTrigger1To40 = ControlDef.Range(ClipGridDefs);

    // Record/Arm trigger range — derived from RecordArmDefs (single source of truth)
    private const int RecordArmBaseId = 2000;
    private static readonly ButtonRange RecordArmButtons = ControlDef.Range(RecordArmDefs);

    #endregion

    private int _updateCount;

    /// <summary>
    /// Sends a Controller Value Update (MIDI CC) to the APC40 for the given channel and control ID.
    /// This implements Outbound Message Type 2 from the APC40 protocol: 0xBn, controller, value.
    /// </summary>
    private void SendControllerValueUpdate(int channel, int controlId, int value)
    {
        if (MidiOutConnection == null)
            return;

        try
        {
            // NAudio ControlChangeEvent expects channel in 1..16 and controller as MidiController enum.
            var cc = new ControlChangeEvent(0, channel, (MidiController)controlId, value);
            MidiOutConnection.Send(cc.GetAsShortMessage());

            Log.Gated.MidiController($"APC40 Mk1: Sent CC ch={channel} ctrl=0x{controlId:X2} ({controlId}) val={value}");
        }
        catch (Exception e)
        {
            Log.Warning($"APC40 Mk1: Failed to send Controller Value Update ch={channel} ctrl=0x{controlId:X2}: {e.Message}");
        }
    }

    /// <summary>
    /// Sets the LED ring type for a knob by index. Ring CCs are always knob CCs + 8.
    /// </summary>
    private void SetKnobRingType(ControlDef[] knobDefs, int knobIndex, int ringType)
    {
        if (knobIndex < 0 || knobIndex >= knobDefs.Length) return;
        SendControllerValueUpdate(1, knobDefs[0].Id + 8 + knobIndex, ringType);
    }

    /// <summary>
    /// Updates the absolute controller value for a knob by index, driving its LED ring display.
    /// </summary>
    private void UpdateKnobValue(ControlDef[] knobDefs, int knobIndex, int value)
    {
        if (knobIndex < 0 || knobIndex >= knobDefs.Length) return;
        SendControllerValueUpdate(1, knobDefs[0].Id + knobIndex, Math.Clamp(value, 0, 127));
    }

    /// <summary>
    /// Convenience wrapper that sets ring mode by APC control index. If the control index maps to
    /// a track or device knob, set the appropriate ring type. Otherwise falls back to previous behavior.
    /// </summary>
    public void SetEncoderRingMode(int apcControlIndex, EncoderRingMode mode, Apc40Mk1Colors color = Apc40Mk1Colors.Green)
    {
        // Map high-level EncoderRingMode to protocol ringType numeric values
        var ringType = mode switch
        {
            EncoderRingMode.Off => 0,
            EncoderRingMode.Single => 1,
            EncoderRingMode.Fill => 2,    // Volume style
            EncoderRingMode.Absolute => 2, // map Absolute to Volume style for now
            EncoderRingMode.Relative => 3, // Pan style as a best-effort for relative
            _ => 0
        };

        // If apcControlIndex falls into track or device knob CC ranges, set via CC
        if (apcControlIndex >= TrackKnobDefs[0].Id && apcControlIndex <= TrackKnobDefs[^1].Id)
        {
            SetKnobRingType(TrackKnobDefs, apcControlIndex - TrackKnobDefs[0].Id, ringType);
            return;
        }

        if (apcControlIndex >= DeviceKnobDefs[0].Id && apcControlIndex <= DeviceKnobDefs[^1].Id)
        {
            SetKnobRingType(DeviceKnobDefs, apcControlIndex - DeviceKnobDefs[0].Id, ringType);
            return;
        }

        // Fallback: preserve legacy behavior (send as LED Note on).
        // APC40 Mk1 doesn't know about LedBehavior here — encode color only.
        var composite = (int)color;
        TrySendLed(apcControlIndex, composite);
    }

    /// <summary>
    /// Sends a raw encoder ring update using explicit color and behavior. For knobs this will send
    /// controller value updates (where applicable) to drive the LED rings according to the protocol.
    /// </summary>
    public void SendEncoderRingRaw(int apcControlIndex, Apc40Mk1Colors color)
    {
        // If this is a track or device knob, send a controller value update instead of NoteOn.
        // Map color to a best-effort numeric value for LED ring display.
        var knobValue = color switch
        {
            Apc40Mk1Colors.Off => 0,
            Apc40Mk1Colors.Green => 64,
            Apc40Mk1Colors.GreenBlinking => 64,
            Apc40Mk1Colors.Red => 127,
            Apc40Mk1Colors.RedBlinking => 127,
            Apc40Mk1Colors.Orange => 90,
            Apc40Mk1Colors.OrangeBlinking => 90,
            _ => 0
        };

        if (apcControlIndex >= TrackKnobDefs[0].Id && apcControlIndex <= TrackKnobDefs[^1].Id)
        {
            UpdateKnobValue(TrackKnobDefs, apcControlIndex - TrackKnobDefs[0].Id, knobValue);
            return;
        }

        if (apcControlIndex >= DeviceKnobDefs[0].Id && apcControlIndex <= DeviceKnobDefs[^1].Id)
        {
            UpdateKnobValue(DeviceKnobDefs, apcControlIndex - DeviceKnobDefs[0].Id, knobValue);
            return;
        }

        // Fallback to legacy NoteOn LED behavior — encode color only for Mk1 devices.
        var composite = (int)color;
        TrySendLed(apcControlIndex, composite);
    }

    /// <summary>
    /// APC40 Mk1 LED color values (sent as velocity in Note On messages)
    /// </summary>
    /// <remarks>
    /// The APC40 Mk1 uses a simple 7-state LED system for the clip launch grid.
    /// Reference: Akai APC40 Communications Protocol
    /// </remarks>
    public enum Apc40Mk1Colors
    {
        Off = 0,
        Green = 1,
        GreenBlinking = 2,
        Red = 3,
        RedBlinking = 4,
        Orange = 5,
        OrangeBlinking = 6
    }

    /// <summary>
    /// Represents an LED state for APC40 Mk1 (color only). Mk1 doesn't support separate
    /// LedBehavior values — behavior is not encoded here.
    /// </summary>
    private readonly record struct LedState(Apc40Mk1Colors Color)
    {
        public static readonly LedState Off = new(Apc40Mk1Colors.Off);
        public int ToCacheKey() => (int)Color;
    }

    /// <summary>
    /// High-level encoder ring modes for callers.
    /// These correspond to the numeric ring type values described in the APC40 protocol.
    /// </summary>
    public enum EncoderRingMode
    {
        Off,
        Absolute,
        Relative,
        Fill,
        Single
    }

    // Internal state
    private bool _initialized;
    private bool _useGenericMode = true; // default to Generic mode

    /// <summary>
    /// Sends an LED state to a button on the APC40 Mk1.
    /// Uses NoteOn messages with velocity encoding for color and channels for behavior (fallback mapping).
    /// </summary>
    private void SendLedState(MidiOut midiOut, int controlIndex, LedState state)
    {
        if (midiOut == null || controlIndex < 0 || controlIndex >= CacheControllerColors.Length)
            return;

        var cacheKey = state.ToCacheKey();
        if (CacheControllerColors[controlIndex] == cacheKey)
            return;

        int channel;
        int noteNumber;

        // APC40 Mk1 does not use behavior channels — always use solid-style mappings.
        if (controlIndex < ClipGridSize)
        {
            if (_useGenericMode)
            {
                // Generic: notes 0-39 on channel 1
                channel = MidiChannels1To8.StartIndex; // channel 1
                noteNumber = controlIndex;
            }
            else
            {
                // Ableton mode: use column channels for solid indication
                var row = controlIndex / AbletonClipGridColumns;
                var col = controlIndex % AbletonClipGridColumns;
                channel = col + MidiChannels1To8.StartIndex; // 1..8
                noteNumber = row + AbletonClipGridNotes.StartIndex; // 53..57
            }
        }
        else
        {
            // Non-grid buttons: send on channel 1
            channel = MidiChannels1To8.StartIndex;
            noteNumber = controlIndex;
        }

        try
        {
            var noteOnEvent = new NoteOnEvent(0, channel, noteNumber, (int)state.Color, 0);
            midiOut.Send(noteOnEvent.GetAsShortMessage());
            CacheControllerColors[controlIndex] = cacheKey;

            Log.Gated.MidiController($"APC40 Mk1: Sent NoteOn ch={channel} note={noteNumber} vel={(int)state.Color} (idx={controlIndex})");
        }
        catch (Exception e)
        {
            Log.Warning($"APC40 Mk1: Failed sending LED NoteOn for idx {controlIndex}: {e.Message}");
        }
    }

}
