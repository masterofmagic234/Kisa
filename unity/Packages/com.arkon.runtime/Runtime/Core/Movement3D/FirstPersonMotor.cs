using System;

namespace Arkon.Core
{
    /// <summary>Pure public math/movement_math.gd, including its deliberate flattening after basis multiplication.</summary>
    public static class FirstPersonMovementMath
    {
        public static MotionVector3 VelocityForInput(MotionVector3 basisX, MotionVector3 basisZ, Vec2 sourceAxis, float walkSpeed)
        {
            var forward = -sourceAxis.Y;
            var velocity = (-basisZ * forward + basisX * sourceAxis.X) * walkSpeed;
            return new MotionVector3(velocity.X, 0, velocity.Z);
        }
    }

    /// <summary>
    /// Movement/look policy from the already-public locomotion/first_person_actor.gd.
    /// Source Input.get_vector supplies a normalized/deadzoned axis; the adapter owns that input conversion.
    /// No private Level1 mission, dash, portrait, weapon catalog or heist code is included.
    /// </summary>
    public sealed class FirstPersonMotor
    {
        public const float Gravity = 18, GroundedPull = -.5f, MaximumPitch = 1.4f;
        public float WalkSpeed = 4;
        public float LookSensitivity = .0028f;
        public bool InputEnabled = true, Dead, TransitionBlocked;
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public MotionVector3 Velocity { get; private set; }
        public bool CanMove => InputEnabled && !Dead && !TransitionBlocked;
        public bool ApplyLookPixels(Vec2 relativePixels, bool pointerCaptured)
        {
            if (!InputEnabled || Dead || !pointerCaptured) return false;
            Yaw -= relativePixels.X * LookSensitivity;
            Pitch = Scalar.Clamp(Pitch - relativePixels.Y * LookSensitivity, -MaximumPitch, MaximumPitch);
            return true;
        }
        public void SetOrientation(float sourceYaw, float sourcePitch)
        { Yaw = sourceYaw; Pitch = Scalar.Clamp(sourcePitch, -MaximumPitch, MaximumPitch); }
        public bool PrepareMovement(Vec2 sourceAxis, bool grounded, float delta)
        {
            if (!CanMove || !Scalar.IsFinite(delta) || delta < 0) return false;
            var c = (float)Math.Cos(Yaw); var s = (float)Math.Sin(Yaw);
            var horizontal = FirstPersonMovementMath.VelocityForInput(new MotionVector3(c, 0, -s), new MotionVector3(s, 0, c), sourceAxis, WalkSpeed);
            Velocity = new MotionVector3(horizontal.X, grounded ? GroundedPull : Velocity.Y - Gravity * delta, horizontal.Z);
            return true;
        }
        public void SetVerticalVelocity(float value) { Velocity = new MotionVector3(Velocity.X, value, Velocity.Z); }
    }
}
