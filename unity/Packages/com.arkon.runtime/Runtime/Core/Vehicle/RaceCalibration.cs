using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Public v1.68 race_level_data.gd calibration; simulation speed units are NOT m/s.</summary>
    public static class RaceCalibration
    {
        public const float SegmentHeight = 40, RoadWidth = 9, GridSpacing = 7;
        public const float SpeedUnitsPerKmH = 32f / 100f;
        public const float SimulationUnitsPerMps = SpeedUnitsPerKmH * 3.6f;
        public const float PlayerMaxSpeedKmH = 219;
        public const float PlayerMaxSpeed = PlayerMaxSpeedKmH * SpeedUnitsPerKmH;
        public const float FieldFenceX = 7.8f, BridgeRailX = 5.5f, CurveRailX = 8;
        public const float TunnelBodyClearX = 5.9f;
        public const float BridgeStart = 3120, BridgeEnd = 4080, TunnelStart = 5120, TunnelEnd = 6400;
        public const float VergeEdge = RoadWidth * .5f + .32f, CutWallX = 6.6f, EmbankmentX = 24;
        private static readonly float[] ElevationZ = { 0, 2680, 3120, 3520, 4080, 4560, 4820, 5120, 6400, 6760, 8880 };
        private static readonly float[] ElevationY = { 0, 0, 18, 20, 18, 0, 0, -10, -10, 0, 0 };
        public static RaceTrackDefinition DefaultSprint => new RaceTrackDefinition(
            new[] { 0, 2, 0, 1, 0, 2, 0, 3, 0, 1, 0, 2, 0, 1, 0, 4, 0, 2, 0, 1, 0 },
            new[] { 12, 4, 14, 4, 10, 4, 5, 5, 50, 4, 19, 4, 12, 4, 13, 5, 16, 4, 11, 4, 18 });
        public static float RoadHeight(float z)
        {
            for (var i = 1; i < ElevationZ.Length; i++)
            {
                if (z > ElevationZ[i]) continue;
                if (ElevationY[i - 1] == ElevationY[i]) return ElevationY[i];
                var t = Scalar.Clamp((z - ElevationZ[i - 1]) / (ElevationZ[i] - ElevationZ[i - 1]), 0, 1);
                return RaceNumerics.Lerp(ElevationY[i - 1], ElevationY[i], (1 - (float)Math.Cos(Math.PI * t)) * .5f);
            }
            return ElevationY[ElevationY.Length - 1];
        }
        public static float RoadGrade(float z)
        {
            for (var i = 1; i < ElevationZ.Length; i++)
            {
                if (z > ElevationZ[i]) continue;
                var span = ElevationZ[i] - ElevationZ[i - 1];
                var t = Scalar.Clamp((z - ElevationZ[i - 1]) / span, 0, 1);
                return (ElevationY[i] - ElevationY[i - 1]) * (float)Math.PI * (float)Math.Sin(Math.PI * t) / (2 * span);
            }
            return 0;
        }
        public static float TerrainHeight(float z) => z >= BridgeEnd ? 0 : RoadHeight(z);
        public static float GroundHeight(float offset, float z)
        {
            var road = RoadHeight(z); var terrain = TerrainHeight(z); var x = Math.Abs(offset);
            if (z >= BridgeStart && z < BridgeEnd) return x <= BridgeRailX ? road : 0;
            if (road < terrain - .001f) return x < CutWallX ? road : terrain;
            return RaceNumerics.Lerp(road, terrain, Scalar.Clamp((x - VergeEdge) / (EmbankmentX - VergeEdge), 0, 1));
        }
    }

    /// <summary>Exact min-array-length expansion policy from race_track_data.gd.</summary>
    public sealed class RaceTrackDefinition
    {
        private readonly int[] types, counts;
        public RaceTrackDefinition(IReadOnlyList<int> sectionTypes, IReadOnlyList<int> sectionCounts)
        {
            types = new int[sectionTypes.Count]; counts = new int[sectionCounts.Count];
            for (var i = 0; i < types.Length; i++) types[i] = sectionTypes[i];
            for (var i = 0; i < counts.Length; i++) counts[i] = sectionCounts[i];
        }
        public int TotalSegments
        {
            get { var total = 0; foreach (var count in counts) total += Math.Max(0, count); return total; }
        }
        public int[] BuildPattern()
        {
            var result = new List<int>();
            for (var i = 0; i < Math.Min(types.Length, counts.Length); i++)
                for (var n = 0; n < Math.Max(0, counts[i]); n++) result.Add(types[i]);
            return result.ToArray();
        }
    }
}
