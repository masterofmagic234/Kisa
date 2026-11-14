using System;

namespace Arkon.Core
{
    // Port of core/pause_manager.gd. The Unity adapter also pauses audio.
    public sealed class PauseModel
    {
        public bool IsPaused { get; private set; }
        public event Action<bool> Changed;
        public void SetPaused(bool value)
        {
            if (value == IsPaused) return;
            IsPaused = value; Changed?.Invoke(value);
        }
        public void Toggle() => SetPaused(!IsPaused);
        public void Resume() => SetPaused(false);
    }

    // Source startup_gate.gd counts observed foreground time, not a blocked call.
    public sealed class ForegroundGate
    {
        public long LastMicroseconds { get; private set; }
        public long ElapsedMicroseconds { get; private set; }
        public int Generation { get; private set; }
        public void Reset(long now)
        {
            LastMicroseconds = now; ElapsedMicroseconds = 0; Generation++;
        }
        public bool Observe(long now, bool active, double timeoutSeconds)
        {
            var step = Math.Max(0, Math.Min(250000, now - LastMicroseconds));
            LastMicroseconds = now;
            if (active) ElapsedMicroseconds += step;
            return ElapsedMicroseconds > timeoutSeconds * 1000000;
        }
    }
}
