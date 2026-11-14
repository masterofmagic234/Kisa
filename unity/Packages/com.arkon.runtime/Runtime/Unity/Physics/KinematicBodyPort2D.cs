using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class KinematicBodyPort2D : MonoBehaviour
    {
        private Rigidbody2D body;
        private CircleCollider2D shape;
        private readonly List<RaycastHit2D> hits = new List<RaycastHit2D>(16);
        public float RadiusPixels = 7;
        public float SkinPixels = .1f;
        private void Awake()
        {
            body = GetComponent<Rigidbody2D>(); shape = GetComponent<CircleCollider2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            shape.radius = RadiusPixels / ArkonCoordinates.PixelsPerUnit;
            gameObject.layer = ArkonCoordinates.BodiesLayer;
        }
        public Vec2 Position => ArkonCoordinates.ToSource(body.position);
        public Vec2 Move(Vec2 velocity, float delta)
        {
            if (delta <= 0) return Vec2.Zero;
            var initial = body.position; var position = initial;
            var resolvedVelocity = ArkonCoordinates.ToUnity(velocity);
            var remaining = ArkonCoordinates.ToUnity(velocity) * delta;
            var filter = new ContactFilter2D { useLayerMask = true, useTriggers = false,
                layerMask = (1 << ArkonCoordinates.WallsLayer) | (1 << ArkonCoordinates.BodiesLayer) };
            var skin = SkinPixels / ArkonCoordinates.PixelsPerUnit;
            for (var slide = 0; slide < 4 && remaining.sqrMagnitude > 1e-12f; slide++)
            {
                body.position = position; Physics2D.SyncTransforms(); hits.Clear();
                var distance = remaining.magnitude;
                shape.Cast(remaining / distance, filter, hits, distance + skin, true);
                var nearest = float.PositiveInfinity; var normal = Vector2.zero;
                foreach (var hit in hits)
                {
                    if (!hit.collider || hit.collider.attachedRigidbody == body || hit.collider.gameObject.scene != gameObject.scene) continue;
                    if (hit.distance < nearest) { nearest = hit.distance; normal = hit.normal; }
                }
                if (float.IsPositiveInfinity(nearest)) { position += remaining; remaining = Vector2.zero; break; }
                var travelled = Mathf.Clamp(nearest - skin, 0, distance);
                position += remaining / distance * travelled;
                remaining *= 1 - travelled / distance;
                var inward = Vector2.Dot(remaining, normal);
                if (inward < 0)
                {
                    remaining -= normal * inward;
                    var velocityIntoWall = Vector2.Dot(resolvedVelocity, normal);
                    if (velocityIntoWall < 0) resolvedVelocity -= normal * velocityIntoWall;
                }
                else break;
            }
            body.position = position;
            // Godot velocity is collision-projected; displacement/delta would
            // instead be get_real_velocity and changes subsequent acceleration.
            return ArkonCoordinates.ToSource(resolvedVelocity);
        }
    }
}
