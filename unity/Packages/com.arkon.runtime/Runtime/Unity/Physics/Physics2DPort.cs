using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    // Real Unity queries, not an engine mock. PlayMode verification is required.
    public sealed class Physics2DPort : MonoBehaviour, IProjectileWorld
    {
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(32);
        private readonly Dictionary<int, DamageReceiver2D> receivers = new Dictionary<int, DamageReceiver2D>();
        public bool Sweep(Vec2 from, Vec2 to, int sourceMask, int ignoredActorId, out ProjectileHit result)
        {
            result = default;
            var origin = ArkonCoordinates.ToUnity(from); var finish = ArkonCoordinates.ToUnity(to);
            var difference = finish - origin;
            if (difference.sqrMagnitude < 1e-12f) return false;
            var filter = new ContactFilter2D { useLayerMask = true, useTriggers = true,
                layerMask = ArkonCoordinates.ToUnityMask(sourceMask) };
            hits.Clear();
            Physics2D.Raycast(origin, difference.normalized, filter, hits, difference.magnitude);
            var closest = float.PositiveInfinity; RaycastHit2D chosen = default; var actorId = 0;
            foreach (var candidate in hits)
            {
                if (!candidate.collider || candidate.collider.gameObject.scene != gameObject.scene) continue;
                var identity = candidate.collider.GetComponentInParent<ActorIdentity2D>();
                var receiver = candidate.collider.GetComponentInParent<DamageReceiver2D>();
                var id = identity ? identity.Id : receiver ? receiver.ActorId : 0;
                if (ignoredActorId != 0 && id == ignoredActorId) continue;
                if (candidate.distance >= closest) continue;
                closest = candidate.distance; chosen = candidate; actorId = id;
            }
            if (!chosen.collider) return false;
            var hitReceiver = chosen.collider.GetComponentInParent<DamageReceiver2D>();
            var colliderId = chosen.collider.GetInstanceID();
            if (hitReceiver) receivers[colliderId] = hitReceiver;
            result = new ProjectileHit(colliderId, actorId, ArkonCoordinates.ToSource(chosen.point), hitReceiver);
            return true;
        }
        public bool TryGetReceiver(ProjectileHit hit, out DamageReceiver2D receiver)
        {
            if (receivers.TryGetValue(hit.ColliderId, out receiver) && receiver) return true;
            receivers.Remove(hit.ColliderId); receiver = null; return false;
        }
        public void DamageHitbox(ProjectileHit hit, int damage, int sourceActorId)
        {
            if (TryGetReceiver(hit, out var receiver)) receiver.ApplyDamage(damage, sourceActorId);
        }
    }
}
