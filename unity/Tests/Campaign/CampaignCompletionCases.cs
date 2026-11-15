using System;
using System.Collections.Generic;
using Arkon.Core;
using WhiteGuard.Gameplay;

/// <summary>
/// Authored objective-state witness, not a native playthrough. It uses production
/// CampaignProgress and checkpoint validation. World facts assume successful
/// movement/combat actions; geometry has a separate static witness, and neither
/// witness certifies live AI, input, PhysX, combat balance, presentation or saves
/// through Unity JsonUtility.
/// </summary>
internal static class CampaignCompletionCases
{
    public static int CompletedLevels { get; private set; }

    public static void Run(CampaignData campaign, Action<bool, string> check)
    {
        CompletedLevels = 0;
        var completed = new List<string>(); var facts = new HashSet<string>(StringComparer.Ordinal);
        var first = Array.FindIndex(campaign.levels, level => level.id == campaign.firstLevel);
        check(first >= 0, "Completion witness starts at the authored firstLevel");
        if (first < 0) return;
        for (var index = first; index < campaign.levels.Length; index++)
        {
            var level = campaign.levels[index]; var progress = new CampaignProgress(level); var world = new Facts(level);
            progress.Tick(0, world);
            world.MovePlayer(Center(level.exit)); progress.Tick(0, world);
            check(!progress.AllObjectivesComplete, level.id + ": merely standing in the final exit does not bypass the objectives");
            var saved = Drive(level, ref progress, ref world, check, true);
            check(progress.AllObjectivesComplete && !progress.Failed, level.id + ": all authored objectives can resolve through their own prerequisite contracts");
            if (!progress.AllObjectivesComplete) continue;
            world.MovePlayer(Center(level.exit));
            check(level.exit.Contains(world.Player.X, world.Player.Y), level.id + ": completion witness also reaches the separate final exit predicate");
            var finished = new CampaignProgress(level);
            check(finished.Restore(progress.Capture()) && finished.AllObjectivesComplete, level.id + ": completed objective checkpoint preserves the terminal state");
            completed.Add(level.id); facts.Add("complete:" + level.id);
            foreach (var fact in world.StoryFacts) facts.Add(fact);
            CompletedLevels++;

            // Death remains fatal even after objectives finish and before the
            // level owner commits extraction. Retry uses the last healthy world.
            var protectedObjective = Array.Find(level.objectives, objective => objective.type == "protect");
            if (protectedObjective != null)
            {
                var doomed = world.Clone(); doomed.KillProtected(protectedObjective.target);
                finished.Tick(0, doomed);
                check(finished.Failed && !finished.AllObjectivesComplete, level.id + ": extraction cannot ignore a protected actor's death");
                var early = new CampaignProgress(level); var earlyWorld = new Facts(level); earlyWorld.KillProtected(protectedObjective.target); early.Tick(0, earlyWorld);
                check(early.Failed, level.id + ": protection also fails before its mission work has unlocked");
            }
            if (saved != null)
            {
                var retried = new CampaignProgress(level); var retryWorld = saved.World.Clone();
                check(retried.Restore(saved.Progress), level.id + ": retry starts from a healthy detached midpoint objective snapshot");
                Drive(level, ref retried, ref retryWorld, check, false);
                check(retried.AllObjectivesComplete && !retried.Failed, level.id + ": retry can finish the remaining mission without repeating already-consumed interactions");
            }
            if (level.id == "m04")
                check(world.Dead.Count == 0 && world.Neutralized.Count > 0,
                    "m04: the stand-down branch completes without converting surrender into a kill-all requirement");
        }
        check(CompletedLevels == campaign.levels.Length, "Objective witness can reach every campaign level from firstLevel in runtime array order");
        var final = new CampaignSave
        {
            campaign = campaign.id, level = "", checkpoint = null,
            completedLevels = completed.ToArray(), facts = new List<string>(facts).ToArray()
        };
        check(CampaignCheckpointValidation.Validate(campaign, final, out _),
            "Completing every authored level produces a finished-campaign save accepted by the production validator");
    }

    private sealed class Midpoint
    {
        public CampaignProgressSnapshot Progress;
        public Facts World;
    }

    private static Midpoint Drive(CampaignLevel level, ref CampaignProgress progress, ref Facts world, Action<bool, string> check, bool captureMidpoint)
    {
        Midpoint midpoint = null;
        for (var iteration = 0; iteration < level.objectives.Length + level.interactions.Length + 8 && !progress.AllObjectivesComplete && !progress.Failed; iteration++)
        {
            var changed = false;
            // Interaction locks belong to the source interaction, including
            // optional observations that may be available before an objective.
            foreach (var interaction in level.interactions)
            {
                if (world.HasInteracted(interaction.id) || !progress.PrerequisitesMet(interaction.requires)) continue;
                world.MovePlayer(new Vec2(interaction.x, interaction.y));
                world.Interact(interaction); changed = true; progress.Tick(0, world);
                CaptureMidpoint(level, ref progress, ref world, check, captureMidpoint, ref midpoint);
            }
            foreach (var objective in level.objectives)
            {
                if (progress.IsCompleted(objective.id) || !progress.PrerequisitesMet(objective.requires)) continue;
                var acted = false;
                switch (objective.type)
                {
                    case "reach": world.MovePlayer(world.RegionCenter(string.IsNullOrEmpty(objective.region) ? objective.target : objective.region)); acted = true; break;
                    case "clear": acted = world.DefeatActiveGroup(objective.target, progress); break;
                    case "escort": acted = world.Arrive(objective.target, objective.region, progress); break;
                    case "survive":
                        if (!string.IsNullOrEmpty(objective.region)) world.MovePlayer(world.RegionCenter(objective.region));
                        progress.Tick(objective.amount, world); acted = true; break;
                }
                if (acted)
                {
                    changed = true; progress.Tick(0, world);
                    CaptureMidpoint(level, ref progress, ref world, check, captureMidpoint, ref midpoint);
                }
            }
            if (!changed && !progress.AllObjectivesComplete)
            {
                var pending = new List<string>(); foreach (var objective in level.objectives) if (!progress.IsCompleted(objective.id)) pending.Add(objective.id);
                check(false, level.id + ": objective contract deadlock: " + string.Join(", ", pending)); break;
            }
        }
        return midpoint;
    }

    private static void CaptureMidpoint(CampaignLevel level, ref CampaignProgress progress, ref Facts world,
        Action<bool, string> check, bool requested, ref Midpoint midpoint)
    {
        if (!requested || midpoint != null) return;
        var snapshot = progress.Capture();
        if (snapshot.completed.Length == 0 || snapshot.completed.Length >= level.objectives.Length) return;
        midpoint = new Midpoint { Progress = snapshot, World = world.Clone() };
        var checkpoint = world.Checkpoint(progress);
        check(CampaignCheckpointValidation.Validate(level, checkpoint, out var error), level.id + ": partial world and objective checkpoint agree: " + error);
        var resumed = new CampaignProgress(level);
        check(resumed.Restore(midpoint.Progress) && !resumed.AllObjectivesComplete, level.id + ": checkpoint resumes before the remaining mission work");
        progress = resumed; world = midpoint.World.Clone();
    }

    private sealed class Actor
    {
        public CampaignActor Data;
        public Vec2 Position;
        public bool Alive = true, Freed, Carried;
        public int RouteIndex;
    }

    private sealed class Facts : IObjectiveWorld
    {
        private readonly CampaignLevel level;
        private readonly Dictionary<string, CampaignRect> regions = new Dictionary<string, CampaignRect>(StringComparer.Ordinal);
        private readonly Dictionary<string, Actor> actors = new Dictionary<string, Actor>(StringComparer.Ordinal);
        private readonly HashSet<string> interacted = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Dead = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> Neutralized = new HashSet<string>(StringComparer.Ordinal);
        public readonly HashSet<string> StoryFacts = new HashSet<string>(StringComparer.Ordinal);
        public Vec2 Player;

        public Facts(CampaignLevel source)
        {
            level = source; Player = new Vec2(source.spawn.x, source.spawn.y);
            foreach (var region in source.regions) regions.Add(region.id, region);
            foreach (var actor in source.actors) actors.Add(actor.id, new Actor { Data = actor, Position = new Vec2(actor.x, actor.y), Freed = actor.role != "hostage" });
        }
        public Facts Clone()
        {
            var clone = new Facts(level) { Player = Player };
            clone.interacted.UnionWith(interacted); clone.Dead.UnionWith(Dead); clone.Neutralized.UnionWith(Neutralized); clone.StoryFacts.UnionWith(StoryFacts);
            foreach (var item in actors)
            {
                var actor = item.Value; clone.actors[item.Key] = new Actor { Data = actor.Data, Position = actor.Position,
                    Alive = actor.Alive, Freed = actor.Freed, Carried = actor.Carried, RouteIndex = actor.RouteIndex };
            }
            return clone;
        }
        public Vec2 RegionCenter(string id) => Center(regions[id]);
        public void MovePlayer(Vec2 point)
        {
            Player = point;
            foreach (var actor in actors.Values) if (actor.Carried) actor.Position = point - Vec2.Right * CampaignActorDimensions.CarriedOffset;
        }
        public void Interact(CampaignInteraction interaction)
        {
            interacted.Add(interaction.id); foreach (var fact in interaction.grants) StoryFacts.Add(fact);
            if (interaction.action == "rescue" && actors.TryGetValue(interaction.target, out var rescued) && rescued.Alive) rescued.Freed = true;
            if (interaction.action == "carry" && actors.TryGetValue(interaction.target, out var carried) && carried.Alive && carried.Data.role != "vehicle" && carried.Data.role != "passenger")
            { carried.Freed = carried.Carried = true; MovePlayer(Player); }
            if (interaction.action == "stand_down") foreach (var enemy in level.enemies) if (enemy.group == interaction.target && !Dead.Contains(enemy.id)) Neutralized.Add(enemy.id);
        }
        public bool DefeatActiveGroup(string group, CampaignProgress progress)
        {
            var changed = false;
            foreach (var enemy in level.enemies)
                if (enemy.group == group && Active(enemy, progress) && !Dead.Contains(enemy.id) && !Neutralized.Contains(enemy.id)) { Dead.Add(enemy.id); changed = true; }
            return changed;
        }
        private static bool Active(CampaignEnemy enemy, CampaignProgress progress)
            => string.IsNullOrEmpty(enemy.activeAfterObjective) || progress.IsCompleted(enemy.activeAfterObjective);
        public bool Arrive(string id, string region, CampaignProgress progress)
        {
            if (!actors.TryGetValue(id, out var actor) || !IsActorAlive(id) || !actor.Freed) return false;
            if (actor.Data.role == "vehicle")
            {
                var route = actor.Data.route;
                if (route.Length == 0) return false;
                foreach (var point in route)
                {
                    // This is an assumed successful combat action, not proof
                    // that the encounter can be beaten with native input/AI.
                    foreach (var enemy in level.enemies)
                        if (Active(enemy, progress) && Vec2.Distance(new Vec2(enemy.x, enemy.y), new Vec2(point.x, point.y)) < 160 && !Neutralized.Contains(enemy.id)) Dead.Add(enemy.id);
                    MovePlayer(new Vec2(point.x + 40, point.y)); actor.Position = new Vec2(point.x, point.y);
                }
                actor.RouteIndex = route.Length - 1;
            }
            else if (actor.Carried) MovePlayer(RegionCenter(region) + Vec2.Right * CampaignActorDimensions.CarriedOffset);
            else { MovePlayer(RegionCenter(region)); actor.Position = RegionCenter(region); }
            return true;
        }
        public void KillProtected(string id)
        {
            var actor = actors[id];
            if (actor.Data.role == "passenger" && actors.TryGetValue(actor.Data.transport, out var transport)) transport.Alive = false;
            else actor.Alive = false;
        }
        public bool HasInteracted(string id) => interacted.Contains(id);
        public bool PlayerInRegion(string id) => regions.TryGetValue(id, out var region) && region.Contains(Player.X, Player.Y);
        public bool IsGroupClear(string id)
        {
            var known = false;
            foreach (var enemy in level.enemies) if (enemy.group == id) { known = true; if (!Dead.Contains(enemy.id) && !Neutralized.Contains(enemy.id)) return false; }
            return known;
        }
        public bool IsActorAlive(string id)
        {
            if (!actors.TryGetValue(id, out var actor) || !actor.Alive) return false;
            return actor.Data.role != "passenger" || actors.TryGetValue(actor.Data.transport, out var transport) && transport.Alive;
        }
        public bool IsActorInRegion(string actor, string region) => IsActorAlive(actor) && regions.TryGetValue(region, out var area) && area.Contains(actors[actor].Position.X, actors[actor].Position.Y);
        public bool IsActorRouteComplete(string id)
        {
            if (!IsActorAlive(id) || !actors[id].Freed) return false;
            var actor = actors[id]; var route = actor.Data.route;
            return actor.Data.role != "vehicle" || route.Length == 0 || actor.RouteIndex == route.Length - 1 && Vec2.Distance(actor.Position, new Vec2(route[route.Length - 1].x, route[route.Length - 1].y)) < 16;
        }
        public CampaignCheckpoint Checkpoint(CampaignProgress progress)
        {
            var health = new HealthModel(); var inventory = new WeaponModel(false);
            if (level.startWeapon == "shotgun") inventory.Equip("shotgun", 6);
            else if (level.startWeapon == "bat") inventory.Equip("bat");
            var result = new CampaignCheckpoint { level = level.id, progress = progress.Capture(), playerX = Player.X, playerY = Player.Y,
                health = health.Current, playerHealth = health.CaptureState(), inventory = inventory.CaptureState(), bottle = true,
                interacted = new List<string>(interacted).ToArray(), neutralized = new List<string>(Neutralized).ToArray() };
            var actorStates = new List<CampaignActorState>();
            foreach (var actor in actors.Values)
            {
                var maximum = actor.Data.health > 0 ? actor.Data.health : actor.Data.role == "vehicle" ? 240 : 100;
                var model = new HealthModel(maximum, actor.Data.role == "vehicle" ? .08f : .24f); if (!actor.Alive) model.ForceKill();
                actorStates.Add(new CampaignActorState { id = actor.Data.id, x = actor.Position.X, y = actor.Position.Y, health = model.Current,
                    healthState = model.CaptureState(), freed = actor.Freed, carried = actor.Carried, routeIndex = actor.RouteIndex });
            }
            result.actors = actorStates.ToArray();
            var enemyStates = new List<CampaignEnemyState>(); var pickups = new List<CampaignPickup>(level.pickups);
            foreach (var enemy in level.enemies)
            {
                var dead = Dead.Contains(enemy.id); enemyStates.Add(new CampaignEnemyState { id = enemy.id, x = enemy.x, y = enemy.y, dead = dead });
                if (dead) pickups.Add(new CampaignPickup { id = enemy.id + "_weapon", kind = enemy.kind == "melee" || enemy.kind == "butcher" ? "bat" : "pistol", x = enemy.x, y = enemy.y });
            }
            result.enemies = enemyStates.ToArray(); result.activePickups = pickups.ToArray();
            var doors = new List<CampaignDoorState>(); foreach (var door in level.doors) doors.Add(new CampaignDoorState { id = door.id, locked = door.locked });
            result.doors = doors.ToArray(); return result;
        }
    }
    private static Vec2 Center(CampaignRect region) => new Vec2(region.x + region.w * .5f, region.y + region.h * .5f);
}
