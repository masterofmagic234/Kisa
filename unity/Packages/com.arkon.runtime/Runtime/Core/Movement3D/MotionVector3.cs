using System;

namespace Arkon.Core
{
    /// <summary>Public FPS source-space vector: Y up; unrotated forward is -Z. Engine adapters reflect Z explicitly.</summary>
    public readonly struct MotionVector3
    {
        public readonly float X, Y, Z;
        public MotionVector3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static MotionVector3 Zero => new MotionVector3(0, 0, 0);
        public static MotionVector3 Up => new MotionVector3(0, 1, 0);
        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public MotionVector3 Normalized => Length > 0 ? this / Length : Zero;
        public static float Dot(MotionVector3 a, MotionVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static MotionVector3 operator +(MotionVector3 a, MotionVector3 b) => new MotionVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static MotionVector3 operator -(MotionVector3 a, MotionVector3 b) => new MotionVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static MotionVector3 operator -(MotionVector3 value) => new MotionVector3(-value.X, -value.Y, -value.Z);
        public static MotionVector3 operator *(MotionVector3 value, float scalar) => new MotionVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        public static MotionVector3 operator /(MotionVector3 value, float scalar) => new MotionVector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
    }
}
