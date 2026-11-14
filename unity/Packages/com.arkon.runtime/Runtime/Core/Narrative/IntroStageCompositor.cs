using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public readonly struct IntroSpriteDraw
    {
        public readonly string ArtId;
        public readonly string Texture;
        public readonly IntroRect Source;
        // Bounds after reflecting the panel. MirrorX also flips image pixels.
        public readonly IntroRect Destination;
        public readonly double[] Tint;
        public readonly bool MirrorX;
        public IntroSpriteDraw(string artId, string texture, IntroRect source,
            IntroRect destination, double[] tint, bool mirrorX)
        { ArtId = artId; Texture = texture; Source = source; Destination = destination; Tint = tint; MirrorX = mirrorX; }
    }

    /// <summary>
    /// Portable schema3 PNG compositor. Coordinates and time remain in the
    /// authoritative Godot plan's 768×432 frame and absolute narration clock.
    /// Mirrors scripts/intro_stage.gd track, crop, pose, wipe and traffic math.
    /// Produces draw commands; it does not invent or alter any artwork.
    /// </summary>
    public static class IntroStageCompositor
    {
        public const double FrameWidth = 768;
        public const double FrameHeight = 432;
        public const double AvenueHorizonX = 689;
        public const double AvenueHorizonY = 117;
        public const double AvenueNearY = 450;
        public const double PassedDepth = .58;

        public static double[] Track(double[][] keys, double elapsed, bool ease)
        {
            if (keys == null || keys.Length == 0 || keys[0] == null || keys[0].Length < 2)
                throw new ArgumentException("A track needs at least one time/value row.", nameof(keys));
            var previous = keys[0];
            for (var i = 1; i < keys.Length; i++)
            {
                var next = keys[i];
                if (elapsed < next[0])
                {
                    var span = next[0] - previous[0];
                    var weight = Math.Max(0, Math.Min(1, (elapsed - previous[0]) / span));
                    if (ease) weight = weight * weight * (3 - 2 * weight);
                    var values = new double[previous.Length - 1];
                    for (var field = 1; field < previous.Length; field++)
                        values[field - 1] = previous[field] + (next[field] - previous[field]) * weight;
                    return values;
                }
                previous = next;
            }
            var held = new double[previous.Length - 1];
            Array.Copy(previous, 1, held, 0, held.Length); return held;
        }

        public static IntroPanelShotData ShotAt(IntroStage3Data plan, double seconds)
        {
            if (plan == null || plan.Shots == null || plan.Shots.Length == 0) return null;
            foreach (var shot in plan.Shots) if (seconds < shot.End) return shot;
            return plan.Shots[plan.Shots.Length - 1];
        }

        public static IReadOnlyList<IntroSpriteDraw> BuildDrawCommands(IntroStageData stage, double seconds)
        {
            if (stage == null || stage.schema != 3 || stage.Stage3 == null)
                throw new ArgumentException("A decoded schema3 stage is required.", nameof(stage));
            return BuildDrawCommands(stage.Stage3, seconds);
        }

        public static IReadOnlyList<IntroSpriteDraw> BuildDrawCommands(IntroStage3Data plan, double seconds)
        {
            var result = new List<IntroSpriteDraw>();
            var shot = ShotAt(plan, seconds); if (shot == null) return result;
            var elapsed = Math.Max(0, seconds - shot.Start);
            foreach (var panel in shot.Panels)
                if (elapsed >= panel.Reveal) DrawPanel(result, plan, panel, elapsed - panel.Reveal);
            return result;
        }

        private static void DrawPanel(List<IntroSpriteDraw> output, IntroStage3Data plan, IntroPanelData panel, double elapsed)
        {
            var target = panel.Rect;
            var view = panel.Crop;
            if (panel.Camera != null && panel.Camera.Length > 0)
            {
                var offset = Track(panel.Camera, elapsed, true);
                view = new IntroRect(view.X + offset[0], view.Y + offset[1], view.Width, view.Height);
            }
            var width = Math.Min(view.Width, view.Height * target.Width / target.Height);
            var height = Math.Min(view.Height, view.Width * target.Height / target.Width);
            view = new IntroRect(view.X + (view.Width - width) * .5,
                view.Y + (view.Height - height) * .5, width, height);
            var tone = panel.Tone;
            var tint = new[] { tone[0], tone[1], tone[2], 1.0 };
            if (!string.IsNullOrEmpty(panel.Art)) DrawSprite(output, plan, panel.Art, 0, 0, 1, view, target, tint, panel.Mirror);
            foreach (var layer in panel.Behind ?? Array.Empty<IntroSpriteLayer>())
                DrawLayer(output, plan, layer, view, target, tint, panel.Mirror, elapsed);
            if (panel.Traffic != null) DrawTraffic(output, plan, panel.Traffic, view, target, tint, panel.Mirror, elapsed);
            foreach (var layer in panel.Layers ?? Array.Empty<IntroSpriteLayer>())
                DrawLayer(output, plan, layer, view, target, tint, panel.Mirror, elapsed);
        }

        private static void DrawLayer(List<IntroSpriteDraw> output, IntroStage3Data plan, IntroSpriteLayer layer,
            IntroRect view, IntroRect target, double[] tint, bool mirror, double elapsed)
        {
            var motion = layer.Motion != null && layer.Motion.Length > 0
                ? Track(layer.Motion, elapsed, layer.Ease) : new[] { 0.0, 0.0, 1.0 };
            if (motion[2] <= 0) return;
            var art = layer.Art;
            if (!string.IsNullOrEmpty(layer.Pose))
            {
                var frames = plan.Poses[layer.Pose];
                var phase = Math.Max(0, Math.Min(1, Track(layer.Phase, elapsed, true)[0]));
                // Godot roundi / JavaScript Math.round round positive .5 upward.
                var index = (int)Math.Floor(phase * (frames.Length - 1) + .5);
                art = frames[index];
            }
            var visibleWidth = layer.Wipe != null && layer.Wipe.Length > 0 ? Track(layer.Wipe, elapsed, false)[0] : 1;
            var color = new[] { tint[0], tint[1], tint[2], tint[3] * motion[2] };
            DrawSprite(output, plan, art, layer.X + motion[0], layer.Y + motion[1], layer.Scale,
                view, target, color, mirror, visibleWidth);
        }

        private static void DrawTraffic(List<IntroSpriteDraw> output, IntroStage3Data plan, IntroTrafficData traffic,
            IntroRect view, IntroRect target, double[] tint, bool mirror, double elapsed)
        {
            var distance = traffic.Offset + elapsed * traffic.Rate;
            var fleet = plan.Formations[traffic.Fleet];
            // Preserve the authored far-to-near order; sorting changes occlusion.
            foreach (var vehicle in fleet.Vehicles)
            {
                var art = plan.Art[vehicle.Art];
                double scale, gx, gy;
                if (fleet.Projection == "side")
                { scale = vehicle.NearWidth / art.Source.Width; gx = vehicle.X + distance; gy = vehicle.DepthOrY; }
                else
                {
                    var depth = vehicle.DepthOrY - distance;
                    if (depth < PassedDepth) continue;
                    scale = vehicle.NearWidth / (art.Source.Width * depth);
                    gx = AvenueHorizonX + (vehicle.X - AvenueHorizonX) / depth;
                    gy = AvenueHorizonY + (AvenueNearY - AvenueHorizonY) / depth;
                }
                var x = gx - art.Source.Width * vehicle.AnchorX * scale;
                var y = gy - art.Source.Height * vehicle.AnchorY * scale;
                DrawSprite(output, plan, vehicle.Art, x, y, scale, view, target, tint, mirror);
                if (!plan.Wheels.TryGetValue(vehicle.Art, out var wheels)) continue;
                foreach (var wheel in wheels)
                {
                    var frames = plan.Poses[wheel.Pose];
                    var phase = Math.Floor(.11 * distance / scale);
                    var index = (int)(((phase % frames.Length) + frames.Length) % frames.Length);
                    DrawSprite(output, plan, frames[index], x + wheel.X * scale, y + wheel.Y * scale,
                        scale, view, target, tint, mirror);
                }
            }
        }

        private static void DrawSprite(List<IntroSpriteDraw> output, IntroStage3Data plan, string id,
            double x, double y, double scale, IntroRect view, IntroRect target, double[] tint,
            bool mirror, double visibleWidth = 1)
        {
            var art = plan.Art[id];
            var x0 = Math.Max(x, view.X); var y0 = Math.Max(y, view.Y);
            var width = Math.Min(x + art.Source.Width * scale * visibleWidth, view.X + view.Width) - x0;
            var height = Math.Min(y + art.Source.Height * scale, view.Y + view.Height) - y0;
            if (width <= 0 || height <= 0 || tint[3] <= 0) return;
            var source = new IntroRect(art.Source.X + (x0 - x) / scale,
                art.Source.Y + (y0 - y) / scale, width / scale, height / scale);
            var destination = new IntroRect(target.X + (x0 - view.X) * target.Width / view.Width,
                target.Y + (y0 - view.Y) * target.Height / view.Height,
                width * target.Width / view.Width, height * target.Height / view.Height);
            if (mirror) destination = new IntroRect(2 * target.X + target.Width - destination.X - destination.Width,
                destination.Y, destination.Width, destination.Height);
            output.Add(new IntroSpriteDraw(id, art.Asset, source, destination,
                new[] { tint[0], tint[1], tint[2], tint[3] }, mirror));
        }
    }
}
