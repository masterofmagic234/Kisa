using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    [RequireComponent(typeof(Physics2DPort))]
    public sealed class ProjectilePool2D : MonoBehaviour
    {
        private Physics2DPort world;
        private LeasePool<ProjectileModel> pool;
        private readonly List<PoolLease<ProjectileModel>> frame = new List<PoolLease<ProjectileModel>>(64);
        private void Awake()
        {
            world = GetComponent<Physics2DPort>();
            pool = new LeasePool<ProjectileModel>(() => new ProjectileModel(), 24, 64);
        }
        public bool Spawn(Vec2 start, Vec2 target, float speed, int mask, int actorId,
            int directDamage, Action<ProjectileImpact> callback)
        {
            if (!isActiveAndEnabled || pool == null || !world || !world.isActiveAndEnabled) return false;
            var lease = pool.Acquire();
            if (!lease.Valid) return false;
            lease.Item.Setup(start, target, speed, callback, true, mask, actorId, directDamage, _ => pool.Release(lease));
            return true;
        }
        private void FixedUpdate()
        {
            pool.Snapshot(frame);
            foreach (var lease in frame)
                if (isActiveAndEnabled && pool.IsCurrent(lease)) lease.Item.Tick(Time.fixedDeltaTime, world);
        }
        private void OnDisable() { pool?.ReleaseAll(); }
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            if (pool == null) return;
            var visible = new List<PoolLease<ProjectileModel>>(); pool.Snapshot(visible);
            foreach (var lease in visible) Gizmos.DrawSphere(ArkonCoordinates.ToUnity(lease.Item.Position), .015f);
        }
    }
}
