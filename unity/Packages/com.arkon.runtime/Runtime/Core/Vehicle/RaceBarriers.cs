using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public readonly struct RaceSweepResult
    {
        public readonly float WorldX, WorldZ, Contact;
        public Vec2 Position => new Vec2(WorldX, WorldZ);
        public RaceSweepResult(float worldX, float worldZ, float contact) { WorldX = worldX; WorldZ = worldZ; Contact = contact; }
    }

    /// <summary>Public race_barriers.gd: sweep world X/Z; never clamp against a moving centerline after movement.</summary>
    public static class RaceBarriers
    {
        public const float BodyHalfWidth = .85f, BodyHalfLength = 2.1f;
        private static readonly int SprintSegmentCount = RaceCalibration.DefaultSprint.TotalSegments;
        public static Vec2 Limits(float z, IReadOnlyList<int> pattern, IReadOnlyList<float> track, float clearance = BodyHalfWidth)
        {
            var left = float.NegativeInfinity; var right = float.PositiveInfinity;
            if (pattern.Count == SprintSegmentCount)
            {
                if (z >= 1520 - BodyHalfLength && z < RaceCalibration.BridgeStart)
                { left = -RaceCalibration.FieldFenceX + clearance; right = RaceCalibration.FieldFenceX - clearance; }
                if (z >= RaceCalibration.BridgeStart - BodyHalfLength && z < RaceCalibration.BridgeEnd)
                { left = -RaceCalibration.BridgeRailX + clearance; right = RaceCalibration.BridgeRailX - clearance; }
                if (z >= RaceCalibration.TunnelStart - BodyHalfLength && z < RaceCalibration.TunnelEnd)
                { left = -RaceCalibration.TunnelBodyClearX + clearance; right = RaceCalibration.TunnelBodyClearX - clearance; }
            }
            if (z >= 0 && z < pattern.Count * RaceCalibration.SegmentHeight)
            {
                var curve = RaceMath.CurveAt(z / RaceCalibration.SegmentHeight, pattern);
                if (curve > .4f) left = Math.Max(left, -RaceCalibration.CurveRailX + clearance);
                else if (curve < -.4f) right = Math.Min(right, RaceCalibration.CurveRailX - clearance);
            }
            var center = RaceMath.TrackCenterX(z / RaceCalibration.SegmentHeight, track);
            return new Vec2(center + left, center + right);
        }
        public static RaceSweepResult Sweep(Vec2 start, Vec2 target, IReadOnlyList<int> pattern, IReadOnlyList<float> track, float heading)
        {
            var clearance = BodyHalfWidth * Math.Abs((float)Math.Cos(heading)) + BodyHalfLength * Math.Abs((float)Math.Sin(heading));
            var bounds = Limits(start.Y, pattern, track, clearance);
            var x = Scalar.Clamp(target.X, bounds.X, bounds.Y);
            var contact = Math.Abs(x - target.X) > .00001f ? 1 : 0;
            var z = target.Y; var firstHit = 1f; var blocked = false;
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(target.Y - start.Y)));
            for (var i = 1; i <= steps; i++)
            {
                var fraction = (float)i / steps;
                var sample = Limits(RaceNumerics.Lerp(start.Y, target.Y, fraction), pattern, track, clearance);
                if (x < sample.X || x > sample.Y) { firstHit = fraction; blocked = true; break; }
            }
            if (blocked)
            {
                var low = 0f; var high = firstHit;
                for (var i = 0; i < 14; i++)
                {
                    var middle = (low + high) * .5f;
                    bounds = Limits(RaceNumerics.Lerp(start.Y, target.Y, middle), pattern, track, clearance);
                    if (x >= bounds.X && x <= bounds.Y) low = middle; else high = middle;
                }
                z = RaceNumerics.Lerp(start.Y, target.Y, low); contact = 1;
            }
            return new RaceSweepResult(x, z, contact);
        }
    }
}
