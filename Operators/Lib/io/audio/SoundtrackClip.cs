using System;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator.Slots;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.audio
{
    [Guid("7f3e4d8a-9b2c-4a1f-8e5d-3c7b2a9d1f6e")]
    internal sealed class SoundtrackClip : Instance<SoundtrackClip>, IPreventingTimeRemap
    {
        [Output(Guid = "b1e9d4f7-3a8c-4b2f-6e5d-1c9a7f3e2b8a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
        public readonly TimeClipSlot<Command> Output = new();

        [Input(Guid = "c4a1d5e2-8f3b-4c6a-9e1d-7b2a5f8c3d4e")]
        public readonly InputSlot<string> AudioFile = new();

        [Input(Guid = "b8e2f1a9-4d7c-4e5b-a2f3-8c6d1e9a4b7f")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "a3f7c1e8-5d2b-4a9f-1c6e-2b8a3f5d7c1e")]
        public readonly InputSlot<bool> Mute = new();

        [Input(Guid = "f2d8a5c1-3e4b-4f6a-8c1d-9e7a2b5f3c6a")]
        public readonly InputSlot<float> Panning = new();

        [Input(Guid = "e1c7b4a2-8f5d-4a3e-2c6f-1a9b8e3d5f2c")]
        public readonly InputSlot<float> Speed = new();

        private Guid _operatorId = Guid.Empty;
        private AudioClipResourceHandle _currentAudioHandle;
        private SoundtrackClipDefinition _currentClip;
        private string _currentFilePath = string.Empty;

        public SoundtrackClip()
        {
            Output.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            if (_operatorId == Guid.Empty)
            {
                _operatorId = AudioPlayerUtils.ComputeInstanceGuid(InstancePath);
            }

            var filePath = AudioFile.GetValue(context);
            var volume = Math.Max(0, Volume.GetValue(context));
            var mute = Mute.GetValue(context);
            Panning.GetValue(context);
            Speed.GetValue(context);

            var timeClip = Output.TimeClip;
            timeClip.UsedForRegionMapping = false;

            if (Playback.Current == null || string.IsNullOrEmpty(filePath))
                return;

            // Ensure clip handle exists
            if (_currentAudioHandle == null || !string.Equals(_currentFilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilePath = filePath;
                _currentClip = new SoundtrackClipDefinition
                               {
                                   Id = _operatorId,
                                   FilePath = filePath,
                                   IsSoundtrack = true,
                                   DiscardAfterUse = false,
                               };
                _currentAudioHandle = new AudioClipResourceHandle(_currentClip, this);
            }

            // Set clip bounds and volume every frame, identical to how the global soundtrack works.
            // StartTime is in bars - UpdateSoundtrackTime() uses SecondsFromBars(StartTime) internally.
            var clipStart = Math.Min(timeClip.TimeRange.Start, timeClip.TimeRange.End);
            var clipEnd = Math.Max(timeClip.TimeRange.Start, timeClip.TimeRange.End);
            _currentClip.StartTime = clipStart;
            _currentClip.EndTime = clipEnd;
            _currentClip.Volume = mute ? 0 : volume;

            // Pass global TimeInSecs as TargetTime, exactly like the global soundtrack does.
            // UpdateSoundtrackTime() computes: localTargetTimeInSecs = TargetTime - SecondsFromBars(clip.StartTime)
            // and handles all pause/unpause/resync/speed logic internally.
            AudioEngine.UseSoundtrackClip(_currentAudioHandle, Playback.Current.TimeInSecs);
        }

        ~SoundtrackClip()
        {
            // Clean up when operator is destroyed
            if (_operatorId != Guid.Empty)
            {
                // The soundtrack clip will be automatically cleaned up by the audio engine
                // when it's no longer in use (DiscardAfterUse is false, so it will persist)
            }
        }
    }
}
