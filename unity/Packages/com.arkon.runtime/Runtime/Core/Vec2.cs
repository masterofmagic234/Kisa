using System;

namespace Arkon.Core
{
    /// <summary>Source-space coordinates: pixels, y down, angles in radians.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static Vec2 Zero => new Vec2(0, 0);
        public static Vec2 Right => new Vec2(1, 0);
        public float LengthSquared => X * X + Y * Y;
        public float Length => (float)Math.Sqrt(LengthSquared);
        public Vec2 Normalized => Length > 0.000001f ? this / Length : Zero;
        public Vec2 LimitLength(float maximum = 1)
        {
            return LengthSquared > maximum * maximum ? Normalized * maximum : this;
        }
        public float Angle => (float)Math.Atan2(Y, X);
        public Vec2 Rotated(float radians)
        {
            var c = (float)Math.Cos(radians); var s = (float)Math.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }
        public Vec2 MoveToward(Vec2 target, float delta)
        {
            var difference = target - this;
            return difference.Length <= delta ? target : this + difference.Normalized * delta;
        }
        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => a + (b - a) * t;
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float b) => new Vec2(a.X * b, a.Y * b);
        public static Vec2 operator *(float a, Vec2 b) => b * a;
        public static Vec2 operator /(Vec2 a, float b) => new Vec2(a.X / b, a.Y / b);
        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => $"({X}, {Y})";
    }

    public static class Scalar
    {
        public static float Clamp(float value, float low, float high) => Math.Max(low, Math.Min(high, value));
        public static float MoveToward(float value, float target, float step) => value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
