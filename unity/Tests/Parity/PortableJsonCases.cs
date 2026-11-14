using System;
using System.Collections.Generic;
using System.Globalization;
using Arkon.Core;

public static class PortableJsonCases
{
    public static void Run(Action<bool, string> check)
    {
        JsonValues(check);
        InvalidJson(check);
        LegacyStoryboard(check);
        PanelStoryboard(check);
    }

    private static void JsonValues(Action<bool, string> check)
    {
        var values = (IList<object>)PortableJson.Parse("[null,true,false,0,-12.5e+2,\"1\"]");
        check(values.Count == 6 && values[0] == null && (bool)values[1] && !(bool)values[2]
            && (double)values[3] == 0 && (double)values[4] == -1250 && (string)values[5] == "1",
            "Portable JSON preserves primitive types and ordered mixed arrays");
        var objectValue = (IDictionary<string, object>)PortableJson.Parse("{\"a\":[{\"b\":3}],\"A\":4,\"nil\":null}");
        check(objectValue.Count == 3 && objectValue.ContainsKey("a") && objectValue.ContainsKey("A")
            && objectValue.ContainsKey("nil") && objectValue["nil"] == null
            && (double)((IDictionary<string, object>)((IList<object>)objectValue["a"])[0])["b"] == 3,
            "Portable JSON preserves nested values, case-sensitive keys and explicit null properties");
        var text = (string)PortableJson.Parse("\"Привет \\\"json\\\" \\\\ \\/ \\b\\f\\n\\r\\t \\u0416 \\ud83d\\ude00\"");
        check(text == "Привет \"json\" \\ / \b\f\n\r\t Ж 😀", "Portable JSON decodes escapes and valid Unicode surrogate pairs");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            check((double)PortableJson.Parse("1.25") == 1.25, "JSON numbers use invariant decimal syntax under another process culture");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        check((double)PortableJson.Parse(" \r\n\t1e-3 ") == .001, "JSON whitespace and finite exponent notation are accepted");
    }

    private static void InvalidJson(Action<bool, string> check)
    {
        check(Rejects(() => PortableJson.Parse("{\"a\":1,\"a\":2}"))
            && Rejects(() => PortableJson.Parse("{\"a\":1,\"\\u0061\":2}")),
            "Duplicate keys are rejected even when one spelling uses a Unicode escape");
        var malformed = new[] { "", "[1,]", "{\"a\":1,}", "{a:1}", "{\"a\" 1}", "[1 2]", "true false", "(1)", "True", "\"unfinished", "\"bad\\x\"", "\"raw\nline\"" };
        var allMalformedRejected = true;
        foreach (var value in malformed) allMalformedRejected &= Rejects(() => PortableJson.Parse(value));
        check(allMalformedRejected, "Malformed JSON is rejected instead of returning partial objects");
        var badNumbers = new[] { "+1", "01", "-01", ".1", "1.", "1e", "1e+", "NaN", "Infinity", "-Infinity", "1e999", "-1e999" };
        var allNumbersRejected = true;
        foreach (var value in badNumbers) allNumbersRejected &= Rejects(() => PortableJson.Parse(value));
        check(allNumbersRejected, "Invalid numeric grammar and non-finite numbers are rejected");
        check(Rejects(() => PortableJson.Parse("\"\\ud800\""))
            && Rejects(() => PortableJson.Parse("\"\\udc00\""))
            && Rejects(() => PortableJson.Parse("\"\\ud800\\u0041\"")),
            "Malformed Unicode surrogate sequences are rejected");
        check(Rejects(() => PortableJson.Parse(new string('[', 130) + "0" + new string(']', 130))),
            "Excessive nesting is rejected before a recursion overflow");
        check(Rejects(() => PortableJson.Parse(null)), "Null JSON input is rejected explicitly");
    }

    private const string Legacy = @"{""schema"":1,""duration"":2,""shots"":[
{""id"":""one"",""start"":0,""end"":1,""block"":0,""asset"":""res://assets/intro/base.png"",""quadrant"":0,""view_from"":[0,0,1,1],""view_to"":[0,0,1,1],""layers"":[
{""asset"":""res://assets/intro/actor.png"",""quadrant"":3,""from"":[-0.5,0,1,1],""to"":[1.5,0,1,1]},
{""asset"":""res://assets/intro/actor.png"",""quadrant"":2,""from"":[0,0,1,1],""to"":[1,0,1,1],""alpha"":0}]},
{""id"":""two"",""start"":1,""end"":2,""block"":1,""asset"":""res://assets/intro/base.png"",""quadrant"":1,""view_from"":[0,0,1,1],""view_to"":[0,0,1,1]}]}";

    private static void LegacyStoryboard(Action<bool, string> check)
    {
        var stage = IntroStageJson.Parse(Legacy);
        check(stage.schema == 1 && stage.Stage3 == null && stage.shots.Length == 2
            && stage.duration == 2 && stage.controls_idle_seconds == 2.25 && !stage.captions_default,
            "The strict parser retains legacy schema1 and its presentation defaults");
        check(stage.shots[0].layers[0].alpha == 1 && stage.shots[0].layers[1].alpha == 0
            && stage.shots[0].layers[0].@from[0] == -.5f && stage.shots[0].layers[0].to[0] == 1.5f,
            "Legacy layer opacity defaults preserve authored zero and off-screen movement");
        check(IntroStageGeometry.ShotAt(stage, 1).id == "two" && IntroStageGeometry.ShotAt(stage, .2).id == "one",
            "Parsed legacy shot metadata still supports cut and reverse seek");
        check(Rejects(() => IntroStageJson.Parse(Legacy.Replace("\"quadrant\":3", "\"quadrant\":4")))
            && Rejects(() => IntroStageJson.Parse(Legacy.Replace("\"alpha\":0", "\"alpha\":1.1"))),
            "Legacy quadrant and opacity limits are validated before display");
        check(Rejects(() => IntroStageJson.Parse(Legacy.Replace("\"start\":1", "\"start\":1.1")))
            && Rejects(() => IntroStageJson.Parse(Legacy.Replace("\"schema\":1", "\"schema\":2"))),
            "Discontinuous legacy timelines and unsupported schema2 cannot silently load");
    }

    private const string Panels = @"{""schema"":3,""duration"":2,
""art"":{""plate"":[""res://assets/intro/base.png"",0,0,768,512],""actor"":[""res://assets/intro/actors.png"",2,3,20,40],""wheel"":[""res://assets/intro/wheels.png"",5,6,10,10]},
""poses"":{""look"":[""actor"",""wheel""]},""wheels"":{""actor"":[[""look"",1,2]]},
""formations"":{""fleet"":{""projection"":""avenue"",""vehicles"":[[""actor"",300,2,100,0.5,1],[""actor"",200,1,100,0.5,1]]}},
""shots"":[{""id"":""scene"",""start"":0,""end"":2,""block"":0,""label"":""Memory } {"",""panels"":[
{""art"":""plate"",""crop"":[0,0,768,432],""traffic"":{""fleet"":""fleet"",""rate"":0.12},""layers"":[
{""pose"":""look"",""at"":[10,20],""phase"":[[0,0],[1,1]],""motion"":[[0,0,0,0],[1,2,3,1]],""wipe"":[[0,0],[1,1]]}]}]}]}";

    private static void PanelStoryboard(Action<bool, string> check)
    {
        var wrapper = IntroStageJson.Parse(Panels); var stage = wrapper.Stage3;
        check(wrapper.schema == 3 && stage != null && stage.Art.Count == 3 && stage.Poses.Count == 1
            && stage.Wheels.Count == 1 && stage.Formations.Count == 1 && stage.Shots.Length == 1,
            "Schema3 mixed arrays become typed art, poses, wheels, formations and panel shots");
        check(stage.Art["actor"].Source.X == 2 && stage.Art["actor"].Source.Y == 3
            && stage.Art["actor"].Source.Width == 20 && stage.Poses["look"][1] == "wheel"
            && stage.Wheels["actor"][0].Pose == "look" && stage.Wheels["actor"][0].Y == 2,
            "Schema3 atlas regions and wheel frame references retain their source values");
        var vehicles = stage.Formations["fleet"].Vehicles;
        check(vehicles.Length == 2 && vehicles[0].DepthOrY == 2 && vehicles[1].DepthOrY == 1
            && vehicles[0].NearWidth == 100 && vehicles[0].AnchorX == .5,
            "Vehicle array order and perspective parameters are preserved without sorting");
        var panel = stage.Shots[0].Panels[0]; var layer = panel.Layers[0];
        check(panel.Rect.Width == 768 && panel.Rect.Height == 432 && panel.Reveal == 0 && !panel.Mirror
            && panel.Tone[0] == 1 && panel.Camera.Length == 0 && panel.Behind.Length == 0
            && panel.Traffic.Offset == 0 && layer.Scale == 1 && layer.Ease && layer.X == 10 && layer.Y == 20,
            "Panel and sprite defaults match source compositor semantics");
        check(layer.Phase[1][1] == 1 && layer.Motion[1][2] == 3 && layer.Motion[0][3] == 0 && layer.Wipe[1][1] == 1
            && wrapper.shots[0].id == "scene" && wrapper.shots[0].label == "Memory } {"
            && wrapper.shots[0].start == 0 && wrapper.shots[0].end == 2,
            "Animation samples and compatibility shot metadata retain source values");

        const string blackout = "{\"schema\":3,\"duration\":1,\"art\":{\"plate\":[\"res://assets/intro/base.png\",0,0,768,512]},\"shots\":[{\"id\":\"blackout\",\"start\":0,\"end\":1,\"block\":0,\"panels\":[]}]}";
        check(IntroStageJson.Parse(blackout).Stage3.Shots[0].Panels.Length == 0,
            "A source-authored empty panel list is a valid matte blackout shot");

        check(Rejects(() => IntroStageJson.Parse(Panels.Replace("\"art\":\"plate\"", "\"art\":\"absent\"")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace("\"pose\":\"look\"", "\"pose\":\"absent\"")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace("\"fleet\":\"fleet\"", "\"fleet\":\"absent\""))),
            "Unknown art, pose and formation references are rejected before rendering");
        check(Rejects(() => IntroStageJson.Parse(Panels.Replace("\"look\":[\"actor\",\"wheel\"]", "\"look\":[]")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace(",\"phase\":[[0,0],[1,1]]", ""))),
            "Empty poses and pose layers without a phase track are rejected");
        check(Rejects(() => IntroStageJson.Parse(Panels.Replace("\"motion\":[[0,0,0,0],[1,2,3,1]]", "\"motion\":[[0,0,0,0],[1,2,1]]")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace("\"phase\":[[0,0],[1,1]]", "\"phase\":[[1,0],[0,1]]")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace("\"phase\":[[0,0],[1,1]]", "\"phase\":[[0,0],[0,1]]"))),
            "Track tuple widths and strictly increasing times are validated");
        check(Rejects(() => IntroStageJson.Parse(Panels.Replace("\"projection\":\"avenue\"", "\"projection\":\"unknown\"")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace("\"rate\":0.12", "\"rate\":\"0.12\"")))
            && Rejects(() => IntroStageJson.Parse(Panels.Replace("2,3,20,40", "2,3,0,40"))),
            "Projection names, numeric types and positive atlas extents are validated");
    }

    private static bool Rejects(Action action)
    {
        try { action(); return false; }
        catch (FormatException) { return true; }
        catch (ArgumentNullException) { return true; }
    }
}
