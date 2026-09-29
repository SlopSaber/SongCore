using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using SiraUtil.Submissions;
using Zenject;

namespace SongCore.Hooks
{
    internal class DisableSubmissionHooks : IInitializable, IDisposable
    {
        private readonly Submission _submission;

        private Hook _autoplayHook = null!;
        private Hook _playbackHook = null!;

        private Ticket? _ticket;

        private DisableSubmissionHooks(Submission submission)
        {
            _submission = submission;
        }

        public void Initialize()
        {
            _autoplayHook = new Hook(typeof(RecPlayBehaviour).GetMethod(nameof(RecPlayBehaviour.Play), BindingFlags.Instance | BindingFlags.NonPublic)!, AutoplayCheck, true);
            _playbackHook = new Hook(typeof(ObjectsMovementRecorder).GetMethod(nameof(ObjectsMovementRecorder.Init), BindingFlags.Instance | BindingFlags.Public)!, PlaybackCheck, true);
        }

        public void Dispose()
        {
            _autoplayHook.Dispose();
            _playbackHook.Dispose();
        }

        private void AutoplayCheck(Action<RecPlayBehaviour> original, RecPlayBehaviour instance)
        {
            original(instance);
            _ticket ??= _submission.DisableScoreSubmission(nameof(SongCore), "Autoplay is enabled.");
        }

        private void PlaybackCheck(Action<ObjectsMovementRecorder> original, ObjectsMovementRecorder instance)
        {
            original(instance);
            if (_ticket == null && instance._mode == ObjectsMovementRecorder.Mode.Playback)
            {
                _ticket = _submission.DisableScoreSubmission(nameof(SongCore), "Playback is enabled.");
            }
        }
    }
}
