using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    public enum ProjectileAppearance { Default, PlayerRound, PlayerPellet, EnemyRound, ThrownItem }

    /// <summary>Read-only presentation data from the same lease used for native physics.</summary>
    public readonly struct ProjectileView
    {
        public readonly long Token;
        public readonly ProjectileAppearance Appearance;
        public readonly Vec2 Position, Target, Direction;
        public readonly float Age;

        internal ProjectileView(PoolLease<ProjectileModel> lease, ProjectileAppearance appearance)
        {
            Token = lease.Token;
            Appearance = appearance;
            Position = lease.Item.Position;
            Target = lease.Item.Target;
            Direction = lease.Item.Direction;
            Age = lease.Item.Elapsed;
        }
    }

    [RequireComponent(typeof(Physics2DPort))]
    public sealed class ProjectilePool2D : MonoBehaviour
    {
        private Physics2DPort world;
        private LeasePool<ProjectileModel> pool;
        private readonly List<PoolLease<ProjectileModel>> frame = new List<PoolLease<ProjectileModel>>(64);
        private readonly List<PoolLease<ProjectileModel>> presentation = new List<PoolLease<ProjectileModel>>(64);
        private readonly Dictionary<long, ProjectileAppearance> appearances = new Dictionary<long, ProjectileAppearance>(64);

        public event Action<ProjectileView> ProjectileSpawned;
        public event Action<ProjectileView, ProjectileImpact> ProjectileImpacted;
        private void Awake()
        {
            world = GetComponent<Physics2DPort>();
            pool = new LeasePool<ProjectileModel>(() => new ProjectileModel(), 24, 64);
        }
        public bool Spawn(Vec2 start, Vec2 target, float speed, int mask, int actorId,
            int directDamage, Action<ProjectileImpact> callback, ProjectileAppearance appearance = ProjectileAppearance.Default)
        {
            if (!isActiveAndEnabled || pool == null || !world || !world.isActiveAndEnabled) return false;
            var lease = pool.Acquire();
            if (!lease.Valid) return false;
            appearances.Add(lease.Token, appearance);
            lease.Item.Setup(start, target, speed, impact =>
            {
                var view = new ProjectileView(lease, appearance);
                callback?.Invoke(impact);
                if (isActiveAndEnabled && pool.IsCurrent(lease)) ProjectileImpacted?.Invoke(view, impact);
            }, true, mask, actorId, directDamage, _ =>
            {
                appearances.Remove(lease.Token);
                pool.Release(lease);
            });
            ProjectileSpawned?.Invoke(new ProjectileView(lease, appearance));
            return true;
        }

        public void CopyVisible(List<ProjectileView> target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.Clear();
            if (!isActiveAndEnabled || pool == null) return;
            pool.Snapshot(presentation);
            foreach (var lease in presentation)
                if (lease.Item.Active && appearances.TryGetValue(lease.Token, out var appearance))
                    target.Add(new ProjectileView(lease, appearance));
        }
        private void FixedUpdate()
        {
            pool.Snapshot(frame);
            foreach (var lease in frame)
                if (isActiveAndEnabled && pool.IsCurrent(lease)) lease.Item.Tick(Time.fixedDeltaTime, world);
        }
        private void OnDisable()
        {
            appearances.Clear();
            pool?.ReleaseAll();
        }
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            if (pool == null) return;
            var visible = new List<PoolLease<ProjectileModel>>(); pool.Snapshot(visible);
            foreach (var lease in visible) Gizmos.DrawSphere(ArkonCoordinates.ToUnity(lease.Item.Position), .015f);
        }
    }
}
