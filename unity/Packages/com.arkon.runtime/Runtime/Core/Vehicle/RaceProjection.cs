using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Per-frame lens values supplied by the gameplay camera owner; camera-space coordinates retain Godot units.</summary>
    public readonly struct RaceProjectionParameters
    {
        public readonly float PlayfieldFraction, CameraDepth, CameraBehind, RoadScreenScale, Horizon, Zoom, Roll;
        public RaceProjectionParameters(float playfieldFraction, float cameraDepth, float cameraBehind, float roadScreenScale, float horizon, float zoom = 1, float roll = 0)
        { PlayfieldFraction = playfieldFraction; CameraDepth = cameraDepth; CameraBehind = cameraBehind; RoadScreenScale = roadScreenScale; Horizon = horizon; Zoom = zoom; Roll = roll; }
        public Vec2 ProjectCameraPoint(RaceVec3 point, Vec2 viewport)
        {
            var numerator = Numerator(point, viewport);
            return new Vec2(viewport.X * .5f, Horizon) + numerator / point.Z;
        }
        internal Vec2 Numerator(RaceVec3 point, Vec2 viewport)
        {
            var focal = CameraDepth * Zoom * viewport.X * RoadScreenScale;
            var height = viewport.Y * PlayfieldFraction;
            return new Vec2(focal * point.X, (height - Horizon) * CameraBehind * Zoom - focal * point.Y).Rotated(-Roll);
        }
        public float ProjectionScale(float depth) => CameraDepth * Zoom / depth;
    }

    public readonly struct RaceTexturedPolygon
    {
        public readonly Vec2[] Points, Uv;
        public RaceTexturedPolygon(Vec2[] points, Vec2[] uv) { Points = points; Uv = uv; }
    }

    /// <summary>Public race_projection.gd plus the algebraic project() from race_projection_frame.gd.</summary>
    public static class RaceProjection
    {
        public const float Near = .8f;
        public static RaceVec3[] ClipCamera(IReadOnlyList<RaceVec3> source, Vec2 viewport, RaceProjectionParameters lens)
        {
            var points = new List<RaceVec3>(source);
            for (var plane = 0; plane < 5; plane++)
            {
                if (points.Count == 0) break;
                var output = new List<RaceVec3>();
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i]; var b = points[(i + 1) % points.Count];
                    var da = CameraDistance(a, viewport, lens, plane); var db = CameraDistance(b, viewport, lens, plane);
                    if (da >= 0) output.Add(a);
                    if ((da >= 0) != (db >= 0)) output.Add(RaceVec3.Lerp(a, b, da / (da - db)));
                }
                points = output;
            }
            return points.ToArray();
        }
        private static float CameraDistance(RaceVec3 point, Vec2 viewport, RaceProjectionParameters lens, int plane)
        {
            if (plane == 0) return point.Z - Near;
            var numerator = lens.Numerator(point, viewport);
            switch (plane)
            {
                case 1: return numerator.X + viewport.X * .5f * point.Z;
                case 2: return viewport.X * .5f * point.Z - numerator.X;
                case 3: return numerator.Y + lens.Horizon * point.Z;
                default: return (viewport.Y * lens.PlayfieldFraction - lens.Horizon) * point.Z - numerator.Y;
            }
        }
        public static Vec2[] Surface(IReadOnlyList<RaceVec3> world, Vec2 viewport, RaceProjectionParameters lens, Func<RaceVec3, RaceVec3> worldToCamera)
        {
            var camera = new RaceVec3[world.Count];
            for (var i = 0; i < camera.Length; i++) camera[i] = worldToCamera(world[i]);
            var result = new List<Vec2>();
            foreach (var point in ClipCamera(camera, viewport, lens))
            {
                var projected = lens.ProjectCameraPoint(point, viewport);
                if (result.Count == 0 || (result[result.Count - 1] - projected).LengthSquared > .0001f) result.Add(projected);
            }
            if (result.Count > 1 && (result[0] - result[result.Count - 1]).LengthSquared < .0001f) result.RemoveAt(result.Count - 1);
            return result.ToArray();
        }
        public static Vec2[] Ribbon(RaceVec3 worldA, RaceVec3 worldB, float thickness, Vec2 viewport, RaceProjectionParameters lens, Func<RaceVec3, RaceVec3> worldToCamera)
        {
            var p = worldToCamera(worldA); var q = worldToCamera(worldB);
            if (p.Z < Near && q.Z < Near) return Array.Empty<Vec2>();
            if (p.Z < Near) p = RaceVec3.Lerp(p, q, (Near - p.Z) / (q.Z - p.Z));
            else if (q.Z < Near) q = RaceVec3.Lerp(q, p, (Near - q.Z) / (p.Z - q.Z));
            var sp = lens.ProjectCameraPoint(p, viewport); var sq = lens.ProjectCameraPoint(q, viewport);
            if ((sp - sq).LengthSquared < .0001f) return Array.Empty<Vec2>();
            var direction = (sq - sp).Normalized;
            // Godot Vector2.orthogonal() is (y,-x), rather than the opposite winding.
            var side = new Vec2(direction.Y, -direction.X);
            var wp = Math.Max(.35f, lens.ProjectionScale(p.Z) * viewport.X * lens.RoadScreenScale * thickness * .5f);
            var wq = Math.Max(.35f, lens.ProjectionScale(q.Z) * viewport.X * lens.RoadScreenScale * thickness * .5f);
            return ClipScreen(new[] { sp + side * wp, sq + side * wq, sq - side * wq, sp - side * wp },
                new RaceRect(Vec2.Zero, new Vec2(viewport.X, viewport.Y * lens.PlayfieldFraction)));
        }
        public static Vec2[] ClipScreen(IReadOnlyList<Vec2> source, RaceRect rect)
        {
            var points = new List<Vec2>(source);
            for (var plane = 0; plane < 4; plane++)
            {
                var output = new List<Vec2>();
                for (var i = 0; i < points.Count; i++)
                {
                    var a = points[i]; var b = points[(i + 1) % points.Count];
                    var da = ScreenDistance(a, rect, plane); var db = ScreenDistance(b, rect, plane);
                    if (da >= 0) output.Add(a);
                    if ((da >= 0) != (db >= 0)) output.Add(Vec2.Lerp(a, b, da / (da - db)));
                }
                points = output;
            }
            return points.ToArray();
        }
        public static RaceTexturedPolygon ClipTexturedScreen(IReadOnlyList<Vec2> source, IReadOnlyList<Vec2> uv, RaceRect rect)
        {
            if (source.Count != uv.Count) throw new ArgumentException("One UV coordinate is required per vertex.");
            var points = new List<Vec2>(source); var coords = new List<Vec2>(uv);
            for (var plane = 0; plane < 4; plane++)
            {
                var output = new List<Vec2>(); var outputUv = new List<Vec2>();
                for (var i = 0; i < points.Count; i++)
                {
                    var next = (i + 1) % points.Count; var a = points[i]; var b = points[next];
                    var da = ScreenDistance(a, rect, plane); var db = ScreenDistance(b, rect, plane);
                    if (da >= 0) { output.Add(a); outputUv.Add(coords[i]); }
                    if ((da >= 0) != (db >= 0))
                    {
                        var t = da / (da - db);
                        output.Add(Vec2.Lerp(a, b, t)); outputUv.Add(Vec2.Lerp(coords[i], coords[next], t));
                    }
                }
                points = output; coords = outputUv;
            }
            return new RaceTexturedPolygon(points.ToArray(), coords.ToArray());
        }
        private static float ScreenDistance(Vec2 point, RaceRect rect, int plane)
        {
            switch (plane) { case 0: return point.X - rect.Position.X; case 1: return rect.End.X - point.X; case 2: return point.Y - rect.Position.Y; default: return rect.End.Y - point.Y; }
        }
    }
}
