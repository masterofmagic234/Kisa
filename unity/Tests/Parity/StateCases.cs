using System;
using System.IO;
using Arkon.Core;

public static class StateCases
{
    public static void Run(Action<bool,string> check)
    {
        var pause = new PauseModel(); var changed = 0;
        pause.Changed += _ => changed++;
        pause.SetPaused(true); pause.SetPaused(true); pause.Resume(); pause.Resume();
        check(changed == 2 && !pause.IsPaused, "Pause is idempotent.");
        var gate = new ForegroundGate(); gate.Reset(0);
        gate.Observe(10000000, true, 1);
        check(gate.ElapsedMicroseconds == 250000, "Blocked interval counts at most 250ms.");
        gate.Observe(20000000, false, 1);
        check(gate.ElapsedMicroseconds == 250000, "Background time does not consume startup budget.");
        var progress = new ProgressModel(); progress.CurrentLevel = "l3"; progress.MarkCompleted("l1");
        progress.SetFact("intro_seen", true); var before = progress.Capture();
        var invalid = new ProgressDocument { schema = 99, current = "bad" };
        check(!progress.TryApply(invalid, out _) && progress.CurrentLevel == "l3", "Unknown schema leaves live progress unchanged.");
        var invalidFact = new ProgressDocument { current = "bad", facts = new[] { new FactRecord { key = "x", kind = "number", value = "NaN" } } };
        check(!progress.TryApply(invalidFact, out _) && progress.IsCompleted("l1"), "Invalid fact is rejected atomically.");
        var loaded = new ProgressModel();
        check(loaded.TryApply(before, out _) && loaded.IsUnlocked("l1") && (bool)loaded.Facts["intro_seen"], "Progress document round trip.");
        var transitions = new TransitionGate(new[] { "l1", "l2" }); var complete = 0;
        transitions.Completed += _ => complete++;
        check(!transitions.TryBegin("unknown", out _), "Unknown route is rejected.");
        check(transitions.TryBegin("l1", out var a) && !transitions.TryBegin("l2", out _), "Only one transition owns loading.");
        transitions.Cancel(a); transitions.TryBegin("l2", out var b);
        check(!transitions.Complete(a) && transitions.Complete(b) && !transitions.Complete(b) && complete == 1, "Stale and duplicate completion are ignored.");
        var directory = Path.Combine(Path.GetTempPath(), "arkon-parity-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(directory, "progress.json"); AtomicTextFile.Write(file, "first"); AtomicTextFile.Write(file, "second");
            check(File.ReadAllText(file) == "second" && !File.Exists(file + ".tmp"), "Save replacement preserves a complete file.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
