using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity.Intro
{
    /// <summary>
    /// Engine adapter for the approved PNG art and existing schema1 direction.
    /// Ported from white_guard/scripts/intro_stage.gd. The direction itself is
    /// deliberately unchanged and has not received artistic acceptance.
    /// Requires actual Unity Editor/platform rendering verification.
    /// </summary>
    public sealed class IntroStageRenderer : IDisposable
    {
        private readonly IntroStageData _stage;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private Texture2D _circle;

        public IntroStageRenderer(IntroStageData stage, Func<string, Texture2D> loadTexture)
        {
            _stage = stage ?? throw new ArgumentNullException(nameof(stage));
            foreach (var shot in stage.shots)
            {
                Load(shot.asset, loadTexture);
                foreach (var layer in shot.layers ?? Array.Empty<IntroLayerData>()) Load(layer.asset, loadTexture);
            }
        }

        public IntroShotData ShotAt(double seconds) => IntroStageGeometry.ShotAt(_stage, seconds);

        private void Load(string path, Func<string, Texture2D> loader)
        {
            if (_textures.ContainsKey(path)) return;
            var texture = loader(path);
            if (texture == null) throw new InvalidOperationException("Missing PNG illustration: " + path);
            _textures.Add(path, texture);
        }

        /// <summary>Call inside a clipped GUI group in a 1280×720 design frame.</summary>
        public void Draw(double seconds, Color tint, Font bodyFont)
        {
            var shot = ShotAt(seconds); if (shot == null) return;
            var elapsed = seconds - shot.start;
            var progress = Math.Max(0, Math.Min(1, elapsed / (shot.end - shot.start)));
            var background = _textures[shot.asset];
            var view = IntroStageGeometry.Lerp(shot.view_from, shot.view_to, progress);
            var source = IntroStageGeometry.BackgroundSource(background.width, background.height, shot.quadrant, view);
            DrawRegion(background, new Rect(0, 0, 1280, 720), source, tint);
            foreach (var layer in shot.layers ?? Array.Empty<IntroLayerData>())
            {
                var rectangle = IntroStageGeometry.Lerp(layer.@from, layer.to, progress);
                var actor = _textures[layer.asset];
                var color = tint; color.a *= layer.alpha;
                DrawRegion(actor, new Rect((float)rectangle.X * 1280, (float)rectangle.Y * 720,
                    (float)rectangle.Width * 1280, (float)rectangle.Height * 720),
                    IntroStageGeometry.Quadrant(actor.width, actor.height, layer.quadrant), color);
            }
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(shot.label) && elapsed < 3.5)
            {
                var fade = Mathf.Clamp01((float)(3.5 - elapsed) * 2);
                var style = new GUIStyle(GUI.skin.label) { font = bodyFont, fontSize = 18, alignment = TextAnchor.MiddleLeft };
                style.normal.textColor = new Color(.94f, .90f, .80f, fade);
                var width = style.CalcSize(new GUIContent(shot.label)).x;
                Solid(new Rect(22, 22, width + 24, 37), new Color(.06f, .07f, .05f, .8f * fade));
                GUI.Label(new Rect(34, 22, width + 2, 37), shot.label, style);
            }
            DrawEffects(shot.effect, elapsed);
            GUI.color = Color.white;
        }

        private static void DrawRegion(Texture2D image, Rect destination, IntroRect source, Color color)
        {
            var uv = IntroStageGeometry.ToBottomLeftUv(source, image.width, image.height);
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(destination, image,
                new Rect((float)uv.X, (float)uv.Y, (float)uv.Width, (float)uv.Height), true);
        }

        private void DrawEffects(string effect, double elapsed)
        {
            if (effect == "four_attempts")
            {
                for (var i = 0; i < 4; i++)
                {
                    if (elapsed < i * .65) continue;
                    var x = .065f + i * .032f;
                    Line(new Vector2(x * 1280, .72f * 720), new Vector2((x + .017f) * 1280, .63f * 720),
                        new Color32(0xad, 0x3b, 0x2e, 255), 5);
                }
            }
            else if (effect == "rifle_fire" && elapsed < 1.8 && elapsed % .43 < .075)
            {
                EnsureCircle(); GUI.color = new Color(1, .85f, .5f, .8f);
                foreach (var p in new[] { new Vector2(.49f * 1280, .21f * 720), new Vector2(.75f * 1280, .24f * 720) })
                    GUI.DrawTexture(new Rect(p.x - 8, p.y - 8, 16, 16), _circle, ScaleMode.StretchToFill, true);
            }
            else if (effect == "rocket")
            {
                var t = Mathf.Clamp01((float)elapsed / 2);
                var end = Vector2.Lerp(new Vector2(.22f * 1280, .65f * 720), new Vector2(.89f * 1280, .08f * 720), t);
                Line(end - new Vector2(.02f * 1280, -.02f * 720), end, new Color32(255, 241, 182, 255), 3);
            }
        }

        internal static void Solid(Rect rect, Color color)
        {
            var previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true); GUI.color = previous;
        }

        private static void Line(Vector2 start, Vector2 end, Color color, float width)
        {
            var matrix = GUI.matrix; var previous = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(end.y - start.y, end.x - start.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width * .5f, Vector2.Distance(start, end), width), Texture2D.whiteTexture);
            GUI.matrix = matrix; GUI.color = previous;
        }

        private void EnsureCircle()
        {
            if (_circle != null) return;
            _circle = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Intro muzzle light", filterMode = FilterMode.Bilinear };
            var pixels = new Color32[32 * 32];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
            {
                var distance = Mathf.Sqrt((x - 15.5f) * (x - 15.5f) + (y - 15.5f) * (y - 15.5f));
                pixels[y * 32 + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp((16 - distance) * 255, 0, 255));
            }
            _circle.SetPixels32(pixels); _circle.Apply(false, true);
        }

        public void Dispose()
        {
            if (_circle != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_circle);
                else UnityEngine.Object.DestroyImmediate(_circle);
            }
            _circle = null; _textures.Clear();
        }
    }
}
