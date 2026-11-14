using System;
using System.Collections.Generic;
using System.Globalization;

namespace Arkon.Core
{
    /// <summary>Public race_math.gd behavior port. No WheelCollider, scene or race-state ownership.</summary>
    public static class RaceMath
    {
        public const float StadiumRadius = 190;
        public static float CurveOf(int segment)
        {
            switch (segment) { case 1: return -1.6f; case 2: return 1.6f; case 3: return -3.2f; case 4: return 3.2f; case 5: return 2.2f; default: return 0; }
        }
        private static float TurnShift(int segment)
        {
            switch (segment) { case 1: return -56; case 2: return 56; case 3: return -72; case 4: return 72; default: return 0; }
        }
        public static float[] AccumulateTrackX(IReadOnlyList<int> pattern)
        {
            var result = new float[pattern.Count]; var currentX = 0f; var i = 0;
            while (i < pattern.Count)
            {
                var type = pattern[i]; var j = i + 1;
                while (j < pattern.Count && pattern[j] == type) j++;
                var shift = TurnShift(type);
                if (shift != 0)
                {
                    for (var k = 0; k < j - i; k++)
                    {
                        var t = (float)(k + 1) / (j - i);
                        result[i + k] = currentX + shift * (1 - (float)Math.Cos(t * Math.PI)) * .5f;
                    }
                    currentX += shift;
                }
                else for (var k = i; k < j; k++) result[k] = currentX;
                i = j;
            }
            // Closure is an authoring invariant; never bend an invalid track to hide its error.
            return result;
        }
        public static float TrackClosureError(IReadOnlyList<int> pattern)
        {
            var total = 0f; var i = 0;
            while (i < pattern.Count)
            {
                var type = pattern[i]; var j = i + 1;
                while (j < pattern.Count && pattern[j] == type) j++;
                total += TurnShift(type); i = j;
            }
            return total;
        }
        public static float TrackElevation(float progress, int trackSize)
        {
            if (trackSize <= 0) return 0;
            var ratio = RaceNumerics.PosMod(progress, trackSize) / trackSize;
            return (float)Math.Sin(ratio * RaceNumerics.Tau * 2) * 1.6f + (float)Math.Sin(ratio * RaceNumerics.Tau * 5) * .45f;
        }
        private static RaceVec3 StadiumBasePoint(float progress, int trackSize, float segmentHeight)
        {
            if (trackSize <= 0) return RaceVec3.Zero;
            var totalLength = trackSize * Math.Max(segmentHeight, .001f);
            var halfStraight = Math.Max(1, (totalLength - RaceNumerics.Tau * StadiumRadius) * .25f);
            var distance = RaceNumerics.PosMod(progress, trackSize) / trackSize * totalLength;
            var straight = halfStraight * 2; var arc = (float)Math.PI * StadiumRadius;
            if (distance < straight) return new RaceVec3(StadiumRadius, 0, -halfStraight + distance);
            distance -= straight;
            if (distance < arc)
            {
                var theta = distance / StadiumRadius;
                return new RaceVec3(StadiumRadius * (float)Math.Cos(theta), 0, halfStraight + StadiumRadius * (float)Math.Sin(theta));
            }
            distance -= arc;
            if (distance < straight) return new RaceVec3(-StadiumRadius, 0, halfStraight - distance);
            distance -= straight;
            var finalTheta = (float)Math.PI + distance / StadiumRadius;
            return new RaceVec3(StadiumRadius * (float)Math.Cos(finalTheta), 0, -halfStraight + StadiumRadius * (float)Math.Sin(finalTheta));
        }
        private static RaceVec3 StadiumBaseTangent(float progress, int trackSize, float segmentHeight)
        {
            if (trackSize <= 0) return new RaceVec3(0, 0, 1);
            var totalLength = trackSize * Math.Max(segmentHeight, .001f);
            var halfStraight = Math.Max(1, (totalLength - RaceNumerics.Tau * StadiumRadius) * .25f);
            var distance = RaceNumerics.PosMod(progress, trackSize) / trackSize * totalLength;
            var straight = halfStraight * 2; var arc = (float)Math.PI * StadiumRadius;
            if (distance < straight) return new RaceVec3(0, 0, 1);
            distance -= straight;
            if (distance < arc)
            {
                var theta = distance / StadiumRadius;
                return new RaceVec3(-(float)Math.Sin(theta), 0, (float)Math.Cos(theta)).Normalized;
            }
            distance -= arc;
            if (distance < straight) return new RaceVec3(0, 0, -1);
            distance -= straight;
            var finalTheta = (float)Math.PI + distance / StadiumRadius;
            return new RaceVec3(-(float)Math.Sin(finalTheta), 0, (float)Math.Cos(finalTheta)).Normalized;
        }
        public static RaceVec3 TrackWorldPosition(float progress, IReadOnlyList<float> trackX, int trackSize, float segmentHeight)
        {
            var point = StadiumBasePoint(progress, trackSize, segmentHeight);
            var tangent = StadiumBaseTangent(progress, trackSize, segmentHeight);
            var right = RaceVec3.Cross(new RaceVec3(0, 1, 0), tangent);
            right = right.LengthSquared < .0001f ? new RaceVec3(1, 0, 0) : right.Normalized;
            point += right * TrackCenterX(progress, trackX);
            return new RaceVec3(point.X, TrackElevation(progress, trackSize), point.Z);
        }
        public static RaceVec3 TrackWorldTangent(float progress, IReadOnlyList<float> trackX, int trackSize, float segmentHeight)
        {
            var tangent = TrackWorldPosition(progress + .02f, trackX, trackSize, segmentHeight) - TrackWorldPosition(progress, trackX, trackSize, segmentHeight);
            if (tangent.LengthSquared < .0001f) tangent = StadiumBaseTangent(progress, trackSize, segmentHeight);
            return tangent.Normalized;
        }
        public static float NearestTrackProgress(RaceVec3 worldPosition, IReadOnlyList<float> trackX, int trackSize, float segmentHeight, float hintProgress = -1)
        {
            if (trackSize <= 0 || trackX.Count == 0) return 0;
            var bestProgress = 0f; var bestDistance = float.PositiveInfinity;
            var start = hintProgress >= 0 ? -3 : 0; var end = hintProgress >= 0 ? 3 : trackSize - 1;
            var center = hintProgress >= 0 ? (int)Math.Floor(RaceNumerics.PosMod(hintProgress, trackSize)) : 0;
            for (var offset = start; offset <= end; offset++)
            {
                var i = hintProgress >= 0 ? RaceNumerics.PosMod(center + offset, trackSize) : offset;
                var p0 = TrackWorldPosition(i, trackX, trackSize, segmentHeight);
                var p1 = TrackWorldPosition(i + 1, trackX, trackSize, segmentHeight);
                var segment = p1 - p0; var lengthSq = segment.LengthSquared; var t = 0f;
                if (lengthSq > .0001f) t = Scalar.Clamp(RaceVec3.Dot(worldPosition - p0, segment) / lengthSq, 0, 1);
                var distanceSq = (worldPosition - (p0 + segment * t)).LengthSquared;
                if (distanceSq < bestDistance) { bestDistance = distanceSq; bestProgress = i + t; }
            }
            // A collision that invalidates the local hint must recover through a full scan.
            return hintProgress >= 0 && bestDistance > 256 ? NearestTrackProgress(worldPosition, trackX, trackSize, segmentHeight, -1) : bestProgress;
        }
        public static float TrackHeading(float progress, IReadOnlyList<float> trackX, float segmentHeight)
        {
            var tangent = TrackWorldTangent(progress, trackX, trackX.Count, segmentHeight);
            return (float)Math.Atan2(tangent.X, tangent.Z);
        }
        public static float TrackCenterX(float progress, IReadOnlyList<float> trackX)
        {
            var n = trackX.Count; if (n == 0) return 0; if (n == 1) return trackX[0];
            progress = Scalar.Clamp(progress, 0, n - 1);
            var index = (int)Math.Floor(progress); var t = progress - (float)Math.Floor(progress);
            var p1 = trackX[index]; var p2 = trackX[Math.Min(index + 1, n - 1)];
            if (n < 4) return RaceNumerics.Lerp(p1, p2, t);
            var p0 = trackX[Math.Max(index - 1, 0)]; var p3 = trackX[Math.Min(index + 2, n - 1)];
            var t2 = t * t; var t3 = t2 * t;
            return .5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }
        public static float TrackCenterSlope(float progress, IReadOnlyList<float> trackX)
        {
            var n = trackX.Count; if (n < 2 || progress <= 0 || progress >= n - 1) return 0;
            progress = Scalar.Clamp(progress, 0, n - 1);
            var index = (int)Math.Floor(progress); var t = progress - (float)Math.Floor(progress);
            var p1 = trackX[index]; var p2 = trackX[Math.Min(index + 1, n - 1)];
            if (n < 4) return p2 - p1;
            var p0 = trackX[Math.Max(index - 1, 0)]; var p3 = trackX[Math.Min(index + 2, n - 1)];
            return .5f * ((-p0 + p2) + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t + 3 * (-p0 + 3 * p1 - 3 * p2 + p3) * t * t);
        }
        public static float CurveAt(float progress, IReadOnlyList<int> pattern)
        {
            var n = pattern.Count; if (n == 0) return 0;
            var index = (int)Math.Floor(progress); var t = progress - (float)Math.Floor(progress);
            var c0 = 0f; var c1 = 0f; var w0 = 0f; var w1 = 0f;
            for (var k = -2; k <= 2; k++)
            {
                var weight = 3 - Math.Abs(k);
                c0 += CurveOf(pattern[RaceNumerics.PosMod(index + k, n)]) * weight;
                c1 += CurveOf(pattern[RaceNumerics.PosMod(index + 1 + k, n)]) * weight;
                w0 += weight; w1 += weight;
            }
            return RaceNumerics.Lerp(c0 / Math.Max(w0, .001f), c1 / Math.Max(w1, .001f), t * t * (3 - 2 * t));
        }
        public static float StepSpeed(float speed, float throttle, float brake, float dt, float maximumSpeed, float acceleration, float brakeForce, float drag)
        {
            if (dt <= 0) return Scalar.Clamp(speed, 0, maximumSpeed);
            if (brake > .01f) return Math.Max(speed - brakeForce * Scalar.Clamp(brake, 0, 1) * dt, 0);
            var target = maximumSpeed * Scalar.Clamp(throttle, 0, 1);
            var response = speed < target ? acceleration : acceleration * .5f;
            speed = Scalar.MoveToward(speed, target, response * dt);
            return Math.Max(speed * (float)Math.Exp(-drag * dt * .001f), 0);
        }
        public static float SteeringDelta(float input, float speed, float dt, float rate, float maximumSpeed)
        {
            var factor = Scalar.Clamp(1 - speed / Math.Max(maximumSpeed, .001f) * .55f, .45f, 1);
            return Scalar.Clamp(input, -1, 1) * rate * factor * dt;
        }
        public static float RoadOffset(float lateralX, float centerX, float halfWidth) => halfWidth <= 0 ? 0 : (lateralX - centerX) / halfWidth;
        public static float AiBrakeFor(int segment, int distanceAhead)
        {
            var curve = Math.Abs(CurveOf(segment));
            return curve < .5f ? 0 : Scalar.Clamp(curve / 14 * Scalar.Clamp(1 - distanceAhead / 12f, 0, 1), 0, 1);
        }
        public static string FormatTime(float seconds)
        {
            if (seconds < 0) return "--:--.--";
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", (int)seconds / 60, (int)seconds % 60, (int)((seconds % 1) * 100));
        }
        public static float SafeCornerSpeed(float at, IReadOnlyList<float> trackX, float maximumSpeed, float authority, float grip)
        {
            var safe = maximumSpeed;
            for (var metres = 0; metres <= 180; metres += 15)
            {
                var p = at + metres / 40f;
                var angle = (float)Math.Atan2(TrackCenterSlope(p, trackX), 40);
                var before = (float)Math.Atan2(TrackCenterSlope(p - .03f, trackX), 40);
                var after = (float)Math.Atan2(TrackCenterSlope(p + .03f, trackX), 40);
                var curvature = Math.Abs(after - before) / 2.4f;
                var local = Math.Min(maximumSpeed, (float)Math.Sqrt(grip / Math.Max(curvature, .00001f)));
                local = Math.Min(local, maximumSpeed * Scalar.Clamp((1 - Math.Abs(angle) / authority) / .52f, .20f, 1));
                safe = Math.Min(safe, (float)Math.Sqrt(local * local + 2 * 7.2f * metres));
            }
            return safe;
        }
    }
}
