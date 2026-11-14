using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public sealed class MotionProbe3D
    {
        public readonly bool Collided;
        public readonly MotionVector3 Travel;
        public readonly IReadOnlyList<MotionVector3> Normals;
        public MotionProbe3D(bool collided, MotionVector3 travel, params MotionVector3[] normals)
        { Collided = collided; Travel = travel; Normals = Array.AsReadOnly((MotionVector3[])(normals ?? Array.Empty<MotionVector3>()).Clone()); }
    }
    public interface IBodySweep3D
    {
        MotionProbe3D TestMotion(MotionVector3 from, MotionVector3 motion, float safeMargin, int maximumCollisions);
    }
    public readonly struct StepPlan3D
    {
        public readonly MotionVector3 Up, Forward, Down;
        public float Rise(MotionVector3 upDirection) => MotionVector3.Dot(Up + Down, upDirection);
        public StepPlan3D(MotionVector3 up, MotionVector3 forward, MotionVector3 down) { Up = up; Forward = forward; Down = down; }
    }

    /// <summary>
    /// Exact decision sequence from public locomotion/grounded_step_motion.gd.
    /// An engine owner executes every accepted displacement with collision recovery; this planner never assigns transforms.
    /// </summary>
    public static class GroundedStepMotion3D
    {
        public const float Height = .20f, LandingMargin = .012f;
        public const float MinimumRise = .002f, MaximumRise = Height + .001f;
        public static bool TryPlan(IBodySweep3D body, MotionVector3 origin, MotionVector3 horizontal,
            MotionVector3 upDirection, bool grounded, float verticalVelocity, float maximumFloorAngle, float safeMargin, out StepPlan3D plan)
        {
            plan = default;
            if (!grounded || verticalVelocity > 0 || horizontal.LengthSquared <= .000001f) return false;
            var floorDot = (float)Math.Cos(maximumFloorAngle);
            var ordinary = body.TestMotion(origin, horizontal, safeMargin, 4);
            if (!ordinary.Collided) return false;
            var blocked = false;
            foreach (var normal in ordinary.Normals) if (MotionVector3.Dot(normal, upDirection) < floorDot) blocked = true;
            if (!blocked) return false;
            var direction = horizontal.Normalized;
            var ordinaryProgress = MotionVector3.Dot(ordinary.Travel, direction);
            var up = upDirection * Height;
            if (body.TestMotion(origin, up, safeMargin, 4).Collided) return false;
            var forward = body.TestMotion(origin + up, horizontal, safeMargin, 4).Travel;
            if (MotionVector3.Dot(forward, direction) < ordinaryProgress + .0005f) return false;
            var landing = body.TestMotion(origin + up + forward, -upDirection * (Height + LandingMargin), safeMargin, 4);
            if (!landing.Collided) return false;
            var walkable = false;
            foreach (var normal in landing.Normals) if (MotionVector3.Dot(normal, upDirection) >= floorDot) walkable = true;
            if (!walkable) return false;
            var rise = MotionVector3.Dot(up + landing.Travel, upDirection);
            if (rise < MinimumRise || rise > MaximumRise) return false;
            plan = new StepPlan3D(up, forward, landing.Travel);
            return true;
        }
    }
}
