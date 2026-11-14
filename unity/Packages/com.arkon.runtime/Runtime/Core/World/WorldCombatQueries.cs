using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public readonly struct CombatTarget
    {
        public readonly int Id;
        public readonly Vec2 Position;
        public readonly EnemyState State;
        public CombatTarget(int id, Vec2 position, EnemyState state) { Id = id; Position = position; State = state; }
    }

    /// <summary>Source-world target policies; overlap candidates and wall sweeps are supplied by the physics adapter.</summary>
    public static class WorldCombatQueries
    {
        public const float LayoutScale = 1.5f;
        public const float BottleRange = 190, BottleSpeed = 360, BottleStun = 3.8f;
        public const float EnemyBulletRange = 900, EnemyBulletSpeed = 380;
        public const int EnemyBulletDamage = 8, MedkitHealing = 35;

        public static Vec2 SafeMuzzle(Vec2 center, Vec2 direction, float extension, IProjectileWorld world)
        {
            var normal = direction.Normalized; var end = center + normal * extension;
            return world.Sweep(center, end, SourceCollisionMasks.Walls, 0, out var hit) ? hit.Position - normal : end;
        }

        public static Vec2 ThrowEndpoint(Vec2 origin, Vec2 direction, IProjectileWorld world)
        {
            var end = origin + direction.Normalized * BottleRange;
            return world.Sweep(origin, end, SourceCollisionMasks.Walls, 0, out var hit) ? hit.Position : end;
        }

        /// <summary>Supply the actual physics overlap of radius72 at origin+direction*14, capped at16 as in the source.</summary>
        public static CombatTarget? SelectBatTarget(Vec2 playerPosition, Vec2 direction, IReadOnlyList<CombatTarget> overlapCandidates, Func<Vec2, Vec2, bool> lineOfSight)
        {
            CombatTarget? selected = null; var best = float.PositiveInfinity;
            foreach (var target in overlapCandidates)
            {
                if (target.State == EnemyState.Dead || !InCone(direction, target.Position - playerPosition, 70)) continue;
                if (!lineOfSight(playerPosition, target.Position)) continue;
                var distance = Vec2.Distance(playerPosition, target.Position);
                if (distance < best) { best = distance; selected = target; }
            }
            return selected;
        }

        public static CombatTarget? SelectExecutionTarget(Vec2 playerPosition, Vec2 aim, float weaponActionRange,
            IReadOnlyList<CombatTarget> targets, Func<Vec2, Vec2, bool> lineOfSight, bool combatEnabled = true)
        {
            if (!combatEnabled) return null;
            CombatTarget? selected = null; var best = weaponActionRange * LayoutScale;
            foreach (var target in targets)
            {
                var distance = Vec2.Distance(playerPosition, target.Position);
                if (target.State != EnemyState.Stunned || distance > best || !InCone(aim, target.Position - playerPosition, 75)) continue;
                if (!lineOfSight(playerPosition, target.Position)) continue;
                best = distance; selected = target;
            }
            return selected;
        }

        public static CombatTarget? SelectThrowTarget(Vec2 origin, Vec2 direction, IReadOnlyList<CombatTarget> targets, Func<Vec2, Vec2, bool> lineOfSight)
        {
            CombatTarget? selected = null; var best = BottleRange;
            foreach (var target in targets)
            {
                var distance = Vec2.Distance(origin, target.Position);
                if (target.State == EnemyState.Dead || distance > best || !InCone(direction, target.Position - origin, 34)) continue;
                if (!lineOfSight(origin, target.Position)) continue;
                best = distance; selected = target;
            }
            return selected;
        }

        private static bool InCone(Vec2 direction, Vec2 offset, float degrees)
        {
            var angle = (float)Math.Atan2(direction.X * offset.Y - direction.Y * offset.X, Vec2.Dot(direction, offset));
            return Math.Abs(angle) <= degrees * (float)Math.PI / 180;
        }
    }
}
