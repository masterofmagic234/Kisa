using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WhiteGuard.Gameplay;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static int checks;
    private static readonly List<string> failures = new List<string>();

    private static int Main(string[] args)
    {
        try { ProgressCases(); TimerCases(); RestoreCases(); ValidationCases(); }
        catch (Exception error) { failures.Add(error.ToString()); }
        var campaignLevels = 0;
        if (args.Length > 0)
        {
            try
            {
                var campaign = JsonSerializer.Deserialize<CampaignData>(File.ReadAllText(args[0]), Json);
                Check(campaign != null && campaign.schema == 1 && !string.IsNullOrWhiteSpace(campaign.id), "Actual campaign has its schema and identity");
                var levels = new HashSet<string>(StringComparer.Ordinal);
                foreach (var level in campaign.levels)
                {
                    Check(levels.Add(level.id), "Actual campaign has unique level ID: " + level.id);
                    var progress = new CampaignProgress(level);
                    Check(progress.Restore(progress.Capture()), "Actual campaign initial checkpoint round-trips: " + level.id);
                    campaignLevels++;
                }
                Check(levels.Contains(campaign.firstLevel), "Actual campaign firstLevel references an existing level");
                CampaignGeometryCases.Run(campaign, Check);
                CampaignCheckpointCases.Run(campaign, Check);
                CampaignCompletionCases.Run(campaign, Check);
            }
            catch (Exception error) { failures.Add("Actual campaign validation: " + error); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = failures.Count == 0 ? "PASS" : "FAIL", checks, failures,
            actual_campaign_validated = args.Length > 0, campaign_levels = campaignLevels,
            objective_witness_completed_levels = CampaignCompletionCases.CompletedLevels,
            scope = "Pure C#9/netstandard2.1 objective/checkpoint domain and static authored-data navigation witnesses. No native Unity, physics, rendering or JsonUtility verification."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return failures.Count == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message) { checks++; if (!condition) failures.Add(message); }
    private static string State(CampaignProgress progress) => JsonSerializer.Serialize(progress.Capture(), Json);
    private static CampaignProgressSnapshot Copy(CampaignProgressSnapshot snapshot)
        => JsonSerializer.Deserialize<CampaignProgressSnapshot>(JsonSerializer.Serialize(snapshot, Json), Json);

    private sealed class World : IObjectiveWorld
    {
        public readonly HashSet<string> Interacted = new HashSet<string>();
        public readonly HashSet<string> PlayerRegions = new HashSet<string>();
        public readonly HashSet<string> ClearedGroups = new HashSet<string>();
        public readonly HashSet<string> Living = new HashSet<string>();
        public readonly HashSet<string> ActorRegions = new HashSet<string>();
        public readonly HashSet<string> Routes = new HashSet<string>();
        public bool HasInteracted(string id) => Interacted.Contains(id);
        public bool PlayerInRegion(string id) => PlayerRegions.Contains(id);
        public bool IsGroupClear(string id) => ClearedGroups.Contains(id);
        public bool IsActorAlive(string id) => Living.Contains(id);
        public bool IsActorInRegion(string actor, string region) => ActorRegions.Contains(actor + "/" + region);
        public bool IsActorRouteComplete(string id) => Routes.Contains(id);
    }

    private static CampaignLevel Level(params CampaignObjective[] objectives)
    {
        return new CampaignLevel
        {
            id = "mission", objectives = objectives,
            actors = new[] { new CampaignActor { id = "vip", name = "Свидетель", health = 100 } },
            interactions = new[] { new CampaignInteraction { id = "switch" } },
            regions = new[] { new CampaignRect { id = "exit" }, new CampaignRect { id = "hold" } },
            enemies = new[] { new CampaignEnemy { id = "guard", group = "guards" } }
        };
    }

    private static CampaignObjective Objective(string id, string type, string target = null, string region = null, float amount = 0, params string[] requires)
        => new CampaignObjective { id = id, text = id, type = type, target = target, region = region, amount = amount, requires = requires };

    private static CampaignLevel MainLevel()
    {
        return Level(
            Objective("protect", "protect", "vip"),
            Objective("start", "interact", "switch"),
            Objective("clear", "clear", "guards", null, 0, "start"),
            Objective("reach", "reach", null, "exit", 0, "clear"),
            Objective("escort", "escort", "vip", "exit", 0, "reach"),
            Objective("hold", "survive", null, "hold", 2, "escort"));
    }

    private static World ReadyWorld()
    {
        var world = new World(); world.Living.Add("vip"); world.Interacted.Add("switch"); world.ClearedGroups.Add("guards");
        world.PlayerRegions.Add("exit"); world.ActorRegions.Add("vip/exit"); world.Routes.Add("vip"); return world;
    }

    private static void ProgressCases()
    {
        var level = MainLevel(); var progress = new CampaignProgress(level); var world = new World(); world.Living.Add("vip");
        Check(!progress.AllObjectivesComplete && !progress.Failed && !progress.IsCompleted("start"), "A mission starts incomplete and alive");
        Check(progress.PrerequisitesMet(null) && progress.PrerequisitesMet(Array.Empty<string>()) && !progress.PrerequisitesMet(new[] { "unknown" }),
            "Empty prerequisites are satisfied while unknown facts remain locked");
        progress.Tick(0, world);
        Check(!progress.IsCompleted("protect"), "Protect cannot finish before the mission's non-protect work");
        world.Interacted.Add("switch"); world.ClearedGroups.Add("guards"); progress.Tick(0, world);
        Check(progress.IsCompleted("start") && progress.IsCompleted("clear") && !progress.IsCompleted("reach"),
            "Interaction and dependent clear can resolve in the same tick without inventing an arrival");
        world.PlayerRegions.Add("exit"); world.Routes.Add("vip"); progress.Tick(0, world);
        Check(progress.IsCompleted("reach") && !progress.IsCompleted("escort"), "Freeing an escort is insufficient until that actor reaches its authored region");
        world.ActorRegions.Add("vip/exit"); progress.Tick(1, world);
        Check(progress.IsCompleted("escort") && progress.Capture().clocks[0].elapsed == 0, "A newly unlocked survive objective cannot consume time spent completing its escort prerequisite");
        world.PlayerRegions.Add("hold"); progress.Tick(.75f, world); progress.Tick(0, world);
        Check(progress.Capture().clocks[0].elapsed == .75f && !progress.AllObjectivesComplete, "A paused zero-delta tick adds no survival time");
        world.PlayerRegions.Remove("hold"); progress.Tick(10, world);
        Check(progress.Capture().clocks[0].elapsed == .75f, "Survival time pauses outside its region and does not reset prior progress");
        world.PlayerRegions.Add("hold"); progress.Tick(1.25f, world);
        Check(progress.AllObjectivesComplete && progress.IsCompleted("protect") && progress.ActiveText == "", "Protection completes only after the last non-protect objective");
        world.Living.Remove("vip"); progress.Tick(0, world);
        Check(progress.Failed && !progress.AllObjectivesComplete && progress.FailureReason.Contains("Свидетель"),
            "A protected actor can still fail the mission while the player is travelling to the final exit");
        var failed = progress.Capture(); var restored = new CampaignProgress(MainLevel());
        Check(restored.Restore(failed) && restored.Failed && restored.FailureReason == progress.FailureReason, "A failed checkpoint preserves its failure and reason");

        var locked = new CampaignProgress(Level(Objective("start", "interact", "switch"), Objective("protect", "protect", "vip", null, 0, "start")));
        locked.Tick(0, new World());
        Check(locked.Failed && !locked.IsCompleted("start"), "Protection death is checked even when its prerequisite has never unlocked");
        var escort = new CampaignProgress(Level(Objective("escort", "escort", "vip", "exit"))); var escortWorld = new World();
        escortWorld.Routes.Add("vip"); escortWorld.ActorRegions.Add("vip/exit"); escort.Tick(0, escortWorld);
        Check(!escort.AllObjectivesComplete, "A dead escort cannot satisfy a route/region objective");
        escortWorld.Living.Add("vip"); escort.Tick(0, escortWorld);
        Check(escort.AllObjectivesComplete, "A living escort satisfies both real route and arrival facts");
        var reach = new CampaignProgress(Level(Objective("reach", "reach", "exit"))); var atExit = new World(); atExit.PlayerRegions.Add("exit"); reach.Tick(0, atExit);
        Check(reach.AllObjectivesComplete, "Reach accepts the documented target-as-region form");

        var reverse = new CampaignProgress(Level(Objective("last", "clear", "guards", null, 0, "first"), Objective("first", "interact", "switch")));
        reverse.Tick(0, ReadyWorld()); Check(reverse.AllObjectivesComplete, "Prerequisite resolution is independent of objective array order");
        var definition = Level(Objective("first", "interact", "switch"), Objective("last", "clear", "guards", null, 0, "first"));
        var copied = new CampaignProgress(definition); definition.objectives[1].requires[0] = "corrupted"; copied.Tick(0, ReadyWorld());
        Check(copied.AllObjectivesComplete, "Mutable authoring DTOs cannot alter validated prerequisite arrays after construction");
    }

    private static void TimerCases()
    {
        var sequence = new CampaignProgress(Level(Objective("a", "survive", null, null, 1), Objective("b", "survive", null, null, 1, "a")));
        sequence.Tick(1, new World());
        Check(sequence.IsCompleted("a") && !sequence.IsCompleted("b") && sequence.Capture().clocks[1].elapsed == 0,
            "A chain of survival timers cannot double-spend the same tick delta");
        sequence.Tick(1, new World()); Check(sequence.AllObjectivesComplete, "A dependent survival timer receives its own later second");
        var parallel = new CampaignProgress(Level(Objective("a", "survive", null, null, 1), Objective("b", "survive", null, null, 1)));
        parallel.Tick(1, new World()); Check(parallel.AllObjectivesComplete, "Independent simultaneous survival objectives advance together");
        var bounded = new CampaignProgress(Level(Objective("a", "survive", null, null, 2))); bounded.Tick(float.MaxValue, new World());
        Check(bounded.AllObjectivesComplete && bounded.Capture().clocks[0].elapsed == 2, "Large finite delta clamps a clock to its authored amount without overflow");
        foreach (var delta in new[] { -.1f, float.NaN, float.PositiveInfinity })
        {
            var before = State(bounded); var rejected = false;
            try { bounded.Tick(delta, new World()); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && State(bounded) == before, "Invalid tick delta is rejected without mutating progress");
        }
    }

    private static void RestoreCases()
    {
        var original = new CampaignProgress(MainLevel()); var world = ReadyWorld(); original.Tick(0, world); world.PlayerRegions.Add("hold"); original.Tick(.75f, world);
        var checkpoint = original.Capture(); var state = State(original); var destination = new CampaignProgress(MainLevel());
        Check(destination.Restore(Copy(checkpoint)) && State(destination) == state, "A JSON field round-trip restores partial clocks and completed prerequisites");
        var detached = original.Capture(); detached.completed[0] = "tampered"; detached.clocks[0].elapsed = 1.99f;
        Check(State(original) == state, "Capture returns detached arrays and clock objects");

        var bad = Copy(checkpoint); bad.level = "other"; Reject(destination, bad, "Wrong mission checkpoint");
        bad = Copy(checkpoint); bad.schema = 2; Reject(destination, bad, "Unknown checkpoint schema");
        bad = Copy(checkpoint); bad.completed = new[] { "unknown" }; Reject(destination, bad, "Unknown objective ID");
        bad = Copy(checkpoint); bad.completed = new[] { "start", "start" }; Reject(destination, bad, "Duplicate completed objective ID");
        bad = Copy(checkpoint); bad.completed = new[] { "clear" }; Reject(destination, bad, "Completed objective missing its prerequisite");
        bad = Copy(checkpoint); bad.completed = new[] { "protect" }; Reject(destination, bad, "Protection completed before non-protect work");
        bad = Copy(checkpoint); bad.completed = new[] { "start", "clear", "reach", "escort", "hold" }; Reject(destination, bad, "Completed survival objective with an unearned timer");
        bad = Copy(checkpoint); bad.clocks = Array.Empty<CampaignObjectiveClock>(); Reject(destination, bad, "Missing survival clock");
        bad = Copy(checkpoint); bad.clocks = new[] { bad.clocks[0], bad.clocks[0] }; Reject(destination, bad, "Duplicate survival clock");
        bad = Copy(checkpoint); bad.clocks[0].id = "unknown"; Reject(destination, bad, "Unknown clock ID");
        bad = Copy(checkpoint); bad.clocks[0].id = "start"; Reject(destination, bad, "A non-survival objective cannot acquire a clock");
        foreach (var value in new[] { float.NaN, float.PositiveInfinity, -.1f, 2.1f })
        {
            bad = Copy(checkpoint); bad.clocks[0].elapsed = value; Reject(destination, bad, "Invalid survival clock value");
        }
        bad = Copy(checkpoint); bad.completed = Array.Empty<string>(); Reject(destination, bad, "Elapsed time before its prerequisites");
        bad = Copy(checkpoint); bad.failed = true; bad.failureReason = ""; Reject(destination, bad, "Failed checkpoint without a reason");
        bad = Copy(checkpoint); bad.failureReason = "contradiction"; Reject(destination, bad, "Successful checkpoint with a failure reason");
        Reject(destination, null, "Null checkpoint");
        Check(destination.Restore(Copy(checkpoint)) && State(destination) == state, "Rejected candidates leave a later legitimate restore usable");
    }

    private static void Reject(CampaignProgress destination, CampaignProgressSnapshot snapshot, string message)
    {
        var before = State(destination);
        Check(!destination.Restore(snapshot) && State(destination) == before, message + " rejected atomically");
    }

    private static void Invalid(CampaignLevel level, string message)
    {
        var rejected = false;
        try { new CampaignProgress(level); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }

    private static void ValidationCases()
    {
        Invalid(Level(Objective("same", "reach", "exit"), Objective("same", "reach", "exit")), "Duplicate objective IDs rejected");
        Invalid(Level(Objective("a", "reach", "exit", null, 0, "missing")), "Unknown prerequisite rejected");
        Invalid(Level(Objective("a", "reach", "exit", null, 0, "b"), Objective("b", "reach", "exit", null, 0, "a")), "Cyclic prerequisites rejected");
        Invalid(Level(Objective("a", "interact", "missing")), "Unknown interaction target rejected");
        Invalid(Level(Objective("a", "reach", "missing")), "Unknown reach region rejected");
        Invalid(Level(Objective("a", "escort", "missing")), "Unknown escort actor rejected");
        Invalid(Level(Objective("a", "clear", "missing")), "Unknown enemy group rejected instead of being treated as clear");
        Invalid(Level(Objective("a", "invented")), "Unknown objective type rejected");
        Invalid(Level(Objective("a", "reach", "exit", "hold")), "Conflicting reach target/region rejected");
        Invalid(Level(Objective("a", "protect", "vip"), Objective("b", "reach", "exit", null, 0, "a")), "Non-protect requiring protect is rejected as a semantic deadlock");
        foreach (var amount in new[] { 0, -.1f, float.NaN, float.PositiveInfinity })
            Invalid(Level(Objective("a", "survive", null, null, amount)), "Survival requires positive finite seconds");
        var duplicates = Level(Objective("a", "reach", "exit")); duplicates.actors = new[] { new CampaignActor { id = "vip" }, new CampaignActor { id = "vip" } };
        Invalid(duplicates, "Duplicate actor IDs rejected");
        var interaction = Level(Objective("a", "interact", "switch")); interaction.interactions[0].requires = new[] { "unknown" };
        Invalid(interaction, "Interaction prerequisites must reference existing objectives");
        interaction = Level(Objective("a", "interact", "switch")); interaction.interactions[0].requires = new[] { "a" };
        Invalid(interaction, "An interaction locked by its own interact objective is a dependency cycle");
        interaction = Level(Objective("a", "interact", "switch"), Objective("p", "protect", "vip")); interaction.interactions[0].requires = new[] { "p" };
        Invalid(interaction, "An interaction cannot hide a non-protect/protect dependency deadlock");
        Invalid(Level(Objective("", "reach", "exit")), "Empty objective IDs rejected");
    }
}
