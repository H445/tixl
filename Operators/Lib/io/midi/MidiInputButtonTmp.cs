using NAudio.Midi;
using Operators.Utils;
using T3.Core.Animation;
using T3.Core.Stats;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("b7e7d697-1322-4484-a78a-94f76d643193")]
public sealed class MidiInputButtonTmp : Instance<MidiInputButtonTmp>
,MidiConnectionManager.IMidiConsumer,IStatusProvider
{
    [Output(Guid = "5aab8de1-830d-41b0-8f3d-a457b15bb147")]
    public readonly Slot<float> Result = new();

    [Output(Guid = "187ab3c4-bda4-454e-b11b-967e8372c92f")]
    public readonly Slot<List<float>> Range = new();
        
    [Output(Guid = "68f66510-ae52-49ab-be3f-21af3efef260")]
    public readonly Slot<bool> WasHit = new();
        
    public MidiInputButtonTmp()
    {
        Result.UpdateAction += Update;
        Range.UpdateAction += Update;
        WasHit.UpdateAction += Update;
    }

    private bool _initialized;
    private double _lastUpdateTime = -1;
        
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        MidiConnectionManager.UnregisterConsumer(this);
    }

    private void Update(EvaluationContext context)
    {
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) < 0.0001f)
            return;
        
        _lastUpdateTime= context.LocalFxTime;
        
        if (!_initialized)
        {
            MidiConnectionManager.RegisterConsumer(this);
            _initialized = true;
        }
            
        _deviceName = Device.GetValue(context);

        if (MidiConnectionManager.TryGetMidiIn(_deviceName, out _))
        {
            _warningMessage = null;
            this.ClearErrorState();
        }
        else
        {
            _warningMessage = $"Midi device '{_deviceName}' is not captured.\nYou can try Windows » Settings » Midi » Rescan Devices.";
            this.LogWarningState(_warningMessage);
        }

        _deviceChannel = Channel.GetValue(context);
        _deviceControllerId = Control.GetValue(context);



        var wasHit = false;
        lock (this)
        {
            foreach (var signal in _lastMatchingSignals)
            {

                var hasValueChanged = Math.Abs(_currentControllerValue - signal.ControllerValue) > 0.001f;
                _currentControllerValue = signal.ControllerValue;
                

                if (hasValueChanged && signal.ControllerValue > 0)
                    wasHit = true;
                    
                LastMessageTime = Playback.RunTimeInSecs;
            }

            _lastMatchingSignals.Clear();
        }
            
        var currentValue = _currentControllerValue;


        WasHit.Value = wasHit;
        Result.Value = currentValue;
        
    }

    public void ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
    }
        

    /// <summary>
    /// This will cause update to be called on next frame 
    /// </summary>
    private void FlagAsDirty()
    {
        // Disable until invalidation is fixed
        // Result.DirtyFlag.Invalidate();
        // Range.DirtyFlag.Invalidate();
        // WasHit.DirtyFlag.Invalidate();
            
        Result.DirtyFlag.Trigger =  DirtyFlagTrigger.Animated;
        Range.DirtyFlag.Trigger =   DirtyFlagTrigger.Animated;
        WasHit.DirtyFlag.Trigger =  DirtyFlagTrigger.Animated;
    }

    /// <remarks>
    /// This comes in multi threaded
    /// </remarks>
    public void MessageReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
        lock (this)
        {
            if (sender is not MidiIn midiIn || msg.MidiEvent == null)
                return;

            MidiSignal newSignal = null;

            var device = MidiConnectionManager.GetDescriptionForMidiIn(midiIn);

            switch (msg.MidiEvent)
            {

                case NoteEvent noteEvent:
                    switch (noteEvent.CommandCode)
                    {
                        case MidiCommandCode.NoteOn:
                        {
                            newSignal = new MidiSignal()
                                            {
                                                Channel = noteEvent.Channel,
                                                ControllerId = noteEvent.NoteNumber,
                                                ControllerValue = noteEvent.Velocity,
                                            };
                            break;
                        }
                        case MidiCommandCode.NoteOff:
                            newSignal = new MidiSignal()
                                            {
                                                Channel = noteEvent.Channel,
                                                ControllerId = noteEvent.NoteNumber,
                                                ControllerValue = 0,
                                            };
                            break;

                    }

                    break;
                
            }

            if (newSignal == null)
                return;

            var matchesDevice = string.IsNullOrEmpty(_deviceName) || device.ProductName == _deviceName;
            var matchesChannel = _deviceChannel < 0 || newSignal.Channel == _deviceChannel;
            var matchesSingleController = _deviceControllerId < 0 || newSignal.ControllerId == _deviceControllerId;
            var matchesController = matchesSingleController;

            if (matchesDevice && matchesChannel && matchesController)
            {
                _lastMatchingSignals.Add(newSignal);
                FlagAsDirty();
            }
        }
    }

    public void OnSettingsChanged()
    {
        Result.DirtyFlag.Invalidate();
        Range.DirtyFlag.Invalidate();
        WasHit.DirtyFlag.Invalidate();
    }
        
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_warningMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;
    }

    public string GetStatusMessage()
    {
        return _warningMessage;
    }

    private string _warningMessage;
        


    private class MidiSignal
    {
        public int Channel;
        public int ControllerId;
        public int ControllerValue;
    }

    public double LastMessageTime; // used for OpUi

    private string _deviceName;
    private int _deviceChannel = -1;
    private int _deviceControllerId = -1;
    private readonly List<MidiSignal> _lastMatchingSignals = new(10);

    private float _currentControllerValue;


    [Input(Guid = "da372541-40d1-499e-ad69-eebe03662d95")]
    public readonly InputSlot<string> Device = new();

    [Input(Guid = "cf192485-5d88-46c4-8155-c258e46c441f")]
    public readonly InputSlot<int> Channel = new();

    [Input(Guid = "eba87e98-f0a1-47a9-a254-84a706590272")]
    public readonly InputSlot<int> Control = new();

}