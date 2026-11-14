using System;
using System.Collections.Generic;
using System.Text;

namespace Arkon.Core
{
    // Field names intentionally match the unchanged Godot JSON. Unity JsonUtility
    // reads these DTOs; the portable runtime has no dependency on UnityEngine.
    [Serializable] public sealed class IntroBookData
    {
        public int schema;
        public string id;
        public string title;
        public string chapter_title;
        public string speaker;
        public string point_of_view;
        public string storyboard;
        public double duration;
        public IntroBlockData[] blocks;
    }

    [Serializable] public sealed class IntroBlockData
    {
        public string id;
        public string text;
        public string atlas;
        public int quadrant;
        public string audio;
        public double duration;
        public IntroCaptionData[] timed_captions;
    }

    [Serializable] public sealed class IntroCaptionData
    {
        public double start;
        public double end;
        public string text;
    }

    [Serializable] public sealed class IntroStageData
    {
        public int schema;
        public double duration;
        public bool captions_default;
        public double controls_idle_seconds;
        public IntroShotData[] shots;
        // Schema3 dictionaries and mixed arrays are decoded by IntroStageJson,
        // not JsonUtility. Legacy shot metadata remains available to callers.
        public IntroStage3Data Stage3;
    }

    [Serializable] public sealed class IntroShotData
    {
        public string id;
        public int block;
        public double start;
        public double end;
        public string asset;
        public int quadrant;
        public float[] view_from;
        public float[] view_to;
        public IntroLayerData[] layers;
        public string label;
        public string effect;
    }

    [Serializable] public sealed class IntroLayerData
    {
        public string asset;
        public int quadrant;
        public float[] @from;
        public float[] to;
        public float alpha;
    }

    public readonly struct IntroRect
    {
        public readonly double X, Y, Width, Height;
        public IntroRect(double x, double y, double width, double height)
        { X = x; Y = y; Width = width; Height = height; }
    }

    public static class IntroStageGeometry
    {
        public static IntroRect Lerp(float[] from, float[] to, double progress)
        {
            var t = Math.Max(0, Math.Min(1, progress));
            return new IntroRect(from[0] + (to[0] - from[0]) * t,
                from[1] + (to[1] - from[1]) * t,
                from[2] + (to[2] - from[2]) * t,
                from[3] + (to[3] - from[3]) * t);
        }

        public static IntroRect Quadrant(double textureWidth, double textureHeight, int quadrant)
        {
            var w = textureWidth * .5; var h = textureHeight * .5;
            return new IntroRect((quadrant % 2) * w, (quadrant / 2) * h, w, h);
        }

        /// <summary>Source pixel rectangle in PNG/Godot top-left coordinates.</summary>
        public static IntroRect BackgroundSource(double textureWidth, double textureHeight,
            int quadrant, IntroRect view, double targetAspect = 1280.0 / 720.0)
        {
            var quad = Quadrant(textureWidth, textureHeight, quadrant);
            var x = quad.X + view.X * quad.Width; var y = quad.Y + view.Y * quad.Height;
            var w = view.Width * quad.Width; var h = view.Height * quad.Height;
            if (w / h > targetAspect) { var next = h * targetAspect; x += (w - next) * .5; w = next; }
            else { var next = w / targetAspect; y += (h - next) * .5; h = next; }
            return new IntroRect(x, y, w, h);
        }

        /// <summary>Unity texture UV starts at the bottom left; PNG crops do not.</summary>
        public static IntroRect ToBottomLeftUv(IntroRect source, double textureWidth, double textureHeight)
        {
            return new IntroRect(source.X / textureWidth,
                1 - (source.Y + source.Height) / textureHeight,
                source.Width / textureWidth, source.Height / textureHeight);
        }

        public static IntroRect Viewport(double screenWidth, double screenHeight)
        {
            var w = screenWidth; var h = w * 720 / 1280;
            if (h > screenHeight) { h = screenHeight; w = h * 1280 / 720; }
            return new IntroRect((screenWidth - w) * .5, (screenHeight - h) * .5, w, h);
        }

        public static IntroShotData ShotAt(IntroStageData stage, double seconds)
        {
            if (stage == null || stage.shots == null || stage.shots.Length == 0) return null;
            for (var i = 0; i < stage.shots.Length; i++)
            { var shot = stage.shots[i]; if (seconds >= shot.start && seconds < shot.end) return shot; }
            return seconds < stage.shots[0].start ? stage.shots[0] : stage.shots[stage.shots.Length - 1];
        }
    }

    public static class IntroDataValidation
    {
        public static bool Validate(IntroBookData book, IntroStageData stage, out string error)
        {
            error = null;
            if (book == null || stage == null || book.blocks == null || book.blocks.Length == 0 ||
                (stage.schema != 1 && stage.schema != 3) || stage.shots == null || stage.shots.Length == 0)
            { error = "Missing book or unsupported PNG storyboard schema."; return false; }
            double total = 0;
            foreach (var block in book.blocks)
            {
                if (block == null || string.IsNullOrEmpty(block.text) || string.IsNullOrEmpty(block.audio) ||
                    !Positive(block.duration) || block.timed_captions == null || block.timed_captions.Length == 0)
                { error = "Missing spoken block or caption track."; return false; }
                double before = 0; var spoken = new StringBuilder();
                foreach (var cue in block.timed_captions)
                {
                    if (cue == null || !Finite(cue.start) || !Finite(cue.end) || cue.end <= cue.start ||
                        Math.Abs(cue.start - before) > .002 || string.IsNullOrEmpty(cue.text))
                    { error = "Caption track is not contiguous."; return false; }
                    if (spoken.Length > 0) spoken.Append(' '); spoken.Append(cue.text); before = cue.end;
                }
                if (spoken.ToString() != block.text || Math.Abs(before - block.duration) > .002)
                { error = "Caption content does not cover the preserved monologue."; return false; }
                total += block.duration;
            }
            if (!Positive(book.duration) || !Positive(stage.duration) || !Finite(stage.controls_idle_seconds) || stage.controls_idle_seconds < 0 ||
                Math.Abs(book.duration - total) > .002 || Math.Abs(stage.duration - total) > .002)
            { error = "Book, voice blocks and visual duration disagree."; return false; }
            if (stage.schema == 3)
            {
                if (stage.Stage3 == null || stage.Stage3.Shots == null || stage.Stage3.Shots.Length != stage.shots.Length ||
                    Math.Abs(stage.Stage3.Duration - total) > .002)
                { error = "Schema3 must be decoded and validated by IntroStageJson."; return false; }
                return true;
            }
            double previous = 0;
            foreach (var shot in stage.shots)
            {
                if (shot == null || !Finite(shot.start) || !Finite(shot.end) || shot.end <= shot.start ||
                    Math.Abs(shot.start - previous) > .002 || string.IsNullOrEmpty(shot.asset) ||
                    shot.quadrant < 0 || shot.quadrant > 3 || !ValidRect(shot.view_from, true) || !ValidRect(shot.view_to, true))
                { error = "Invalid or discontinuous PNG shot."; return false; }
                foreach (var layer in shot.layers ?? Array.Empty<IntroLayerData>())
                {
                    if (layer == null || string.IsNullOrEmpty(layer.asset) || layer.quadrant < 0 || layer.quadrant > 3 ||
                        !ValidRect(layer.@from, false) || !ValidRect(layer.to, false) || !Scalar.IsFinite(layer.alpha) || layer.alpha < 0 || layer.alpha > 1)
                    { error = "Invalid independent PNG layer."; return false; }
                }
                previous = shot.end;
            }
            if (Math.Abs(previous - total) > .002) { error = "Picture ends before the monologue."; return false; }
            return true;
        }
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static bool Positive(double n) => Finite(n) && n > 0;
        private static bool ValidRect(float[] r, bool source)
        {
            if (r == null || r.Length != 4 || r[2] <= 0 || r[3] <= 0) return false;
            foreach (var value in r) if (!Scalar.IsFinite(value)) return false;
            return !source || (r[0] >= 0 && r[1] >= 0 && r[0] + r[2] <= 1.001 && r[1] + r[3] <= 1.001);
        }
    }

    public static class IntroJsonDefaults
    {
        // JsonUtility gives missing float fields zero, bypassing constructors.
        // Insert only the absent optional layer alpha in memory; source JSON and
        // its hashes stay untouched, and an explicitly authored alpha=0 survives.
        public static string WithLayerAlpha(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            var stack = new Stack<JsonContainer>(); var insertions = new List<int>();
            for (var i = 0; i < json.Length; i++)
            {
                var c = json[i];
                if (c == '"')
                {
                    var start = ++i;
                    while (i < json.Length && json[i] != '"') { if (json[i] == '\\') i++; i++; }
                    if (i >= json.Length) break;
                    var next = i + 1; while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                    if (stack.Count > 0 && stack.Peek().IsObject && next < json.Length && json[next] == ':')
                    {
                        var key = json.Substring(start, i - start);
                        stack.Peek().Fields.Add(key); stack.Peek().LastProperty = key;
                    }
                }
                else if (c == '{')
                    stack.Push(new JsonContainer(i + 1, true, false,
                        stack.Count > 0 && !stack.Peek().IsObject && stack.Peek().IsLayerArray));
                else if (c == '[')
                    stack.Push(new JsonContainer(i + 1, false,
                        stack.Count > 0 && stack.Peek().IsObject && stack.Peek().LastProperty == "layers", false));
                else if (c == ']' && stack.Count > 0 && !stack.Peek().IsObject) stack.Pop();
                else if (c == '}' && stack.Count > 0)
                {
                    var obj = stack.Pop();
                    if (obj.IsLayer && obj.Fields.Contains("asset") && obj.Fields.Contains("from") && obj.Fields.Contains("to") && !obj.Fields.Contains("alpha"))
                        insertions.Add(obj.Start);
                }
            }
            insertions.Sort(); var result = new StringBuilder(json);
            for (var i = insertions.Count - 1; i >= 0; i--) result.Insert(insertions[i], "\"alpha\":1,");
            return result.ToString();
        }
        private sealed class JsonContainer
        {
            public readonly int Start;
            public readonly bool IsObject, IsLayerArray, IsLayer;
            public string LastProperty;
            public readonly HashSet<string> Fields = new HashSet<string>(StringComparer.Ordinal);
            public JsonContainer(int start, bool isObject, bool isLayerArray, bool isLayer)
            { Start = start; IsObject = isObject; IsLayerArray = isLayerArray; IsLayer = isLayer; }
        }
    }
}
