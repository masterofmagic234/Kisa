using System;
using Arkon.Unity.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Arkon.Unity.Editor
{
    /// <summary>
    /// Native PlayMode command, NOT executed by API compilation or core tests.
    /// Batch use omits -quit; the command exits after returning to EditMode.
    /// No scenes/assets or Editor Enter Play Mode options are saved or changed.
    /// </summary>
    [InitializeOnLoad]
    public static class PhysicsLifecycleChecks
    {
        private const string Active = "Arkon.Lifecycle.Active";
        private const string Finishing = "Arkon.Lifecycle.Finishing";
        private const string Result = "Arkon.Lifecycle.Result";
        private const string Message = "Arkon.Lifecycle.Message";
        private const string Deadline = "Arkon.Lifecycle.Deadline";

        static PhysicsLifecycleChecks()
        {
            EditorApplication.playModeStateChanged -= StateChanged;
            EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.update -= Watchdog;
            EditorApplication.update += Watchdog;
        }

        [MenuItem("Arkon/Migration/Verify Native 2D Lifecycle")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Active, false))
                throw new InvalidOperationException("Start the lifecycle probe once from EditMode.");
            SessionState.SetBool(Active, true); SessionState.SetBool(Finishing, false);
            SessionState.SetBool(Result, false); SessionState.SetString(Message, "PlayMode exited before completion.");
            SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 120);
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 30);
                var host = new GameObject("Arkon lifecycle regression host");
                host.hideFlags = HideFlags.HideAndDontSave;
                host.AddComponent<PhysicsLifecycleProbe>().Completed = Complete;
            }
            else if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        private static void Complete(bool success, string message)
        {
            if (!SessionState.GetBool(Active, false) || SessionState.GetBool(Finishing, false)) return;
            SessionState.SetBool(Finishing, true); SessionState.SetBool(Result, success);
            SessionState.SetString(Message, message); EditorApplication.isPlaying = false;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }

        private static void Watchdog()
        {
            if (SessionState.GetBool(Active, false) && !SessionState.GetBool(Finishing, false)
                && EditorApplication.timeSinceStartup > SessionState.GetFloat(Deadline, float.PositiveInfinity))
                Complete(false, "Native lifecycle probe timed out.");
        }

        private static void Finish()
        {
            if (!SessionState.GetBool(Active, false)) return;
            var success = SessionState.GetBool(Result, false);
            var message = SessionState.GetString(Message, "Lifecycle result is missing.");
            SessionState.SetBool(Active, false); SessionState.SetBool(Finishing, false);
            if (success) Debug.Log(message); else Debug.LogError(message);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
