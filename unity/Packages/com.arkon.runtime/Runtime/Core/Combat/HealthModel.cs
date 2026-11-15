using System;

namespace Arkon.Core
{
    /// <summary>Detached serialization DTO; restoring validates against the receiving actor's configured limits.</summary>
    [Serializable]
    public sealed class HealthState
    {
        public int maximum, current;
        public bool dead;
        public float invulnerabilityRemaining;
    }

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

        public HealthState CaptureState() => new HealthState
        {
            maximum = Maximum, current = Current, dead = Dead, invulnerabilityRemaining = InvulnerabilityRemaining
        };

        public bool TryRestoreState(HealthState state) => TryRestoreState(state, out _);

        /// <summary>Commit exact health without synthesizing damage, death or a new invulnerability window.</summary>
        public bool TryRestoreState(HealthState state, out string error)
        {
            error = "";
            if (state == null) { error = "Missing health state."; return false; }
            if (state.maximum != Maximum || state.current < 0 || state.current > Maximum)
            { error = "Health state does not match the actor's configured maximum."; return false; }
            if (state.dead != (state.current == 0)) { error = "Health state has contradictory life/death fields."; return false; }
            if (!Scalar.IsFinite(state.invulnerabilityRemaining) || state.invulnerabilityRemaining < 0 || state.invulnerabilityRemaining > InvulnerabilityDuration)
            { error = "Health invulnerability time is outside the configured range."; return false; }
            Current = state.current; Dead = state.dead; InvulnerabilityRemaining = state.invulnerabilityRemaining;
            Changed?.Invoke(Current, Maximum);
            return true;
        }
    }
}
