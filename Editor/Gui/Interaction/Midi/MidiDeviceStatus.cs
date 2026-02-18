using System;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Immutable snapshot of a MIDI device state for read-only UI rendering.
/// </summary>
public sealed class MidiDeviceStatus
{
    public MidiDeviceStatus(string productName,
                            string deviceTypeName,
                            bool isConnected,
                            bool isInControlMode,
                            bool? useGenericMode,
                            int[] controllerColors,
                            int controlCount,
                            int? clipGridSize,
                            DateTime snapshotTimeUtc)
    {
        ProductName = productName;
        DeviceTypeName = deviceTypeName;
        IsConnected = isConnected;
        IsInControlMode = isInControlMode;
        UseGenericMode = useGenericMode;
        ControllerColors = controllerColors ?? Array.Empty<int>();
        ControlCount = controlCount;
        ClipGridSize = clipGridSize;
        SnapshotTimeUtc = snapshotTimeUtc;
    }

    public string ProductName { get; }
    public string DeviceTypeName { get; }
    public bool IsConnected { get; }
    public bool IsInControlMode { get; }
    public bool? UseGenericMode { get; }
    public int[] ControllerColors { get; }
    public int ControlCount { get; }
    public int? ClipGridSize { get; }
    public DateTime SnapshotTimeUtc { get; }
}

