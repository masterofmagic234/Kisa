using System;

namespace Arkon.Core
{
    /// <summary>
    /// Pure state port of white_guard/addons/arkon_runtime/combat/health_component.gd.
    /// The owner supplies game-time ticks and translates integer source IDs to actors.
    /// </summary>
    public sealed class HealthModel
    {
        public int Current { get; private set; }
        public int Maximum { get; private set; }
        public bool Dead { get; private set; }
        public float InvulnerabilityRemaining { get; private set; }
        public float InvulnerabilityDuration { get; }
        public bool CanTakeDamage => !Dead && InvulnerabilityRemaining <= 0;

        public event Action<int, int> Changed;
        public event Action<int, int> Damaged;
        public event Action Death;

        public HealthModel(int max = 100, float invul = .24f)
        {
            if (!Scalar.IsFinite(invul)) throw new ArgumentOutOfRangeException(nameof(invul));
            InvulnerabilityDuration = Math.Max(0, invul);
            Reset(max);
        }

        public void Tick(float delta)
        {
            if (!Scalar.IsFinite(delta) || delta <= 0) return;
            InvulnerabilityRemaining = Math.Max(0, InvulnerabilityRemaining - delta);
        }

        public void Reset(int? maximum = null)
        {
            Maximum = Math.Max(maximum ?? Maximum, 1);
            Current = Maximum;
            InvulnerabilityRemaining = 0;
            Dead = false;
            Changed?.Invoke(Current, Maximum);
        }

        public bool ApplyDamage(int amount, int source = 0)
        {
            if (!CanTakeDamage || amount <= 0) return false;
            InvulnerabilityRemaining = InvulnerabilityDuration;
            Current = Math.Max(Current - amount, 0);
            Changed?.Invoke(Current, Maximum);
            Damaged?.Invoke(amount, source);
            // Source health/damage events precede the death transition. An
            // observer may already have forced death during those callbacks.
            if (Current <= 0 && !Dead)
            {
                Dead = true;
                Death?.Invoke();
            }
            return true;
        }

        public bool ForceKill()
        {
            if (Dead) return false;
            Current = 0;
            Dead = true;
            Changed?.Invoke(Current, Maximum);
            Death?.Invoke();
            return true;
        }

        public void Heal(int amount)
        {
            if (Dead || amount <= 0) return;
            var next = (int)Math.Min((long)Current + amount, Maximum);
            if (next == Current) return;
            Current = next;
            Changed?.Invoke(Current, Maximum);
        }
    }
}
