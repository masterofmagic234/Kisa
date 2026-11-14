using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>
    /// Native Unity capsule-query seam for the public grounded-step policy.
    /// PhysX casts and Godot body_test_motion have different contact recovery;
    /// metadata compilation does not prove native stair equivalence.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class CapsuleBodySweep3D : MonoBehaviour, IBodySweep3D
    {
        public LayerMask SolidLayers = ~0;
        public float SourceSafeMargin = .001f;
        private CharacterController controller;
        public CharacterController Controller => controller ? controller : (controller = GetComponent<CharacterController>());
        public static MotionVector3 ToSource(Vector3 value) => new MotionVector3(value.x, value.y, -value.z);
        public static Vector3 ToUnity(MotionVector3 value) => new Vector3(value.X, value.Y, -value.Z);

        public MotionProbe3D TestMotion(MotionVector3 from, MotionVector3 motion, float safeMargin, int maximumCollisions)
        {
            var movement = ToUnity(motion); var distance = movement.magnitude;
            if (distance <= 0) return new MotionProbe3D(false, motion);
            var body = Controller; var scale = transform.lossyScale; var up = transform.up.normalized;
            var radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var height = Mathf.Max(body.height * Mathf.Abs(scale.y), radius * 2);
            var halfLine = Mathf.Max(0, height * .5f - radius);
            var center = ToUnity(from) + transform.TransformVector(body.center);
            var margin = Mathf.Max(0, safeMargin);
            // A tiny query inset avoids a starting floor overlap masquerading as a horizontal wall.
            var queryRadius = Mathf.Max(.0001f, radius - margin);
            var hits = Physics.CapsuleCastAll(center - up * halfLine, center + up * halfLine, queryRadius,
                movement / distance, distance + margin, SolidLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var nearest = float.PositiveInfinity;
            var normals = new List<MotionVector3>();
            foreach (var hit in hits)
            {
                if (!hit.collider || hit.collider == body || hit.collider.transform.IsChildOf(transform)) continue;
                if (float.IsPositiveInfinity(nearest)) nearest = hit.distance;
                // Only the nearest contact manifold, not a distant floor behind a blocking wall.
                if (hit.distance > nearest + Mathf.Max(margin * 2, .0005f)) break;
                if (normals.Count < Mathf.Max(1, maximumCollisions)) normals.Add(ToSource(hit.normal));
            }
            if (float.IsPositiveInfinity(nearest)) return new MotionProbe3D(false, motion);
            var travel = Mathf.Min(distance, Mathf.Max(0, nearest - margin));
            return new MotionProbe3D(true, motion * (travel / distance), normals.ToArray());
        }
    }
}
