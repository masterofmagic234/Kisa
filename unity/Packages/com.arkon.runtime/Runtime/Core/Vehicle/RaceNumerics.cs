using System;

namespace Arkon.Core
{
    /// <summary>Race-space vector: +X right, +Y up, +Z forward. No Unity coordinate conversion.</summary>
    public readonly struct RaceVec3 : IEquatable<RaceVec3>
    {
        public readonly float X, Y, Z;
        public RaceVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static RaceVec3 Zero => new RaceVec3(0, 0, 0);
        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public RaceVec3 Normalized => Length > 0 ? this / Length : Zero;
        public static float Dot(RaceVec3 a, RaceVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static RaceVec3 Cross(RaceVec3 a, RaceVec3 b) => new RaceVec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public static RaceVec3 Lerp(RaceVec3 a, RaceVec3 b, float t) => a + (b - a) * t;
        public static RaceVec3 operator +(RaceVec3 a, RaceVec3 b) => new RaceVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static RaceVec3 operator -(RaceVec3 a, RaceVec3 b) => new RaceVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static RaceVec3 operator *(RaceVec3 a, float b) => new RaceVec3(a.X * b, a.Y * b, a.Z * b);
        public static RaceVec3 operator /(RaceVec3 a, float b) => new RaceVec3(a.X / b, a.Y / b, a.Z / b);
        public bool Equals(RaceVec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        public override bool Equals(object obj) => obj is RaceVec3 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode());
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public readonly struct RaceRect
    {
        public readonly Vec2 Position, Size;
        public Vec2 End => Position + Size;
        public RaceRect(Vec2 position, Vec2 size) { Position = position; Size = size; }
    }

    internal static class RaceNumerics
    {
        internal const float Tau = (float)(Math.PI * 2);
        internal static float Lerp(float a, float b, float t) => a + (b - a) * t;
        internal static float SmoothStep(float from, float to, float value)
        {
            var t = Scalar.Clamp((value - from) / (to - from), 0, 1);
            return t * t * (3 - 2 * t);
        }
        internal static int PosMod(int value, int divisor) => ((value % divisor) + divisor) % divisor;
        internal static float PosMod(float value, float divisor) => ((value % divisor) + divisor) % divisor;
    }
}
