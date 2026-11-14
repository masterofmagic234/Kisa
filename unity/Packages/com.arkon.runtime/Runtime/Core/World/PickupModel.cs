using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public interface IPickupActor
    {
        int Id { get; }
        bool Dead { get; }
        bool CombatEnabled { get; }
        int Health { get; }
        int MaximumHealth { get; }
    }

    /// <summary>
    /// Collection policy from topdown/level3_pickup.gd. The owner applies the collected
    /// effect (+35 for a medkit) and removes the visual; this model does not own actors.
    /// </summary>
    public sealed class PickupModel
    {
        public string Kind { get; }
        public bool Consumed { get; private set; }
        public bool Automatic => Kind == "medkit";
        private float _retryTime;
        public event Action<string, int> Collected;

        public PickupModel(string kind) { Kind = kind == "ammo" ? "medkit" : kind ?? ""; }

        public bool Collect(IPickupActor actor, bool lineOfSight)
        {
            if (actor == null || Consumed || actor.Dead || !actor.CombatEnabled || !lineOfSight) return false;
            if (Kind == "medkit" && actor.Health >= actor.MaximumHealth) return false;
            Consumed = true; Collected?.Invoke(Kind, actor.Id); return true;
        }

        /// <summary>Call from active physics ticks with current overlapping actors, not only an enter edge.</summary>
        public bool Tick(float delta, IEnumerable<IPickupActor> overlapping, Func<IPickupActor, bool> lineOfSight)
        {
            if (!Automatic || Consumed) return false;
            if (delta < 0 || !Scalar.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            _retryTime -= delta;
            if (_retryTime > 0) return false;
            _retryTime = .12f;
            foreach (var actor in overlapping)
                if (actor != null && !actor.Dead && actor.CombatEnabled && Collect(actor, lineOfSight(actor))) return true;
            return false;
        }
    }
}
