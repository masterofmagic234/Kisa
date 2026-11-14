using System;
using System.Collections.Generic;
using Arkon.Core;

public static class NarrativeCases
{
    public static void Run(Action<bool, string> check)
    {
        CheckDecodedTimeCues(check);
        CheckReaderHoldAndOwnership(check);
        CheckCompletionAndReentry(check);
        CheckIntroGeometry(check);
        CheckLayerDefaults(check);
    }

    private static void CheckDecodedTimeCues(Action<bool, string> check)
    {
        var track = new CueTrack();
        var first = new CaptionCue(0, 1, "first");
        var second = new CaptionCue(1, 2, "second");
        check(track.Configure(new[] { first, second }), "Caption track accepts ordered decoded intervals");
        check(track.At(0).Text == "first" && track.At(.999).Text == "first", "First caption covers its half-open interval");
        check(track.At(1).Text == "second" && track.At(2) == null, "Caption boundaries select next cue and exclude the final end");
        check(track.At(1.8).Text == "second" && track.At(.2).Text == "first", "Backward decoded seek restores an earlier caption");
        check(track.At(-2) == null && track.At(double.NaN) == null, "No caption is invented outside a decoded interval");
        first.Text = "mutated input";
        var queried = track.At(.5);
        queried.Text = "mutated result";
        check(track.At(.5).Text == "first", "Caption input and query results cannot mutate the installed track");
        check(!track.Configure(new[] { new CaptionCue(1, 2, "bad"), new CaptionCue(.5, 1, "order") })
            && track.Count == 2 && track.At(.2).Text == "first", "Invalid replacement leaves the active cue track intact");
        check(!track.Configure(new[] { new CaptionCue(2, 1, "negative span") })
            && !track.Configure(new[] { new CaptionCue(0, double.PositiveInfinity, "unusable") })
            && track.Count == 2, "Invalid cue spans do not partially replace installed captions");
        check(track.Configure(new[] { new CaptionCue(0, 2, "outer"), new CaptionCue(1, 3, "inner") })
            && track.At(1.5).Text == "outer", "Overlapping cues preserve source first-match behavior");
        check(track.Configure(Array.Empty<CaptionCue>()) && track.At(0) == null, "An explicitly empty cue track clears previous cues");
    }

    private static void CheckReaderHoldAndOwnership(Action<bool, string> check)
    {
        var sequence = new ReaderSequence();
        var panel = new ReaderPanel("start", .5);
        var facts = new Dictionary<string, bool> { { "introduced", true } };
        var panelEvents = 0;
        sequence.PanelChanged += (shown, index, total) => { panelEvents++; shown.Text = "listener mutation"; };
        check(sequence.Play("scene", new[] { panel, new ReaderPanel("end") }, facts), "Reader starts a valid sequence");
        panel.Text = "input mutation"; facts["introduced"] = false;
        check(sequence.CurrentPanel.Text == "start" && panelEvents == 1, "Reader deep-copies panels and delivers detached panel notifications");
        sequence.Tick(.25);
        sequence.Tick(4, true);
        check(!sequence.Advance() && sequence.Index == 0 && sequence.HeldSeconds == .25, "Paused time does not satisfy minimum reading hold");
        sequence.Tick(.25);
        check(sequence.Advance() && sequence.Index == 1 && sequence.HeldSeconds == 0 && panelEvents == 2,
            "Advance requires the hold and resets time for the next panel");
        sequence.Tick(.1);
        var held = sequence.HeldSeconds;
        check(!sequence.Play("bad", new[] { new ReaderPanel("valid"), new ReaderPanel("", 0) })
            && sequence.Active && sequence.SequenceId == "scene" && sequence.Index == 1
            && sequence.CurrentPanel.Text == "end" && sequence.HeldSeconds == held,
            "Invalid late panel cannot destroy the current sequence");
        check(!sequence.Play("bad", new[] { new ReaderPanel("bad hold", -.1) })
            && !sequence.Play("bad", Array.Empty<ReaderPanel>())
            && !sequence.Play("", new[] { new ReaderPanel("bad id") })
            && sequence.SequenceId == "scene", "Invalid reader IDs, empty panels and negative holds preserve current state");
        IReadOnlyDictionary<string, bool> outcome = null;
        sequence.Finished += (id, values, skipped) => outcome = values;
        sequence.Skip();
        check(outcome != null && outcome["introduced"] && facts["introduced"] == false,
            "Facts are copied at play and delivered to an owner without mutating its live dictionary");
    }

    private static void CheckCompletionAndReentry(Action<bool, string> check)
    {
        var normal = new ReaderSequence();
        var skipped = new ReaderSequence();
        var facts = new Dictionary<string, bool> { { "complete", true }, { "choice", false } };
        IReadOnlyDictionary<string, bool> normalFacts = null, skippedFacts = null;
        var normalCount = 0; var skippedCount = 0;
        var normalSkipFlag = true; var skipFlag = false;
        normal.Finished += (id, values, wasSkipped) => { normalCount++; normalFacts = values; normalSkipFlag = wasSkipped; };
        skipped.Finished += (id, values, wasSkipped) => { skippedCount++; skippedFacts = values; skipFlag = wasSkipped; };
        normal.Play("same", new[] { new ReaderPanel("one") }, facts);
        skipped.Play("same", new[] { new ReaderPanel("one", 99) }, facts);
        check(normal.Advance() && skipped.Skip(), "Natural finish and explicit skip both complete a sequence");
        check(normalCount == 1 && skippedCount == 1 && !normalSkipFlag && skipFlag
            && normalFacts["complete"] == skippedFacts["complete"] && normalFacts["choice"] == skippedFacts["choice"],
            "Skip and natural completion deliver the same facts exactly once");
        check(!normal.Skip() && !normal.Advance() && !skipped.Skip() && normalCount == 1 && skippedCount == 1,
            "Late completion inputs cannot duplicate outcome delivery");
        var immutable = false;
        try { ((IDictionary<string, bool>)normalFacts)["complete"] = false; }
        catch (NotSupportedException) { immutable = true; }
        check(immutable, "Completion listeners receive an immutable snapshot");

        var reentrant = new ReaderSequence();
        var count = 0;
        reentrant.Finished += (id, values, wasSkipped) =>
        {
            count++;
            if (id == "old") reentrant.Play("next", new[] { new ReaderPanel("new panel", .75) });
        };
        reentrant.Play("old", new[] { new ReaderPanel("old panel") });
        reentrant.Advance();
        check(reentrant.Active && reentrant.SequenceId == "next" && reentrant.Index == 0
            && reentrant.CurrentPanel.Text == "new panel" && reentrant.HeldSeconds == 0 && count == 1,
            "A completion callback can start the next sequence without stale state overwriting it");
        reentrant.Skip();
        check(count == 2 && !reentrant.Active && !reentrant.Skip(), "Reentered sequence has its own single completion");
    }

    private static void CheckIntroGeometry(Action<bool, string> check)
    {
        var first = IntroStageGeometry.Quadrant(200, 100, 0);
        var firstUv = IntroStageGeometry.ToBottomLeftUv(first, 200, 100);
        check(Near(firstUv.X, 0) && Near(firstUv.Y, .5) && Near(firstUv.Width, .5) && Near(firstUv.Height, .5),
            "Top-left atlas cell becomes upper-half Unity UV, not a vertically flipped lower cell");
        var fourth = IntroStageGeometry.Quadrant(200, 100, 3);
        var fourthUv = IntroStageGeometry.ToBottomLeftUv(fourth, 200, 100);
        check(Near(fourthUv.X, .5) && Near(fourthUv.Y, 0) && Near(fourthUv.Width, .5) && Near(fourthUv.Height, .5),
            "Bottom-right atlas cell maps to lower-right Unity UV");

        var squareCover = IntroStageGeometry.BackgroundSource(800, 400, 0, new IntroRect(0, 0, 1, 1), 1);
        check(Near(squareCover.X, 100) && Near(squareCover.Y, 0)
            && Near(squareCover.Width, 200) && Near(squareCover.Height, 200),
            "A wide atlas cell is centrally cropped to a square instead of stretching its figures");
        var wideCover = IntroStageGeometry.BackgroundSource(400, 800, 0, new IntroRect(0, 0, 1, 1), 2);
        check(Near(wideCover.X, 0) && Near(wideCover.Y, 150)
            && Near(wideCover.Width, 200) && Near(wideCover.Height, 100),
            "A tall atlas cell is vertically cropped to the requested wide aspect");
        var authoredCrop = IntroStageGeometry.BackgroundSource(1024, 1024, 3, new IntroRect(.25, .125, .5, .5), 1);
        var authoredUv = IntroStageGeometry.ToBottomLeftUv(authoredCrop, 1024, 1024);
        check(Near(authoredCrop.X, 640) && Near(authoredCrop.Y, 576)
            && Near(authoredUv.X, .625) && Near(authoredUv.Y, .1875),
            "An authored view crop stays relative to its selected atlas cell before UV conversion");

        var portrait = IntroStageGeometry.Viewport(390, 844);
        check(Near(portrait.Width, 390) && Near(portrait.Height, 219.375)
            && Near(portrait.X, 0) && Near(portrait.Y, 312.3125)
            && portrait.X >= 0 && portrait.Y >= 0
            && portrait.X + portrait.Width <= 390 && portrait.Y + portrait.Height <= 844,
            "Portrait screens keep the whole centered 16:9 stage within screen bounds");
        var ultrawide = IntroStageGeometry.Viewport(1920, 720);
        check(Near(ultrawide.X, 320) && Near(ultrawide.Y, 0)
            && Near(ultrawide.Width, 1280) && Near(ultrawide.Height, 720),
            "Ultrawide screens add side space without expanding or distorting stage geometry");

        var stage = new IntroStageData
        {
            shots = new[]
            {
                new IntroShotData { id = "first", start = 0, end = 2 },
                new IntroShotData { id = "second", start = 2, end = 4 }
            }
        };
        check(IntroStageGeometry.ShotAt(stage, 1.999).id == "first"
            && IntroStageGeometry.ShotAt(stage, 2).id == "second",
            "The exact cut boundary selects the following visual shot");
        check(IntroStageGeometry.ShotAt(stage, 3).id == "second"
            && IntroStageGeometry.ShotAt(stage, .25).id == "first",
            "Seeking back across a cut restores the previous visual shot");
        check(IntroStageGeometry.ShotAt(new IntroStageData { shots = Array.Empty<IntroShotData>() }, 0) == null,
            "An empty storyboard does not invent a visual shot");
    }

    private static void CheckLayerDefaults(Action<bool, string> check)
    {
        const string missing = "{\"shots\":[{\"asset\":\"background\",\"layers\":[{\"asset\":\"actor\",\"from\":[0,0,1,1],\"to\":[1,0,1,1]}]}]}";
        var defaulted = IntroJsonDefaults.WithLayerAlpha(missing);
        check(Count(defaulted, "\"alpha\":1,") == 1
            && defaulted.Replace("\"alpha\":1,", "") == missing,
            "An absent layer alpha gains exactly one opaque default without changing authored data");
        check(IntroJsonDefaults.WithLayerAlpha(defaulted) == defaulted,
            "Applying layer defaults twice never creates duplicate alpha fields");

        const string explicitZero = "{\"layers\":[{\"asset\":\"actor\",\"from\":[0,0,1,1],\"to\":[1,0,1,1],\"alpha\":0}]}";
        check(IntroJsonDefaults.WithLayerAlpha(explicitZero) == explicitZero,
            "An explicitly invisible layer remains alpha zero");
        const string explicitFraction = "{\"layers\":[{\"alpha\":0.42,\"asset\":\"actor\",\"from\":[0,0,1,1],\"to\":[1,0,1,1]}]}";
        check(IntroJsonDefaults.WithLayerAlpha(explicitFraction) == explicitFraction,
            "An authored fractional opacity is preserved regardless of property order");

        const string quoted = "{\"label\":\"braces } { and \\\"quoted\\\" \\\"alpha\\\":0\",\"layers\":[{\"asset\":\"actor } { \\\"quoted\\\"\",\"from\":[0,0,1,1],\"to\":[1,0,1,1]}]}";
        var quotedResult = IntroJsonDefaults.WithLayerAlpha(quoted);
        check(Count(quotedResult, "\"alpha\":1,") == 1
            && quotedResult.Replace("\"alpha\":1,", "") == quoted,
            "Braces and escaped quotes inside JSON strings do not corrupt layer-default insertion");

        const string nonLayer = "{\"asset\":\"background\",\"view_from\":[0,0,1,1],\"view_to\":[0,0,1,1],\"metadata\":{\"label\":\"from and to\"}}";
        check(IntroJsonDefaults.WithLayerAlpha(nonLayer) == nonLayer,
            "Background shots and ordinary metadata do not receive a layer opacity field");
        const string lookalike = "{\"metadata\":{\"asset\":\"route\",\"from\":\"start\",\"to\":\"finish\"},\"layers\":[]}";
        check(IntroJsonDefaults.WithLayerAlpha(lookalike) == lookalike,
            "A metadata object resembling layer fields remains untouched outside the layers array");
        check(IntroJsonDefaults.WithLayerAlpha(null) == null && IntroJsonDefaults.WithLayerAlpha("") == "",
            "Empty input stays empty for the actual JSON parser to handle");
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .000001;

    private static int Count(string text, string fragment)
    {
        var count = 0; var start = 0;
        while ((start = text.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += fragment.Length;
        }
        return count;
    }
}
