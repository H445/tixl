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
                            float[] controllerValues,
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
        ControllerValues = controllerValues ?? Array.Empty<float>();
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
    /// <summary>Last seen controller values indexed by channel*128+controllerId in range 0..1.</summary>
    public float[] ControllerValues { get; }
    public int ControlCount { get; }
    public int? ClipGridSize { get; }
    public DateTime SnapshotTimeUtc { get; }
}
