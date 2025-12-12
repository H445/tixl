using NAudio.Midi;
using Operators.Utils;
using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.io.midi.apc40;

[Guid("6dbe4ed2-5f14-4ad3-aa19-84679a466815")]
internal sealed class Apc40MidiButton : Instance<Apc40MidiButton>
,MidiConnectionManager.IMidiConsumer,ICustomDropdownHolder,IStatusProvider
{
    [Output(Guid = "9b43cb79-a171-4fc4-bcd2-e13a4e31bbf6")]
    public readonly Slot<Command> Result = new();
    
    [Output(Guid = "8227cd32-c872-41f1-9edd-9afb7c350377")]
    public readonly Slot<bool> IsActive = new();
    
    bool toggledActive = false;

    public Apc40MidiButton()
    {
        Result.UpdateAction = Update;
    }

    private bool _initialized;
    protected override void Dispose(bool isDisposing)
    {
        if(!isDisposing) return;

        if (_initialized)
        {
            MidiConnectionManager.UnregisterConsumer(this);
        }
    }
    private void Update(EvaluationContext context)
    {
        var triggerActive = TriggerSend.GetValue(context);
        var sendMode = SendMode.GetEnumValue<SendModes>(context);
        var deviceName = Device.GetValue(context);
        var foundDevice = false;
        var channel = ChannelNumber.GetValue(context).Clamp(1, 16);
        var noteIndex = NoteNumber.GetValue(context).Clamp(0, 127);
        var durationInMs = ((int)(DurationInSecs.GetValue(context)*1000)).Clamp(1, 100000);

        int onColor = OnColor.GetValue(context).Clamp(0, 7);
        int offColor = OffColor.GetValue(context).Clamp(0, 7);
        

        var triggerJustActivated = false;
        var triggerJustDeactivated = false;

        if(!_initialized)
        {
            MidiConnectionManager.RegisterConsumer(this);
            _initialized = true;
        }
        
        #region output
        
        if (triggerActive != _triggered)
        {
            if (triggerActive)
            {
                
                triggerJustActivated = true;
            }
            else
            {
                triggerJustDeactivated = true;
            }

            _triggered = triggerActive;
        }

        var absTime = (long)Playback.RunTimeInSecs * 1000;


        foreach (var (m, device) in MidiConnectionManager.MidiOutsWithDevices)
        {
            if (device.ProductName != deviceName)
                continue;           
                
            try
            {
                MidiEvent midiEvent =null;
                switch (sendMode)
                {
                    case SendModes.Note_WhileTriggered:
                        if (triggerJustActivated)
                        {
                            var noteOnEvent = new NoteOnEvent(0, channel, noteIndex, onColor, durationInMs);
                            midiEvent = noteOnEvent;
                            _offEvent = noteOnEvent.OffEvent;
                        }
                        else if (triggerJustDeactivated)
                        {
                            midiEvent = _offEvent;
                            _offEvent = null;
                        }
                        break;
                        
                    case SendModes.Note_FixedDuration:
                        if (triggerJustActivated)
                        {
                            if(_offEvent != null) 
                            {
                                m.Send(_offEvent.GetAsShortMessage());
                                _offEvent = null;
                            }
                            var noteOnEvent = new NoteOnEvent(0, channel, noteIndex, onColor, durationInMs);
                            midiEvent = noteOnEvent;
                            _lastNoteOnTime = absTime;
                            _offEvent = noteOnEvent.OffEvent;
                        }
                        else if (absTime - _lastNoteOnTime > durationInMs)
                        {
                            midiEvent = _offEvent;
                            _offEvent = null;
                        }
                        break;
                    
                    case SendModes.Note_Toggle:
                        var buttonOnEvent = new NoteOnEvent(0, channel, noteIndex, onColor, durationInMs);
                        
                        if (triggerJustActivated)
                        {
                            Log.Debug("hit");
                            toggledActive ^= true;
                            IsActive.Value = toggledActive;

                            


                            if (toggledActive)
                            {
                                Log.Debug("hit active");

                                midiEvent = buttonOnEvent;
                                _offEvent = buttonOnEvent.OffEvent;
                            }
                            else
                            {
                                if (_offEvent != null)
                                {
                                    Log.Debug("hit off");

                                    midiEvent = _offEvent;
                                    _offEvent = null;
                                }

                            }
                        }

                        break;
                        
                }
                if(midiEvent != null)
                    m.Send(midiEvent.GetAsShortMessage());
                    
                foundDevice = true;
                break;
            }
            catch (Exception e)
            {
                _lastErrorMessage = $"Failed to send midi to {deviceName}: " + e.Message;
                Log.Warning(_lastErrorMessage, this);
            }
                
        }
        
        #endregion
        
        
        _lastErrorMessage = !foundDevice ? $"Can't find MidiDevice {deviceName}" : null;
    }
        
    private double _lastNoteOnTime;

    private static int GetMicrosecondsPerQuarterNoteFromBpm(double bpm)
    {
        var ms = 600000 / bpm;
        return (int)ms;
    }

    private enum SendModes
    {
        Note_FixedDuration,
        Note_WhileTriggered,
        Note_Toggle,
    }
    
    private enum ButtonColors
    {
        undefined,
        Green,
        Green_Blinking,
        Red,
        Red_Blinking,
        Orange,
        Orange_Blinking
    }

    private bool _triggered;

    #region device dropdown
        
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return Device.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != Device.Id)
        {
            yield return "undefined";
            yield break;
        }
            
        foreach (var device in MidiConnectionManager.MidiOutsWithDevices.Values)
        {
            yield return device.ProductName;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        Log.Debug($"Got {selected}", this);
        Device.SetTypedInputValue(selected);
    }
    #endregion
        
    #region Implement statuslevel
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;
    }

    string IStatusProvider.GetStatusMessage()
    {
        return _lastErrorMessage;
    }

    // We don't actually receive midi in this operator, those methods can remain empty, we just want the MIDI connection thread up
    public void MessageReceivedHandler(object sender, MidiInMessageEventArgs msg) {}

    public void ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg) {}

    public void OnSettingsChanged() {}

    private string _lastErrorMessage;
    #endregion
        
    [Input(Guid = "dd076a47-99df-4357-95fe-2d9bcc83afeb")]
    public readonly InputSlot<bool> TriggerSend = new ();        
        
    [Input(Guid = "9756ac4b-2687-4c61-9b05-d9646e3a0be5", MappedType = typeof(SendModes))]
    public readonly InputSlot<int> SendMode = new ();

    [Input(Guid = "051d2472-f3e4-40a0-ad3c-4fad77d1bf74")]
    public readonly InputSlot<string> Device = new ();
        
    [Input(Guid = "53f4dbbc-249d-4043-af11-367cf738e9ee")]
    public readonly InputSlot<int> ChannelNumber = new ();

    [Input(Guid = "6a94579f-5142-407a-9341-d6c4ab6a94fa")]
    public readonly InputSlot<int> NoteNumber = new ();

    [Input(Guid = "0575aa4b-16a6-4bb5-a6ff-01ce8feb68db", MappedType = typeof(ButtonColors))]
    public readonly InputSlot<int> OnColor = new ();
    
    [Input(Guid = "13e66d6a-2383-4691-a4de-06cedae90b5f", MappedType = typeof(ButtonColors))]
    public readonly InputSlot<int> OffColor = new ();

    [Input(Guid = "903019ed-1991-4670-a00a-c5d004b9bcbe")]
    public readonly InputSlot<float> DurationInSecs = new ();

    private NoteEvent _offEvent;
}