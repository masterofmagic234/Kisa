using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Source masks, not Unity layer indices. An engine adapter must map them explicitly.</summary>
    public static class SourceCollisionMasks
    {
        public const int Walls = 1, Bodies = 2, EnemyHitboxes = 4, PlayerHitboxes = 8;
    }

    public readonly struct ProjectileHit
    {
        public readonly int ColliderId, ActorId;
        public readonly bool DamageableHitbox;
        public readonly Vec2 Position;
        public ProjectileHit(int colliderId, int actorId, Vec2 position, bool damageableHitbox)
        { ColliderId = colliderId; ActorId = actorId; Position = position; DamageableHitbox = damageableHitbox; }
    }

    public readonly struct ProjectileImpact
    {
        public readonly Vec2 Position;
        public readonly bool HasCollider;
        public readonly ProjectileHit Hit;
        public ProjectileImpact(Vec2 position, bool hasCollider = false, ProjectileHit hit = default)
        { Position = position; HasCollider = hasCollider; Hit = hit; }
    }

    public interface IProjectileWorld
    {
        /// <summary>First body/area hit on the entire segment; exclude the ignored actor AND its child hitboxes.</summary>
        bool Sweep(Vec2 from, Vec2 to, int sourceMask, int ignoredActorId, out ProjectileHit hit);
        void DamageHitbox(ProjectileHit hit, int damage, int sourceActorId);
    }

    /// <summary>
    /// Swept travel policy from topdown/level3_projectile.gd. Player shots use directDamage=0;
    /// their impact callback owns damage and the shared shotgun ShotHitCache. Enemy shots use 8.
    /// </summary>
    public sealed class ProjectileModel
    {
        public Vec2 Position { get; private set; }
        public Vec2 Target { get; private set; }
        public Vec2 Direction { get; private set; }
        public float Speed { get; private set; }
        public float Lifetime { get; private set; }
        public float Elapsed { get; private set; }
        public bool Active { get; private set; }
        public bool ImpactStarted { get; private set; }
        public int DirectDamage { get; private set; }
        private bool _collisions;
        private int _mask, _ignoredActor;
        private Action<ProjectileImpact> _impact;
        private Action<ProjectileModel> _recycle;

        public void Setup(Vec2 start, Vec2 target, float speed, Action<ProjectileImpact> impact,
            bool collisions = false, int collisionMask = 0, int ignoredActorId = 0, int directDamage = 0,
            Action<ProjectileModel> recycle = null)
        {
            Position = start; Target = target; Direction = (target - start).Normalized;
            Speed = Math.Max(speed, 1); Lifetime = Math.Max(Vec2.Distance(start, target) / Speed, .08f) + .20f;
            Elapsed = 0; _impact = impact; _recycle = recycle; _collisions = collisions;
            _mask = collisions ? collisionMask : 0; _ignoredActor = ignoredActorId;
            DirectDamage = Math.Max(0, directDamage); ImpactStarted = false; Active = true;
        }

        public void Tick(float delta, IProjectileWorld world)
        {
            if (!Active || ImpactStarted) return;
            if (delta < 0 || !Scalar.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            Elapsed += delta; var remaining = Vec2.Distance(Position, Target);
            if (remaining <= 2 || Elapsed >= Lifetime)
            {
                Resolve(new ProjectileImpact(Position), world); return;
            }
            var next = Position + Direction * Math.Min(Speed * delta, remaining);
            if (_collisions && _mask != 0)
            {
                if (world == null) throw new ArgumentNullException(nameof(world));
                if (world.Sweep(Position, next, _mask, _ignoredActor, out var hit))
                {
                    Position = hit.Position; Resolve(new ProjectileImpact(Position, true, hit), world); return;
                }
            }
            Position = next;
        }

        private void Resolve(ProjectileImpact impact, IProjectileWorld world)
        {
            if (ImpactStarted) return;
            // Mark before external code: callbacks cannot resolve or recycle this impact twice.
            ImpactStarted = true; Active = false;
            var callback = _impact; var recycle = _recycle;
            _impact = null; _recycle = null;
            var source = _ignoredActor; _ignoredActor = 0;
            try
            {
                if (impact.HasCollider && impact.Hit.DamageableHitbox && DirectDamage > 0)
                    world.DamageHitbox(impact.Hit, DirectDamage, source);
                callback?.Invoke(impact);
            }
            finally { recycle?.Invoke(this); }
        }
    }

    /// <summary>One cache per volley, shared by all pellets. Do not create one cache per projectile.</summary>
    public sealed class ShotHitCache
    {
        private readonly HashSet<int> _actors = new HashSet<int>();
        public bool TryHit(int actorId) => _actors.Add(actorId);
        public int Count => _actors.Count;
    }
}
