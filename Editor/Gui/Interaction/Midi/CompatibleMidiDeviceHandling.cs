#nullable enable
using System.Reflection;
using Operators.Utils;
using T3.Editor.Gui.Interaction.Midi.CompatibleDevices;
using Type = System.Type;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Handles the initialization and update of <see cref="CompatibleMidiDevice"/>s.
/// </summary>
internal static class CompatibleMidiDeviceHandling
{
    static CompatibleMidiDeviceHandling()
    {
        _compatibleControllerTypes = ScanForCompatibleDevices();
    }

    private static List<Type> ScanForCompatibleDevices()
    {
        var baseType = typeof(CompatibleMidiDevice);
        return Assembly.GetExecutingAssembly()
                       .GetTypes()
                       .Where(t => baseType.IsAssignableFrom(t) && 
                                   !t.IsAbstract && 
                                   t.GetCustomAttribute<MidiDeviceProductAttribute>() != null)
                       .ToList();
    }    
    
    internal static void InitializeConnectedDevices()
    {
        if (!MidiConnectionManager.Initialized)
        {
            //Log.Warning("MidiInConnectionManager should be initialized before InitializeConnectedDevices().");
            MidiConnectionManager.Rescan();
        }

        // Dispose devices
        foreach (var device in _connectedMidiDevices)
        {
            device.Dispose();
        }

        _connectedMidiDevices.Clear();
        
        CreateConnectedCompatibleDevices();
    }

    internal static void UpdateConnectedDevices()
    {
        foreach (var compatibleMidiDevice in _connectedMidiDevices)
        {
            compatibleMidiDevice.Update();
        }
    }

    /// <summary>
    /// Returns a stable snapshot list of connected device statuses for UI rendering.
    /// </summary>
    internal static IReadOnlyList<MidiDeviceStatus> GetConnectedDeviceStatuses()
    {
        var list = new List<MidiDeviceStatus>(_connectedMidiDevices.Count);
        foreach (var d in _connectedMidiDevices)
        {
            try
            {
                list.Add(d.GetStatusSnapshot());
            }
            catch (Exception e)
            {
                Log.Warning($"Failed taking snapshot of device {d}: {e.Message}");
            }
        }

        return list;
    }

    /// <summary>
    /// Creates instances for connected known controller types.
    /// </summary>
    private static void CreateConnectedCompatibleDevices()
    {
        // Log all detected MIDI input devices for debugging
        Log.Gated.MidiController("Scanning for compatible MIDI devices...");
        foreach (var (midiIn, midiInCapabilities) in MidiConnectionManager.MidiIns)
        {
            Log.Gated.MidiController($"  Found MIDI input device: '{midiInCapabilities.ProductName}'");
        }
        
        foreach (var controllerType in _compatibleControllerTypes)
        {
            var attr = controllerType.GetCustomAttribute<MidiDeviceProductAttribute>(false);
            if (attr == null)
            {
                Log.Error($"{controllerType} should implement MidiDeviceProductAttribute");
                continue;
            }

            var productNames = attr.ProductNames;
            Log.Gated.MidiController($"  Looking for controller type {controllerType.Name} with product names: {string.Join(", ", productNames.Select(n => $"'{n}'"))}");

            foreach (var (midiIn, midiInCapabilities) in MidiConnectionManager.MidiIns)
            {
                var productName = midiInCapabilities.ProductName;
                if (!productNames.Contains(productName))
                    continue;
                
                Log.Gated.MidiController($"  Matched device '{productName}' to {controllerType.Name}");
                
                if (!MidiConnectionManager.TryGetMidiOut(productName, out var midiOut))
                {
                    Log.Error($"Can't find midi out connection for {attr.ProductNames}");
                    continue;
                }

                if (Activator.CreateInstance(controllerType) is not CompatibleMidiDevice compatibleDevice)
                {
                    Log.Error("Can't create midi-device?");
                    continue;
                }

                compatibleDevice.Initialize(midiIn, midiOut);
                _connectedMidiDevices.Add(compatibleDevice);
                Log.Gated.MidiController($"Connected compatible midi device {compatibleDevice}");
            }
        }
    }

    private static readonly List<Type> _compatibleControllerTypes;
    private static readonly List<CompatibleMidiDevice> _connectedMidiDevices = new();

    /// <summary>
    /// Returns the connected CompatibleMidiDevice instance that matches the product name, or null.
    /// </summary>
    internal static CompatibleMidiDevice? GetConnectedDeviceByProductName(string productName)
    {
        return _connectedMidiDevices.FirstOrDefault(d => d.DeviceProductName == productName);
    }
}