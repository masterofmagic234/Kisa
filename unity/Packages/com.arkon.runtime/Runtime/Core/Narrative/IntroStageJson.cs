using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>
    /// Converts the unchanged PNG storyboard JSON to portable typed data.
    /// Schema1 remains supported; schema3 preserves explicit atlas regions,
    /// panels, pose tracks, wheel cycles and formation order.
    /// </summary>
    public static class IntroStageJson
    {
        public static IntroStageData Parse(string json)
        {
            var root = Object(PortableJson.Parse(json), "$stage");
            var schema = Integer(Required(root, "schema", "$stage"), "$stage.schema");
            if (schema != 1 && schema != 3) throw Invalid("$stage.schema", "Only PNG schema1 and schema3 are supported");
            var stage = new IntroStageData
            {
                schema = schema,
                duration = Number(Required(root, "duration", "$stage"), "$stage.duration"),
                captions_default = BoolField(root, "captions_default", "$stage", false),
                controls_idle_seconds = NumberField(root, "controls_idle_seconds", "$stage", 2.25)
            };
            if (stage.duration <= 0 || stage.controls_idle_seconds < 0) throw Invalid("$stage", "Invalid duration or controls timeout");
            if (schema == 1) stage.shots = LegacyShots(root, stage.duration);
            else
            {
                stage.Stage3 = Stage3(root, stage.duration);
                stage.shots = new IntroShotData[stage.Stage3.Shots.Length];
                for (var i = 0; i < stage.shots.Length; i++)
                {
                    var shot = stage.Stage3.Shots[i];
                    stage.shots[i] = new IntroShotData
                    {
                        id = shot.Id, start = shot.Start, end = shot.End, block = shot.Block,
                        label = shot.Label, layers = Array.Empty<IntroLayerData>()
                    };
                }
            }
            return stage;
        }

        private static IntroShotData[] LegacyShots(IDictionary<string, object> root, double duration)
        {
            var records = ArrayValue(Required(root, "shots", "$stage"), "$stage.shots");
            if (records.Count == 0) throw Invalid("$stage.shots", "Storyboard must have at least one shot");
            var shots = new IntroShotData[records.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal); double previous = 0;
            for (var i = 0; i < records.Count; i++)
            {
                var path = "$stage.shots[" + i + "]"; var row = Object(records[i], path);
                var shot = new IntroShotData
                {
                    id = String(Required(row, "id", path), path + ".id"),
                    start = Number(Required(row, "start", path), path + ".start"),
                    end = Number(Required(row, "end", path), path + ".end"),
                    block = Integer(Required(row, "block", path), path + ".block"),
                    asset = String(Required(row, "asset", path), path + ".asset"),
                    quadrant = Quadrant(Required(row, "quadrant", path), path + ".quadrant"),
                    view_from = FloatRect(Required(row, "view_from", path), path + ".view_from", true),
                    view_to = FloatRect(Required(row, "view_to", path), path + ".view_to", true),
                    label = StringField(row, "label", path, ""),
                    effect = StringField(row, "effect", path, ""),
                    layers = Array.Empty<IntroLayerData>()
                };
                Span(shot.id, shot.block, shot.start, shot.end, previous, ids, path); previous = shot.end;
                if (row.TryGetValue("layers", out var rawLayers))
                {
                    var layers = ArrayValue(rawLayers, path + ".layers"); shot.layers = new IntroLayerData[layers.Count];
                    for (var j = 0; j < layers.Count; j++)
                    {
                        var lp = path + ".layers[" + j + "]"; var layer = Object(layers[j], lp);
                        var alpha = NumberField(layer, "alpha", lp, 1);
                        if (alpha < 0 || alpha > 1) throw Invalid(lp + ".alpha", "Opacity must be between zero and one");
                        shot.layers[j] = new IntroLayerData
                        {
                            asset = String(Required(layer, "asset", lp), lp + ".asset"),
                            quadrant = Quadrant(Required(layer, "quadrant", lp), lp + ".quadrant"),
                            @from = FloatRect(Required(layer, "from", lp), lp + ".from", false),
                            to = FloatRect(Required(layer, "to", lp), lp + ".to", false), alpha = (float)alpha
                        };
                    }
                }
                shots[i] = shot;
            }
            End(previous, duration);
            return shots;
        }

        private static IntroStage3Data Stage3(IDictionary<string, object> root, double duration)
        {
            var plan = new IntroStage3Data { Duration = duration };
            var art = Object(Required(root, "art", "$stage"), "$stage.art");
            if (art.Count == 0) throw Invalid("$stage.art", "The art catalog is empty");
            foreach (var entry in art)
            {
                var path = "$stage.art[" + entry.Key + "]"; var values = ArrayValue(entry.Value, path);
                if (string.IsNullOrEmpty(entry.Key) || values.Count != 5) throw Invalid(path, "An art region needs id, asset and four source coordinates");
                var x = Number(values[1], path); var y = Number(values[2], path);
                var width = Number(values[3], path); var height = Number(values[4], path);
                if (x < 0 || y < 0 || width <= 0 || height <= 0) throw Invalid(path, "Invalid atlas source rectangle");
                plan.Art.Add(entry.Key, new IntroArtRegion
                { Id = entry.Key, Asset = String(values[0], path), Source = new IntroRect(x, y, width, height) });
            }
            if (root.TryGetValue("poses", out var rawPoses))
                foreach (var entry in Object(rawPoses, "$stage.poses"))
                {
                    var path = "$stage.poses[" + entry.Key + "]"; var values = ArrayValue(entry.Value, path);
                    if (string.IsNullOrEmpty(entry.Key) || values.Count == 0) throw Invalid(path, "A pose needs a nonempty frame sequence");
                    var frames = new string[values.Count];
                    for (var i = 0; i < frames.Length; i++) frames[i] = ArtRef(plan, values[i], path);
                    plan.Poses.Add(entry.Key, frames);
                }
            if (root.TryGetValue("wheels", out var rawWheels))
                foreach (var entry in Object(rawWheels, "$stage.wheels"))
                {
                    var path = "$stage.wheels[" + entry.Key + "]"; ArtRef(plan, entry.Key, path);
                    var values = ArrayValue(entry.Value, path); var wheels = new IntroWheelData[values.Count];
                    for (var i = 0; i < wheels.Length; i++)
                    {
                        var wheel = NumbersAndName(values[i], 3, path);
                        var pose = PoseRef(plan, wheel[0], path);
                        wheels[i] = new IntroWheelData { Pose = pose, X = Number(wheel[1], path), Y = Number(wheel[2], path) };
                    }
                    plan.Wheels.Add(entry.Key, wheels);
                }
            if (root.TryGetValue("formations", out var rawFormations))
                foreach (var entry in Object(rawFormations, "$stage.formations"))
                {
                    var path = "$stage.formations[" + entry.Key + "]";
                    if (string.IsNullOrEmpty(entry.Key)) throw Invalid(path, "Empty formation id");
                    var fleet = Object(entry.Value, path);
                    var projection = String(Required(fleet, "projection", path), path + ".projection");
                    if (projection != "avenue" && projection != "side") throw Invalid(path + ".projection", "Unknown vehicle projection");
                    var values = ArrayValue(Required(fleet, "vehicles", path), path + ".vehicles");
                    var vehicles = new IntroVehicleData[values.Count];
                    for (var i = 0; i < vehicles.Length; i++)
                    {
                        var vp = path + ".vehicles[" + i + "]"; var v = NumbersAndName(values[i], 6, vp);
                        var width = Number(v[3], vp);
                        if (width <= 0) throw Invalid(vp, "Vehicle near width must be positive");
                        vehicles[i] = new IntroVehicleData
                        {
                            Art = ArtRef(plan, v[0], vp), X = Number(v[1], vp), DepthOrY = Number(v[2], vp),
                            NearWidth = width, AnchorX = Number(v[4], vp), AnchorY = Number(v[5], vp)
                        };
                    }
                    plan.Formations.Add(entry.Key, new IntroFleetData { Projection = projection, Vehicles = vehicles });
                }

            var shots = ArrayValue(Required(root, "shots", "$stage"), "$stage.shots");
            if (shots.Count == 0) throw Invalid("$stage.shots", "Storyboard must have at least one shot");
            plan.Shots = new IntroPanelShotData[shots.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal); double previous = 0;
            for (var i = 0; i < shots.Count; i++)
            {
                var path = "$stage.shots[" + i + "]"; var row = Object(shots[i], path);
                var shot = new IntroPanelShotData
                {
                    Id = String(Required(row, "id", path), path + ".id"),
                    Block = Integer(Required(row, "block", path), path + ".block"),
                    Start = Number(Required(row, "start", path), path + ".start"),
                    End = Number(Required(row, "end", path), path + ".end"),
                    Label = StringField(row, "label", path, "")
                };
                Span(shot.Id, shot.Block, shot.Start, shot.End, previous, ids, path); previous = shot.End;
                var panels = ArrayValue(Required(row, "panels", path), path + ".panels");
                shot.Panels = new IntroPanelData[panels.Count];
                for (var j = 0; j < panels.Count; j++) shot.Panels[j] = Panel(plan, panels[j], shot.End - shot.Start, path + ".panels[" + j + "]");
                plan.Shots[i] = shot;
            }
            End(previous, duration);
            return plan;
        }

        private static IntroPanelData Panel(IntroStage3Data plan, object value, double duration, string path)
        {
            var row = Object(value, path);
            var panel = new IntroPanelData
            {
                Crop = Rect(Required(row, "crop", path), path + ".crop"),
                Reveal = NumberField(row, "reveal", path, 0),
                Mirror = BoolField(row, "mirror", path, false)
            };
            if (panel.Reveal < 0 || panel.Reveal >= duration) throw Invalid(path + ".reveal", "Panel reveal is outside its shot");
            if (row.TryGetValue("rect", out var placement)) panel.Rect = Rect(placement, path + ".rect");
            if (row.TryGetValue("art", out var art)) panel.Art = ArtRef(plan, art, path + ".art");
            if (row.TryGetValue("tone", out var tone)) panel.Tone = Numbers(tone, 3, path + ".tone");
            if (row.TryGetValue("camera", out var camera)) panel.Camera = Track(camera, 3, path + ".camera");
            if (row.TryGetValue("behind", out var behind)) panel.Behind = Layers(plan, behind, path + ".behind");
            if (row.TryGetValue("layers", out var layers)) panel.Layers = Layers(plan, layers, path + ".layers");
            if (row.TryGetValue("traffic", out var traffic))
            {
                var data = Object(traffic, path + ".traffic");
                var fleet = String(Required(data, "fleet", path + ".traffic"), path + ".traffic.fleet");
                if (!plan.Formations.ContainsKey(fleet)) throw Invalid(path + ".traffic.fleet", "Unknown formation reference");
                panel.Traffic = new IntroTrafficData
                {
                    Fleet = fleet, Rate = Number(Required(data, "rate", path + ".traffic"), path + ".traffic.rate"),
                    Offset = NumberField(data, "offset", path + ".traffic", 0)
                };
            }
            return panel;
        }

        private static IntroSpriteLayer[] Layers(IntroStage3Data plan, object value, string path)
        {
            var records = ArrayValue(value, path); var layers = new IntroSpriteLayer[records.Count];
            for (var i = 0; i < layers.Length; i++)
            {
                var lp = path + "[" + i + "]"; var row = Object(records[i], lp);
                var at = Numbers(Required(row, "at", lp), 2, lp + ".at");
                var layer = new IntroSpriteLayer
                { X = at[0], Y = at[1], Scale = NumberField(row, "scale", lp, 1), Ease = BoolField(row, "ease", lp, true) };
                if (layer.Scale <= 0) throw Invalid(lp + ".scale", "Sprite scale must be positive");
                if (row.TryGetValue("art", out var art)) layer.Art = ArtRef(plan, art, lp + ".art");
                if (row.TryGetValue("pose", out var pose)) layer.Pose = PoseRef(plan, pose, lp + ".pose");
                if (layer.Art == null && layer.Pose == null) throw Invalid(lp, "Layer needs artwork or a pose");
                if (row.TryGetValue("motion", out var motion)) layer.Motion = Track(motion, 4, lp + ".motion");
                if (row.TryGetValue("phase", out var phase)) layer.Phase = Track(phase, 2, lp + ".phase");
                if (row.TryGetValue("wipe", out var wipe)) layer.Wipe = Track(wipe, 2, lp + ".wipe");
                if (layer.Pose != null && layer.Phase.Length == 0) throw Invalid(lp + ".phase", "An animated pose needs a phase track");
                layers[i] = layer;
            }
            return layers;
        }

        private static double[][] Track(object value, int columns, string path)
        {
            var records = ArrayValue(value, path);
            if (records.Count == 0) throw Invalid(path, "Animation track must not be empty");
            var result = new double[records.Count][]; double previous = -1;
            for (var i = 0; i < result.Length; i++)
            {
                var key = Numbers(records[i], columns, path + "[" + i + "]");
                if (key[0] < 0 || key[0] <= previous) throw Invalid(path, "Track times must increase strictly from a nonnegative time");
                previous = key[0]; result[i] = key;
            }
            return result;
        }

        private static void Span(string id, int block, double start, double end, double previous,
            HashSet<string> ids, string path)
        {
            if (!ids.Add(id) || block < 0 || start < 0 || end <= start || Math.Abs(start - previous) > .002)
                throw Invalid(path, "Invalid id, block, span or discontinuous timeline");
        }
        private static void End(double previous, double duration)
        { if (Math.Abs(previous - duration) > .002) throw Invalid("$stage.shots", "Timeline does not cover the declared duration"); }
        private static string ArtRef(IntroStage3Data plan, object value, string path)
        {
            var id = String(value, path);
            if (!plan.Art.ContainsKey(id)) throw Invalid(path, "Unknown art reference");
            return id;
        }
        private static string PoseRef(IntroStage3Data plan, object value, string path)
        {
            var id = String(value, path);
            if (!plan.Poses.ContainsKey(id)) throw Invalid(path, "Unknown pose reference");
            return id;
        }
        private static int Quadrant(object value, string path)
        {
            var q = Integer(value, path); if (q < 0 || q > 3) throw Invalid(path, "Atlas quadrant must be zero through three"); return q;
        }
        private static IntroRect Rect(object value, string path)
        {
            var r = Numbers(value, 4, path);
            if (r[2] <= 0 || r[3] <= 0) throw Invalid(path, "Rectangle extent must be positive");
            return new IntroRect(r[0], r[1], r[2], r[3]);
        }
        private static float[] FloatRect(object value, string path, bool normalizedSource)
        {
            var r = Numbers(value, 4, path);
            if (r[2] <= 0 || r[3] <= 0 || (normalizedSource &&
                (r[0] < 0 || r[1] < 0 || r[0] + r[2] > 1.001 || r[1] + r[3] > 1.001))) throw Invalid(path, "Invalid legacy rectangle");
            var result = new float[4];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = (float)r[i];
                if (float.IsNaN(result[i]) || float.IsInfinity(result[i])) throw Invalid(path, "Rectangle exceeds float coordinates");
            }
            if (result[2] <= 0 || result[3] <= 0) throw Invalid(path, "Rectangle is too small for float coordinates");
            return result;
        }
        private static double[] Numbers(object value, int count, string path)
        {
            var values = ArrayValue(value, path); if (values.Count != count) throw Invalid(path, "Wrong numeric tuple size");
            var result = new double[count]; for (var i = 0; i < count; i++) result[i] = Number(values[i], path + "[" + i + "]"); return result;
        }
        private static IList<object> NumbersAndName(object value, int count, string path)
        {
            var values = ArrayValue(value, path); if (values.Count != count) throw Invalid(path, "Wrong named tuple size");
            return values;
        }
        private static object Required(IDictionary<string, object> value, string key, string path)
        { if (!value.TryGetValue(key, out var result)) throw Invalid(path + "." + key, "Missing required property"); return result; }
        private static IDictionary<string, object> Object(object value, string path)
        { return value as IDictionary<string, object> ?? throw Invalid(path, "Expected object"); }
        private static IList<object> ArrayValue(object value, string path)
        { return value as IList<object> ?? throw Invalid(path, "Expected array"); }
        private static double Number(object value, string path)
        {
            if (!(value is double number) || double.IsNaN(number) || double.IsInfinity(number)) throw Invalid(path, "Expected finite number");
            return number;
        }
        private static int Integer(object value, string path)
        {
            var number = Number(value, path);
            if (number < int.MinValue || number > int.MaxValue || number != Math.Truncate(number)) throw Invalid(path, "Expected integer");
            return (int)number;
        }
        private static string String(object value, string path, bool empty = false)
        {
            if (!(value is string text) || (!empty && text.Length == 0)) throw Invalid(path, "Expected nonempty string");
            return text;
        }
        private static double NumberField(IDictionary<string, object> row, string key, string path, double fallback)
            => row.TryGetValue(key, out var value) ? Number(value, path + "." + key) : fallback;
        private static string StringField(IDictionary<string, object> row, string key, string path, string fallback)
            => row.TryGetValue(key, out var value) ? String(value, path + "." + key, true) : fallback;
        private static bool BoolField(IDictionary<string, object> row, string key, string path, bool fallback)
        {
            if (!row.TryGetValue(key, out var value)) return fallback;
            if (!(value is bool flag)) throw Invalid(path + "." + key, "Expected boolean");
            return flag;
        }
        private static FormatException Invalid(string path, string message) => new FormatException(message + " at " + path + ".");
    }
}
