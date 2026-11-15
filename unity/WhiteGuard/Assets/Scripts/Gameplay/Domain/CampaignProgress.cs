using System;
using System.Collections.Generic;

namespace WhiteGuard.Gameplay
{
    /// <summary>Read-only encounter facts. Unknown actors, regions and groups must return false.</summary>
    public interface IObjectiveWorld
    {
        bool HasInteracted(string id);
        bool PlayerInRegion(string id);
        bool IsGroupClear(string id);
        bool IsActorAlive(string id);
        bool IsActorInRegion(string actor, string region);
        bool IsActorRouteComplete(string id);
    }

    [Serializable]
    public sealed class CampaignObjectiveClock
    {
        public string id;
        public float elapsed;
    }

    [Serializable]
    public sealed class CampaignProgressSnapshot
    {
        public int schema = 1;
        public string level;
        public string[] completed = Array.Empty<string>();
        public CampaignObjectiveClock[] clocks = Array.Empty<CampaignObjectiveClock>();
        public bool failed;
        public string failureReason;
    }

    /// <summary>
    /// Encounter objective state, independent of Unity and physics. A level owner
    /// supplies real world facts and separately checks the authored exit region.
    /// Checkpoints contain objective state only, not a fabricated world restore.
    /// </summary>
    public sealed class CampaignProgress
    {
        private sealed class Objective
        {
            public string Id, Text, Type, Target, Region;
            public string[] Requires;
            public float Amount;
        }

        private readonly string levelId;
        private readonly Objective[] objectives;
        private readonly Dictionary<string, Objective> byId = new Dictionary<string, Objective>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> actorNames = new Dictionary<string, string>(StringComparer.Ordinal);
        private HashSet<string> completed = new HashSet<string>(StringComparer.Ordinal);
        private Dictionary<string, float> clocks = new Dictionary<string, float>(StringComparer.Ordinal);

        public bool Failed { get; private set; }
        public string FailureReason { get; private set; } = "";
        public bool AllObjectivesComplete => !Failed && completed.Count == objectives.Length;
        public string ActiveText
        {
            get
            {
                if (Failed) return FailureReason;
                foreach (var objective in objectives)
                    if (!completed.Contains(objective.Id) && PrerequisitesMet(objective.Requires)) return objective.Text;
                return "";
            }
        }

        public CampaignProgress(CampaignLevel level)
        {
            if (level == null) throw new ArgumentNullException(nameof(level));
            RequireId(level.id, "level"); levelId = level.id;
            var actors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var actor in level.actors ?? Array.Empty<CampaignActor>())
            {
                if (actor == null) throw Invalid("Null actor.");
                AddId(actors, actor.id, "actor"); actorNames.Add(actor.id, string.IsNullOrWhiteSpace(actor.name) ? actor.id : actor.name);
            }
            var regions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var region in level.regions ?? Array.Empty<CampaignRect>())
            {
                if (region == null) throw Invalid("Null region.");
                AddId(regions, region.id, "region");
            }
            var interactions = new HashSet<string>(StringComparer.Ordinal);
            var interactionRequirements = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var interaction in level.interactions ?? Array.Empty<CampaignInteraction>())
            {
                if (interaction == null) throw Invalid("Null interaction.");
                AddId(interactions, interaction.id, "interaction");
                interactionRequirements.Add(interaction.id, (string[])(interaction.requires ?? Array.Empty<string>()).Clone());
            }
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var enemies = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in level.enemies ?? Array.Empty<CampaignEnemy>())
            {
                if (enemy == null) throw Invalid("Null enemy.");
                AddId(enemies, enemy.id, "enemy");
                if (!string.IsNullOrWhiteSpace(enemy.group)) groups.Add(enemy.group);
            }

            var source = level.objectives ?? Array.Empty<CampaignObjective>();
            objectives = new Objective[source.Length];
            for (var i = 0; i < source.Length; i++)
            {
                var item = source[i];
                if (item == null) throw Invalid("Null objective.");
                RequireId(item.id, "objective");
                if (byId.ContainsKey(item.id)) throw Invalid("Duplicate objective: " + item.id);
                var objective = new Objective
                {
                    Id = item.id, Text = string.IsNullOrWhiteSpace(item.text) ? item.id : item.text,
                    Type = item.type, Target = item.target, Region = item.region,
                    Amount = item.amount, Requires = (string[])(item.requires ?? Array.Empty<string>()).Clone()
                };
                switch (objective.Type)
                {
                    case "interact": RequireReference(interactions, objective.Target, "interaction", objective.Id); break;
                    case "clear": RequireReference(groups, objective.Target, "enemy group", objective.Id); break;
                    case "escort":
                    case "protect": RequireReference(actors, objective.Target, "actor", objective.Id); break;
                    case "reach":
                        if (!string.IsNullOrEmpty(objective.Region) && !string.IsNullOrEmpty(objective.Target) && objective.Region != objective.Target)
                            throw Invalid("Reach objective has two different region references: " + objective.Id);
                        objective.Region = string.IsNullOrEmpty(objective.Region) ? objective.Target : objective.Region;
                        RequireReference(regions, objective.Region, "region", objective.Id); break;
                    case "survive":
                        if (!Finite(objective.Amount) || objective.Amount <= 0) throw Invalid("Survive amount must be finite positive seconds: " + objective.Id);
                        clocks.Add(objective.Id, 0); break;
                    default: throw Invalid("Unknown objective type '" + objective.Type + "': " + objective.Id);
                }
                if (!string.IsNullOrEmpty(objective.Region)) RequireReference(regions, objective.Region, "region", objective.Id);
                objectives[i] = objective; byId.Add(objective.Id, objective);
            }
            foreach (var interaction in interactionRequirements) ValidateRequirements(interaction.Value, interaction.Key);
            foreach (var objective in objectives)
            {
                ValidateRequirements(objective.Requires, objective.Id);
                // An interaction's lock is part of an interact objective's actual
                // dependency graph, not merely a UI hint. Include it in checkpoint
                // and cycle validation even when the author omitted a duplicate
                // objective.requires entry.
                if (objective.Type == "interact")
                {
                    var merged = new List<string>(objective.Requires);
                    foreach (var requirement in interactionRequirements[objective.Target])
                        if (!merged.Contains(requirement)) merged.Add(requirement);
                    objective.Requires = merged.ToArray();
                }
                if (objective.Type != "protect")
                    foreach (var requirement in objective.Requires)
                        if (byId[requirement].Type == "protect")
                            throw Invalid("A non-protect objective cannot require protect, which finishes after all non-protect objectives: " + objective.Id);
            }
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var objective in objectives) Visit(objective, states);
        }

        public bool IsCompleted(string id) => id != null && completed.Contains(id);

        public bool PrerequisitesMet(string[] requires)
        {
            if (requires == null) return true;
            foreach (var requirement in requires) if (requirement == null || !completed.Contains(requirement)) return false;
            return true;
        }

        public void Tick(float delta, IObjectiveWorld world)
        {
            if (!Finite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (Failed) return;
            // A locked or already-completed protect objective never grants immunity.
            // Continue checking while the owner waits for the player to reach its exit.
            foreach (var objective in objectives)
            {
                if (objective.Type != "protect" || world.IsActorAlive(objective.Target)) continue;
                Failed = true; FailureReason = "Погиб персонаж: " + actorNames[objective.Target]; return;
            }
            // Only clocks eligible at the START of this tick may spend its delta.
            // A chain of one-second timers cannot all finish in one one-second tick.
            foreach (var objective in objectives)
            {
                if (objective.Type != "survive" || completed.Contains(objective.Id) || !PrerequisitesMet(objective.Requires)) continue;
                if (!string.IsNullOrEmpty(objective.Region) && !world.PlayerInRegion(objective.Region)) continue;
                clocks[objective.Id] = (float)Math.Min(objective.Amount, (double)clocks[objective.Id] + delta);
            }
            // Non-temporal facts can unlock one another immediately, regardless of
            // their authored array order. Validation guarantees an acyclic graph.
            bool changed;
            do
            {
                changed = false;
                foreach (var objective in objectives)
                {
                    if (completed.Contains(objective.Id) || !PrerequisitesMet(objective.Requires) || !Satisfied(objective, world)) continue;
                    completed.Add(objective.Id); changed = true;
                }
            } while (changed);
        }

        private bool Satisfied(Objective objective, IObjectiveWorld world)
        {
            switch (objective.Type)
            {
                case "interact": return world.HasInteracted(objective.Target);
                case "reach": return world.PlayerInRegion(objective.Region);
                case "clear": return world.IsGroupClear(objective.Target);
                case "escort": return world.IsActorAlive(objective.Target) && world.IsActorRouteComplete(objective.Target) &&
                    (string.IsNullOrEmpty(objective.Region) || world.IsActorInRegion(objective.Target, objective.Region));
                case "protect": return NonProtectComplete();
                case "survive": return clocks[objective.Id] >= objective.Amount;
                default: return false;
            }
        }

        private bool NonProtectComplete()
        {
            foreach (var objective in objectives) if (objective.Type != "protect" && !completed.Contains(objective.Id)) return false;
            return true;
        }

        public CampaignProgressSnapshot Capture()
        {
            var done = new List<string>(); var time = new List<CampaignObjectiveClock>();
            foreach (var objective in objectives)
            {
                if (completed.Contains(objective.Id)) done.Add(objective.Id);
                if (objective.Type == "survive") time.Add(new CampaignObjectiveClock { id = objective.Id, elapsed = clocks[objective.Id] });
            }
            return new CampaignProgressSnapshot
            {
                schema = 1, level = levelId, completed = done.ToArray(), clocks = time.ToArray(),
                failed = Failed, failureReason = FailureReason
            };
        }

        /// <summary>Validate the entire candidate before mutating any live progress.</summary>
        public bool Restore(CampaignProgressSnapshot snapshot)
        {
            if (snapshot == null || snapshot.schema != 1 || snapshot.level != levelId || snapshot.completed == null || snapshot.clocks == null ||
                (snapshot.failed ? string.IsNullOrWhiteSpace(snapshot.failureReason) : !string.IsNullOrEmpty(snapshot.failureReason))) return false;
            var restored = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in snapshot.completed) if (id == null || !byId.ContainsKey(id) || !restored.Add(id)) return false;
            var times = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var clock in snapshot.clocks)
            {
                if (clock == null || clock.id == null || !byId.TryGetValue(clock.id, out var objective) || objective.Type != "survive" ||
                    !Finite(clock.elapsed) || clock.elapsed < 0 || clock.elapsed > objective.Amount || times.ContainsKey(clock.id)) return false;
                times.Add(clock.id, clock.elapsed);
            }
            if (times.Count != clocks.Count) return false;
            foreach (var objective in objectives)
            {
                if (restored.Contains(objective.Id))
                {
                    foreach (var requirement in objective.Requires) if (!restored.Contains(requirement)) return false;
                    if (objective.Type == "protect")
                        foreach (var other in objectives) if (other.Type != "protect" && !restored.Contains(other.Id)) return false;
                }
                if (objective.Type != "survive") continue;
                var elapsed = times[objective.Id];
                if (restored.Contains(objective.Id) != (elapsed >= objective.Amount)) return false;
                if (elapsed > 0) foreach (var requirement in objective.Requires) if (!restored.Contains(requirement)) return false;
            }
            completed = restored; clocks = times; Failed = snapshot.failed; FailureReason = snapshot.failureReason ?? "";
            return true;
        }

        private void ValidateRequirements(string[] requirements, string owner)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var requirement in requirements)
                if (string.IsNullOrWhiteSpace(requirement) || !byId.ContainsKey(requirement) || !seen.Add(requirement))
                    throw Invalid("Unknown or duplicate prerequisite '" + requirement + "' for " + owner);
        }

        private void Visit(Objective objective, Dictionary<string, int> states)
        {
            if (states.TryGetValue(objective.Id, out var state))
            {
                if (state == 1) throw Invalid("Cyclic objective prerequisites at " + objective.Id);
                return;
            }
            states[objective.Id] = 1;
            foreach (var requirement in objective.Requires) Visit(byId[requirement], states);
            states[objective.Id] = 2;
        }

        private static void RequireReference(HashSet<string> available, string id, string kind, string owner)
        {
            if (id == null || !available.Contains(id)) throw Invalid("Unknown " + kind + " '" + id + "' for " + owner);
        }
        private static void RequireId(string id, string kind)
        { if (string.IsNullOrWhiteSpace(id)) throw Invalid("Missing " + kind + " ID."); }
        private static void AddId(HashSet<string> ids, string id, string kind)
        { RequireId(id, kind); if (!ids.Add(id)) throw Invalid("Duplicate " + kind + " ID: " + id); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static ArgumentException Invalid(string message) => new ArgumentException(message, "level");
    }
}
