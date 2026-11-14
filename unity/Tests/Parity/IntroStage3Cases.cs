using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Arkon.Core;

/// <summary>
/// Checks the current PNG compositor against draw records emitted by the actual
/// Godot script. These are geometry/data checks, not Unity rendering or artistic
/// acceptance. The pinned counts describe this source fixture, not a cut quota.
/// </summary>
public static class IntroStage3Cases
{
    private const double PixelTolerance = .002;
    private const double TintTolerance = .00001;

    public static void Run(Action<bool, string> check)
    {
        CheckTracks(check);
        CheckPoseRounding(check);
        CheckWheelModulo(check);
        CheckAvenueCutoff(check);
        CheckActualGodotTrace(check);
    }

    private static void CheckTracks(Action<bool, string> check)
    {
        var keys = new[] { new[] { 0.0, 10.0, 20.0 }, new[] { 4.0, 50.0, 60.0 } };
        var linear = IntroStageCompositor.Track(keys, 1, false);
        var smooth = IntroStageCompositor.Track(keys, 1, true);
        check(Near(linear[0], 20, 1e-10) && Near(linear[1], 30, 1e-10),
            "Schema3 linear motion uses elapsed/key duration for every coordinate");
        check(Near(smooth[0], 16.25, 1e-10) && Near(smooth[1], 26.25, 1e-10),
            "Schema3 camera/phase easing preserves source smoothstep timing");
        var first = IntroStageCompositor.Track(keys, -8, true);
        var last = IntroStageCompositor.Track(keys, 99, true);
        check(Near(first[0], 10, 1e-10) && Near(last[0], 50, 1e-10),
            "Schema3 tracks clamp before first key and hold the final authored value");
        check(Near(IntroStageCompositor.Track(keys, 4, true)[1], 60, 1e-10)
            && Near(IntroStageCompositor.Track(new[] { new[] { 0.0, .25 } }, 8, true)[0], .25, 1e-10),
            "Exact track boundary and single-key poses preserve the authored values");
    }

    private static void CheckPoseRounding(Action<bool, string> check)
    {
        var art = new Dictionary<string, object>
        {
            { "p0", Region(0, 0, 8, 8) }, { "p1", Region(8, 0, 8, 8) }, { "p2", Region(16, 0, 8, 8) }
        };
        var panel = Panel(0, 0, 768, 432);
        panel["layers"] = new object[] { new Dictionary<string, object>
        {
            { "at", new[] { 20.0, 30.0 } }, { "pose", "turn" },
            { "phase", new[] { new[] { 0.0, .25 } } }
        } };
        var poses = new Dictionary<string, object> { { "turn", new[] { "p0", "p1", "p2" } } };
        var stage = Stage(art, panel, poses: poses);
        var draws = IntroStageCompositor.BuildDrawCommands(stage, 0);
        check(draws.Count == 1 && Near(draws[0].Source.X, 8, 1e-10),
            "Pose phase .25 over three frames rounds the .5 tie upward like Godot roundi");
        check(draws.Count == 1 && Near(draws[0].Destination.X, 20, 1e-10)
            && Near(draws[0].Destination.Y, 30, 1e-10),
            "Discrete pose selection keeps the layer's authored placement");
    }

    private static void CheckWheelModulo(Action<bool, string> check)
    {
        var art = new Dictionary<string, object> { { "car", Region(0, 0, 100, 50) } };
        for (var i = 0; i < 4; i++) art.Add("w" + i, Region(100 + i * 10, 0, 10, 10));
        var poses = new Dictionary<string, object> { { "spin", new[] { "w0", "w1", "w2", "w3" } } };
        var wheels = new Dictionary<string, object> { { "car", new object[] { new object[] { "spin", 10.0, 20.0 } } } };
        var fleets = new Dictionary<string, object> { { "side", new Dictionary<string, object>
        {
            { "projection", "side" },
            { "vehicles", new object[] { new object[] { "car", 200.0, 200.0, 100.0, .5, 1.0 } } }
        } } };
        var panel = Panel(0, 0, 768, 432);
        panel["traffic"] = new Dictionary<string, object> { { "fleet", "side" }, { "rate", 1.0 }, { "offset", -10.0 } };
        var reverse = IntroStageCompositor.BuildDrawCommands(Stage(art, panel, poses, wheels, fleets), 0);
        check(reverse.Count == 2 && Near(reverse[1].Source.X, 120, 1e-10),
            "Negative traffic distance uses floor then positive wheel modulo, not C# negative remainder");
        check(reverse.Count == 2 && Near(reverse[0].Destination.X, 140, 1e-10)
            && Near(reverse[1].Destination.X, 150, 1e-10) && Near(reverse[1].Destination.Y, 170, 1e-10),
            "Wheel sprites remain attached to the scaled body anchor");
        panel["traffic"] = new Dictionary<string, object> { { "fleet", "side" }, { "rate", 1.0 }, { "offset", 10.0 } };
        var forward = IntroStageCompositor.BuildDrawCommands(Stage(art, panel, poses, wheels, fleets), 0);
        check(forward.Count == 2 && Near(forward[1].Source.X, 110, 1e-10),
            "Forward traffic selects its wheel frame from traveled ground distance");
    }

    private static void CheckAvenueCutoff(Action<bool, string> check)
    {
        var art = new Dictionary<string, object> { { "car", Region(0, 0, 100, 50) } };
        var fleets = new Dictionary<string, object> { { "near", new Dictionary<string, object>
        {
            { "projection", "avenue" },
            { "vehicles", new object[] { new object[] { "car", 689.0, .58, 100.0, .5, 1.0 } } }
        } } };
        var panel = Panel(0, 550, 768, 432);
        panel["traffic"] = new Dictionary<string, object> { { "fleet", "near" }, { "rate", 1.0 } };
        var stage = Stage(art, panel, fleets: fleets);
        var atThreshold = IntroStageCompositor.BuildDrawCommands(stage, 0);
        var pastThreshold = IntroStageCompositor.BuildDrawCommands(stage, .001);
        check(atThreshold.Count == 1 && pastThreshold.Count == 0,
            "Avenue depth .58 is visible; a vehicle that passed below .58 is removed as a whole");
    }

    private static void CheckActualGodotTrace(Action<bool, string> check)
    {
        var oraclePath = Environment.GetEnvironmentVariable("ARKON_INTRO_ORACLE");
        if (string.IsNullOrWhiteSpace(oraclePath)) oraclePath = FindFromWorkingDirectory("unity/Tests/Fixtures/godot-intro-schema3.json");
        check(!string.IsNullOrWhiteSpace(oraclePath) && File.Exists(oraclePath),
            "Actual Godot schema3 draw oracle is available (ARKON_INTRO_ORACLE)");
        if (string.IsNullOrWhiteSpace(oraclePath) || !File.Exists(oraclePath)) return;
        oraclePath = Path.GetFullPath(oraclePath);
        var planPath = Environment.GetEnvironmentVariable("ARKON_INTRO_PLAN");
        if (string.IsNullOrWhiteSpace(planPath))
            planPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(oraclePath), "../../..", "white_guard/data/intro_shots.json"));
        check(File.Exists(planPath), "The exact Godot source plan is available for compositor parity");
        if (!File.Exists(planPath)) return;

        using var oracle = JsonDocument.Parse(File.ReadAllText(oraclePath));
        var root = oracle.RootElement;
        var sourceHashes = root.GetProperty("source_files");
        check(Sha256(planPath) == sourceHashes.GetProperty("data/intro_shots.json").GetString(),
            "Oracle plan SHA-256 matches the current source; changed direction requires a new oracle");
        var sourceRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(planPath), ".."));
        var compositorPath = Path.Combine(sourceRoot, "scripts/intro_stage.gd");
        check(File.Exists(compositorPath) && Sha256(compositorPath) == sourceHashes.GetProperty("scripts/intro_stage.gd").GetString(),
            "Oracle originates from the exact current Godot compositor");
        var stage = IntroStageJson.Parse(File.ReadAllText(planPath));
        check(stage.schema == 3 && stage.Stage3 != null && stage.Stage3.Shots.Length == 84,
            "Current schema3 source fixture loads all 84 authored shots");
        check(stage.Stage3 != null && stage.Stage3.Art.Count == 176 && stage.Stage3.Poses.Count == 17
            && stage.Stage3.Formations.Count == 11 && stage.Stage3.Wheels.Count == 3,
            "Current source fixture retains every atlas region, pose, formation and wheel attachment");
        check(root.GetProperty("canvas")[0].GetInt32() == 768 && root.GetProperty("canvas")[1].GetInt32() == 432,
            "The Godot oracle uses the authored 768×432 composition frame");
        var samples = root.GetProperty("samples");
        check(samples.GetArrayLength() == 363, "The source oracle covers all cuts plus panel-reveal boundary samples");

        double maxSource = 0, maxDestination = 0, maxTint = 0;
        var compared = 0; var mirrored = 0;
        foreach (var sample in samples.EnumerateArray())
        {
            var time = sample.GetProperty("time").GetDouble();
            var expected = sample.GetProperty("draws");
            var actual = IntroStageCompositor.BuildDrawCommands(stage, time);
            var context = "Godot schema3 t=" + time.ToString("G12", CultureInfo.InvariantCulture);
            var shot = IntroStageGeometry.ShotAt(stage, time);
            check(shot != null && shot.id == sample.GetProperty("shot_id").GetString(), context + " selects the original shot");
            check(actual.Count == expected.GetArrayLength(), context + " keeps draw count/order (including clipping and hidden layers)");
            var count = Math.Min(actual.Count, expected.GetArrayLength());
            for (var i = 0; i < count; i++)
            {
                var e = expected[i]; var a = actual[i]; var where = context + " draw=" + i;
                check(a.Texture == e.GetProperty("texture").GetString(), where + " keeps the exact texture and depth order");
                var source = Values(e.GetProperty("source"));
                var destination = Values(e.GetProperty("destination"));
                var mirror = e.GetProperty("mirror").GetBoolean();
                if (mirror) { destination[0] = 2 * e.GetProperty("mirror_center").GetDouble() - destination[0] - destination[2]; mirrored++; }
                var sourceError = Error(RectValues(a.Source), source);
                var destinationError = Error(RectValues(a.Destination), destination);
                var tintError = Error(a.Tint, Values(e.GetProperty("tint")));
                maxSource = Math.Max(maxSource, sourceError); maxDestination = Math.Max(maxDestination, destinationError); maxTint = Math.Max(maxTint, tintError);
                check(sourceError <= PixelTolerance, where + " preserves clipped top-left atlas source; error=" + sourceError.ToString("G9", CultureInfo.InvariantCulture));
                check(destinationError <= PixelTolerance, where + " preserves fitted/mirrored destination; error=" + destinationError.ToString("G9", CultureInfo.InvariantCulture));
                check(tintError <= TintTolerance && a.MirrorX == mirror, where + " preserves tone, opacity and texture mirror");
                compared++;
            }
        }
        check(compared == 1021 && mirrored == 144,
            "Current oracle compared all 1021 sprite draws, including 144 mirrored draws");
        Console.WriteLine("INTRO_SCHEMA3_PARITY samples=" + samples.GetArrayLength() + " draws=" + compared
            + " max_source_px=" + maxSource.ToString("G9", CultureInfo.InvariantCulture)
            + " max_destination_px=" + maxDestination.ToString("G9", CultureInfo.InvariantCulture)
            + " max_tint=" + maxTint.ToString("G9", CultureInfo.InvariantCulture)
            + "; numerical Godot trace only, not a Unity/GPU/artistic test.");
    }

    private static object[] Region(double x, double y, double width, double height) =>
        new object[] { "res://assets/intro/test.png", x, y, width, height };

    private static Dictionary<string, object> Panel(double x, double y, double width, double height) =>
        new Dictionary<string, object> { { "crop", new[] { x, y, width, height } } };

    private static IntroStageData Stage(Dictionary<string, object> art, Dictionary<string, object> panel,
        Dictionary<string, object> poses = null, Dictionary<string, object> wheels = null, Dictionary<string, object> fleets = null)
    {
        var data = new Dictionary<string, object>
        {
            { "schema", 3 }, { "duration", 2.0 }, { "captions_default", false }, { "controls_idle_seconds", 2.25 },
            { "art", art }, { "poses", poses ?? new Dictionary<string, object>() },
            { "wheels", wheels ?? new Dictionary<string, object>() }, { "formations", fleets ?? new Dictionary<string, object>() },
            { "shots", new object[] { new Dictionary<string, object>
                { { "id", "case" }, { "start", 0.0 }, { "end", 2.0 }, { "block", 0 }, { "panels", new object[] { panel } } } } }
        };
        return IntroStageJson.Parse(JsonSerializer.Serialize(data));
    }

    private static string FindFromWorkingDirectory(string relative)
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null)
        { var path = Path.Combine(directory.FullName, relative); if (File.Exists(path)) return path; directory = directory.Parent; }
        return null;
    }
    private static string Sha256(string path)
    { using var sha = SHA256.Create(); return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture))); }
    private static double[] Values(JsonElement array) => array.EnumerateArray().Select(x => x.GetDouble()).ToArray();
    private static double[] RectValues(IntroRect rect) => new[] { rect.X, rect.Y, rect.Width, rect.Height };
    private static bool Near(double a, double b, double tolerance) => !double.IsNaN(a) && !double.IsInfinity(a) && Math.Abs(a - b) <= tolerance;
    private static double Error(double[] actual, double[] expected)
    {
        if (actual == null || actual.Length != expected.Length) return double.PositiveInfinity;
        double max = 0;
        for (var i = 0; i < actual.Length; i++)
        { if (double.IsNaN(actual[i]) || double.IsInfinity(actual[i])) return double.PositiveInfinity; max = Math.Max(max, Math.Abs(actual[i] - expected[i])); }
        return max;
    }
}
