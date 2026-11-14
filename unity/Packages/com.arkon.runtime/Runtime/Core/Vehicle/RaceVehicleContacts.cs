using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Adapter owns persistent route correction and signed drivetrain policy.</summary>
    public interface IRaceContactBody
    {
        Vec2 Position { get; }
        float HeadingYaw { get; }
        float InverseMass { get; }
        Vec2 ContactVelocity { get; }
        void ResolveContact(Vec2 correction);
        void ApplyContactVelocity(Vec2 value);
    }

    public readonly struct RaceContactPair : IEquatable<RaceContactPair>
    {
        public readonly int First, Second;
        public RaceContactPair(int first, int second) { First = first; Second = second; }
        public bool Equals(RaceContactPair other) => First == other.First && Second == other.Second;
        public override bool Equals(object obj) => obj is RaceContactPair other && Equals(other);
        public override int GetHashCode() => unchecked(First * 397 ^ Second);
    }

    /// <summary>Public race_vehicle_contacts.gd swept SAT and four-pass body/body/wall constraint solver.</summary>
    public static class RaceVehicleContacts
    {
        public static Vec2 Half => new Vec2(.90f, 2.20f);
        public const float Skin = .025f;
        private static Vec2[] Axes(float yaw) => new[] { new Vec2((float)Math.Cos(yaw), -(float)Math.Sin(yaw)), new Vec2((float)Math.Sin(yaw), (float)Math.Cos(yaw)) };
        private static float Radius(Vec2 axis, Vec2[] axes) => Math.Abs(Vec2.Dot(axis, axes[0])) * Half.X + Math.Abs(Vec2.Dot(axis, axes[1])) * Half.Y;
        public static Vec2 Contact(IRaceContactBody a, IRaceContactBody b, Vec2 oldA, Vec2 oldB, bool swept = true)
        {
            var aa = Axes(a.HeadingYaw); var ba = Axes(b.HeadingYaw);
            var axes = new[] { aa[0], aa[1], ba[0], ba[1] };
            var end = a.Position - b.Position; var start = swept ? oldA - oldB : end; var movement = end - start;
            var entry = 0f; var leave = 1f; var entryAxis = Vec2.Zero;
            var penetration = float.PositiveInfinity; var overlapAxis = Vec2.Zero; var overlapping = true;
            foreach (var axis in axes)
            {
                var radius = Radius(axis, aa) + Radius(axis, ba) + Skin;
                var initial = Vec2.Dot(start, axis); var final = Vec2.Dot(end, axis); var depth = radius - Math.Abs(final);
                if (depth < 0) overlapping = false;
                if (depth < penetration) { penetration = depth; overlapAxis = axis * (final >= 0 ? 1 : -1); }
                var velocity = Vec2.Dot(movement, axis);
                if (Math.Abs(velocity) < .000001f) { if (Math.Abs(initial) > radius) return Vec2.Zero; }
                else
                {
                    var low = (-radius - initial) / velocity; var high = (radius - initial) / velocity;
                    if (low > high) { var temp = low; low = high; high = temp; }
                    if (low > entry) { entry = low; entryAxis = axis * -Math.Sign(velocity); }
                    leave = Math.Min(leave, high);
                    if (entry > leave) return Vec2.Zero;
                }
            }
            if (entry > 0 && entry <= 1 && entryAxis.LengthSquared > 0)
            {
                var radius = Radius(entryAxis, aa) + Radius(entryAxis, ba) + Skin;
                return entryAxis * Math.Max(0, radius - Vec2.Dot(end, entryAxis));
            }
            return overlapping ? overlapAxis * Math.Max(0, penetration) : Vec2.Zero;
        }
        public static IReadOnlyList<RaceContactPair> Solve(IReadOnlyList<IRaceContactBody> cars, IReadOnlyList<Vec2> previous)
        {
            if (cars.Count != previous.Count) throw new ArgumentException("Each contact body requires its previous position.");
            var touched = new List<RaceContactPair>();
            for (var iteration = 0; iteration < 4; iteration++)
                for (var i = 0; i < cars.Count; i++)
                    for (var j = i + 1; j < cars.Count; j++)
                    {
                        var a = cars[i]; var b = cars[j]; var start = previous[i] - previous[j]; var end = a.Position - b.Position;
                        if (Math.Min(start.X, end.X) > 5 || Math.Max(start.X, end.X) < -5 || Math.Min(start.Y, end.Y) > 5 || Math.Max(start.Y, end.Y) < -5) continue;
                        var amass = a.InverseMass; var bmass = b.InverseMass; var total = amass + bmass;
                        if (total <= 0) continue;
                        var correction = Contact(a, b, previous[i], previous[j], iteration == 0);
                        if (correction.LengthSquared < .00000001f) continue;
                        var pair = new RaceContactPair(i, j); if (!touched.Contains(pair)) touched.Add(pair);
                        var normal = correction.Normalized; var beforeA = a.Position; var beforeB = b.Position;
                        a.ResolveContact(correction * (amass / total)); b.ResolveContact(-correction * (bmass / total));
                        var moved = (a.Position - beforeA) - (b.Position - beforeB);
                        var remaining = Math.Max(0, correction.Length - Vec2.Dot(moved, normal));
                        if (remaining > .0001f)
                        {
                            var amoved = Vec2.Dot(a.Position - beforeA, normal);
                            if (amoved < correction.Length * .45f) b.ResolveContact(-normal * remaining);
                            else a.ResolveContact(normal * remaining);
                        }
                        var av = a.ContactVelocity; var bv = b.ContactVelocity; var closing = Vec2.Dot(av - bv, normal);
                        if (closing < 0)
                        {
                            var impulse = -closing / total;
                            a.ApplyContactVelocity(av + normal * impulse * amass); b.ApplyContactVelocity(bv - normal * impulse * bmass);
                        }
                    }
            return touched;
        }
    }
}
