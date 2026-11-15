using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using WhiteGuard.Diagnostics;

namespace WhiteGuard.Editor
{
    /// <summary>Run without -quit. The command enters PlayMode, waits for its native probe and exits batch mode itself.</summary>
    [InitializeOnLoad]
    public static class CampaignGameChecks
    {
        private const string Prefix = "WhiteGuard.CampaignStartup.";
        private const string Active = Prefix + "Active", Finishing = Prefix + "Finishing", Result = Prefix + "Result";
        private const string Report = Prefix + "Report", Deadline = Prefix + "Deadline";
        private const string OriginalTimeScale = Prefix + "OriginalTimeScale";

        static CampaignGameChecks()
        {
            EditorApplication.playModeStateChanged -= StateChanged; EditorApplication.playModeStateChanged += StateChanged;
            EditorApplication.update -= Watchdog; EditorApplication.update += Watchdog;
        }

        [MenuItem("White Guard/Run Native Campaign Map Startup Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Active, false))
                throw new InvalidOperationException("Start the campaign map startup probe once from EditMode.");
            WhiteGuardGameBuild.ValidateData();
            SessionState.SetBool(Active, true); SessionState.SetBool(Finishing, false); SessionState.SetBool(Result, false);
            SessionState.SetFloat(OriginalTimeScale, Time.timeScale);
            SessionState.SetString(Report, FailureJson("PlayMode exited before the campaign probe completed."));
            SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 300);
            EditorApplication.isPlaying = true;
        }

        private static void StateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 300);
                var host = new GameObject("White Guard native campaign map probe") { hideFlags = HideFlags.HideAndDontSave };
                host.AddComponent<CampaignGameProbe>().Completed = Complete;
            }
            else if (state == PlayModeStateChange.EnteredEditMode) Finish();
        }

        private static void Complete(bool success, string report)
        {
            if (!SessionState.GetBool(Active, false) || SessionState.GetBool(Finishing, false)) return;
            SessionState.SetBool(Finishing, true); SessionState.SetBool(Result, success); SessionState.SetString(Report, report);
            EditorApplication.isPlaying = false;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
        }

        private static void Watchdog()
        {
            if (SessionState.GetBool(Active, false) && !SessionState.GetBool(Finishing, false)
                && EditorApplication.timeSinceStartup > SessionState.GetFloat(Deadline, float.PositiveInfinity))
                Complete(false, FailureJson("Native campaign map startup probe timed out."));
        }

        private static void Finish()
        {
            if (!SessionState.GetBool(Active, false)) return;
            var success = SessionState.GetBool(Result, false); var report = SessionState.GetString(Report, FailureJson("No native result."));
            SessionState.SetBool(Active, false); SessionState.SetBool(Finishing, false);
            Time.timeScale = SessionState.GetFloat(OriginalTimeScale, 1);
            try
            {
                var configured = Environment.GetEnvironmentVariable("WHITEGUARD_NATIVE_REPORT");
                var project = Directory.GetParent(Application.dataPath).FullName;
                var path = Path.GetFullPath(Path.Combine(project, string.IsNullOrWhiteSpace(configured) ? "Logs/CampaignMapStartup.json" : configured));
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, report + Environment.NewLine);
            }
            catch (Exception error) { success = false; Debug.LogException(error); }
            if (success) Debug.Log("WHITE GUARD NATIVE MAP STARTUP CHECK PASSED. Full gameplay completion and audiovisual review remain separate.");
            else Debug.LogError("WHITE GUARD NATIVE MAP STARTUP CHECK FAILED.");
            Debug.Log(report);
            if (Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        [Serializable] private sealed class FailureReport
        {
            public string status = "FAIL_MAP_STARTUP", scope = "Campaign map startup/lifetime probe; not a full playthrough", failure = "";
            public bool fullCampaignCompleted = false;
        }
        private static string FailureJson(string message) => JsonUtility.ToJson(new FailureReport { failure = message }, true);
    }
}
