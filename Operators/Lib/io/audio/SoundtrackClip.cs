using System;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.Logging;
using T3.Core.Operator.Slots;

// ReSharper disable MemberCanBePrivate.Global

namespace Lib.io.audio
{
    [Guid("7f3e4d8a-9b2c-4a1f-8e5d-3c7b2a9d1f6e")]
    internal sealed class SoundtrackClip : Instance<SoundtrackClip>, IPreventingTimeRemap, IUpdateOutsideTimeClipRange
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

        [Input(Guid = "9d3f1e7a-6c2b-4a8f-b5d1-2e7c9a4f6b3d")]
        public readonly InputSlot<bool> AllowManualStretch = new();

        private Guid _operatorId = Guid.Empty;
        private AudioClipResourceHandle _currentAudioHandle;
        private SoundtrackClipDefinition _currentClip;
        private string _currentFilePath = string.Empty;
        private const float MinDurationBars = 0.0001f;

        // Non-stretch trim tracking.
        // LayersArea moves SourceRange.Start along with TimeRange.Start on EVERY drag
        // (body drag AND handle trim), so we can't read SourceRange.Start as a trim offset.
        // Instead we track the trim-in offset ourselves by detecting when the start handle
        // was dragged (TimeRange.Start moved but TimeRange.End didn't) vs body drag (both moved).
        private float _trimInBars;
        private float _lastTimeStart = float.NaN;
        private float _lastTimeEnd = float.NaN;

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
            var allowManualStretch = AllowManualStretch.GetValue(context);
            Panning.GetValue(context);

            var timeClip = Output.TimeClip;
            timeClip.UsedForRegionMapping = false;

            if (Playback.Current == null || string.IsNullOrEmpty(filePath))
                return;

            // Ensure clip handle exists
            var clipWasRecreated = false;
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
                clipWasRecreated = true;
                _trimInBars = 0;
                _lastTimeStart = float.NaN;
                _lastTimeEnd = float.NaN;
            }

            if (_currentClip.LengthInSeconds > 0 && Playback.Current != null)
            {
                try
                {
                    var audioDurationInBars = (float)Playback.Current.BarsFromSeconds(_currentClip.LengthInSeconds);
                    if (!float.IsNaN(audioDurationInBars) && audioDurationInBars > MinDurationBars)
                    {
                        // Initialise new clips to full file duration.
                        if (clipWasRecreated || Math.Abs(timeClip.TimeRange.Duration) <= MinDurationBars)
                        {
                            timeClip.TimeRange.Duration = audioDurationInBars;
                            _trimInBars = 0;
                        }

                        if (!allowManualStretch)
                        {
                            // --- Non-stretch mode ---
                            //
                            // Detect trim vs body-drag by comparing how Start and End moved
                            // since last frame:
                            //   Body drag:        Start moved, End moved by same amount
                            //   Start-handle trim: Start moved, End stayed
                            //   End-handle trim:   End moved, Start stayed
                            //
                            // Only a start-handle trim changes the trim-in offset.

                            if (!float.IsNaN(_lastTimeStart) && !float.IsNaN(_lastTimeEnd))
                            {
                                var dStart = timeClip.TimeRange.Start - _lastTimeStart;
                                var dEnd = timeClip.TimeRange.End - _lastTimeEnd;

                                // Start-handle moved but end didn't (or moved much less) → trim-in
                                if (Math.Abs(dStart) > 0.0001f && Math.Abs(dEnd) < 0.0001f)
                                {
                                    _trimInBars += dStart;
                                }
                                // End-handle moved but start didn't → trim-out (no effect on trim-in)
                                // Body drag: both moved equally → no change to trim-in
                            }

                            // Clamp trim-in to valid range
                            _trimInBars = Math.Clamp(_trimInBars, 0, audioDurationInBars - MinDurationBars);

                            // Clamp clip duration to the remaining audio after trim-in.
                            var maxDuration = audioDurationInBars - _trimInBars;
                            var timeStart = timeClip.TimeRange.Start;
                            var timeEnd = timeClip.TimeRange.End;
                            var duration = timeEnd - timeStart;

                            if (duration > maxDuration)
                                timeEnd = timeStart + maxDuration;

                            if (timeEnd - timeStart < MinDurationBars)
                                timeEnd = timeStart + MinDurationBars;

                            timeClip.TimeRange.Start = timeStart;
                            timeClip.TimeRange.End = timeEnd;

                            // SourceRange mirrors the played audio window exactly (1:1, no stretch).
                            var clampedDuration = timeEnd - timeStart;
                            timeClip.SourceRange.Start = _trimInBars;
                            timeClip.SourceRange.End = _trimInBars + clampedDuration;

                            _currentClip.PlaybackRateMultiplier = 1.0;
                            _currentClip.SourceOffsetInSeconds = Playback.Current.SecondsFromBars(_trimInBars);
                        }
                        else
                        {
                            // --- Stretch mode ---
                            // Lock SourceRange to full file; undo any trim deltas from LayersArea.
                            timeClip.SourceRange.Start = 0;
                            timeClip.SourceRange.End = audioDurationInBars;

                            var visibleDuration = Math.Max(Math.Abs(timeClip.TimeRange.Duration), MinDurationBars);
                            _currentClip.PlaybackRateMultiplier = audioDurationInBars / visibleDuration;
                            _currentClip.SourceOffsetInSeconds = 0;
                        }

                        // Remember this frame's TimeRange for next-frame delta detection.
                        _lastTimeStart = timeClip.TimeRange.Start;
                        _lastTimeEnd = timeClip.TimeRange.End;
                    }
                }
                catch (Exception e)
                {
                    Log.Warning("Failed to apply soundtrack file duration to time clip: " + e.Message);
                    _currentClip.PlaybackRateMultiplier = 1.0;
                    _currentClip.SourceOffsetInSeconds = 0;
                }
            }
            else
            {
                _currentClip.PlaybackRateMultiplier = 1.0;
                _currentClip.SourceOffsetInSeconds = 0;
            }

            // Set clip bounds and volume every frame.
            var clipStart = Math.Min(timeClip.TimeRange.Start, timeClip.TimeRange.End);
            var clipEnd = Math.Max(timeClip.TimeRange.Start, timeClip.TimeRange.End);
            _currentClip.StartTime = clipStart;
            _currentClip.EndTime = clipEnd;
            _currentClip.Volume = mute ? 0 : volume;

            AudioEngine.UseSoundtrackClip(_currentAudioHandle, Playback.Current.TimeInSecs);
        }

        ~SoundtrackClip()
        {
            if (_operatorId != Guid.Empty)
            {
            }
        }
    }
}
