using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>A frame-local, engine-independent candidate supplied by the owning encounter.</summary>
    public readonly struct EnemyTargetCandidate
    {
        public readonly int ActorId;
        public readonly Vec2 Position;
        public readonly bool Alive;
        public EnemyTargetCandidate(int actorId, Vec2 position, bool alive = true)
        { ActorId = actorId; Position = position; Alive = alive; }
    }

    /// <summary>
    /// Optional encounter policy for multiple allied targets. The original Level3
    /// single-player binding does not call this extension. Visibility wins over
    /// proximity; an explicitly supplied live fallback remains the pursuit target
    /// when nobody is visible. Equal distances retain the encounter's input order.
    /// </summary>
    public static class EnemyTargetSelection
    {
        public static EnemyTargetCandidate? Select(Vec2 origin, IReadOnlyList<EnemyTargetCandidate> candidates,
            Func<Vec2, Vec2, bool> hasLineOfSight, int fallbackActorId = 0)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            EnemyTargetCandidate? visible = null, nearest = null, fallback = null;
            var visibleDistance = float.PositiveInfinity; var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (!candidate.Alive || candidate.ActorId == 0) continue;
                var distance = (candidate.Position - origin).LengthSquared;
                if (!Scalar.IsFinite(distance)) continue;
                if (candidate.ActorId == fallbackActorId) fallback = candidate;
                if (distance < nearestDistance) { nearest = candidate; nearestDistance = distance; }
                if (distance >= visibleDistance || (hasLineOfSight != null && !hasLineOfSight(origin, candidate.Position))) continue;
                visible = candidate; visibleDistance = distance;
            }
            return visible ?? fallback ?? nearest;
        }
    }
}
