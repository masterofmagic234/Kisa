using System;
using System.Collections.Generic;
using System.Text.Json;
using Arkon.Core;
using WhiteGuard.Gameplay;

internal static class CampaignCheckpointCases
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    public static void Run(CampaignData campaign, Action<bool, string> check)
    {
        foreach (var level in campaign.levels)
        {
            var snapshot = InitialCheckpoint(level);
            check(CampaignCheckpointValidation.Validate(level, snapshot, out _), level.id + ": complete initial world candidate validates before native restore");
            var copied = Clone(snapshot);
            check(CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": checkpoint primitive DTO survives a real JSON round-trip");
            if (copied.actors.Length > 0)
            {
                copied.actors[copied.actors.Length - 1].x = float.NaN;
                check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": late actor NaN is rejected before world mutation");
                check(CampaignCheckpointValidation.Validate(level, snapshot, out _), level.id + ": rejecting a damaged candidate leaves the valid source snapshot unchanged");
                copied = Clone(snapshot); copied.actors[0].id = "unknown_actor";
                check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": unknown actor cannot silently enter a saved map");
            }
            copied = Clone(snapshot); copied.inventory.infiniteAmmo = true;
            check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": finite campaign rejects an infinite-ammunition snapshot");
            copied = Clone(snapshot); copied.playerX = level.width + 100;
            check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": saved player cannot spawn outside the map");
            copied = Clone(snapshot); copied.playerHealth.maximum = 999;
            check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": checkpoint cannot replace configured player health");
            copied = Clone(snapshot); copied.inventory.reserves[0].count = -1;
            check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": negative finite reserve is rejected atomically");
            copied = Clone(snapshot); copied.inventory.cooldown = float.PositiveInfinity;
            check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": infinite weapon cooldown is rejected");
            if (snapshot.activePickups.Length > 0)
            {
                copied = Clone(snapshot); copied.consumedPickups = new[] { copied.activePickups[0].id };
                check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": consumed pickup cannot also remain active");
            }
            var required = Array.Find(snapshot.actors, a => Array.Find(level.actors, d => d.id == a.id).required);
            if (required != null)
            {
                copied = Clone(snapshot); var dead = Array.Find(copied.actors, a => a.id == required.id);
                dead.health = 0; dead.healthState.current = 0; dead.healthState.dead = true;
                check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": dead required actor is not a resumable checkpoint");
            }
            var hostage = Array.Find(level.actors, a => a.role == "hostage");
            if (hostage != null)
            {
                copied = Clone(snapshot); var carried = Array.Find(copied.actors, a => a.id == hostage.id);
                carried.freed = false; carried.carried = true;
                check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": carried-but-unfreed hostage cannot soft-lock the escort");
            }
            var nonHostage = Array.Find(level.actors, a => a.role != "hostage");
            if (nonHostage != null)
            {
                copied = Clone(snapshot); Array.Find(copied.actors, a => a.id == nonHostage.id).freed = false;
                check(!CampaignCheckpointValidation.Validate(level, copied, out _), level.id + ": impossible unfreed ally or passenger is rejected");
            }
        }
        var save = new CampaignSave
        {
            campaign = campaign.id, level = campaign.levels[0].id,
            checkpoint = InitialCheckpoint(campaign.levels[0]), facts = Array.Empty<string>(), completedLevels = Array.Empty<string>()
        };
        check(CampaignCheckpointValidation.Validate(campaign, save, out _), "Whole campaign candidate validates before live progress is replaced");
        var invalid = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(save, Json), Json);
        invalid.completedLevels = new[] { "unknown_chapter" };
        check(!CampaignCheckpointValidation.Validate(campaign, invalid, out _), "Unknown completed chapter cannot overwrite the live run");
        invalid = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(save, Json), Json);
        invalid.facts = new[] { "unearned_unknown_fact" };
        check(!CampaignCheckpointValidation.Validate(campaign, invalid, out _), "Unknown story fact is rejected before live facts are cleared");
        invalid = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(save, Json), Json);
        invalid.totalSeconds = float.NaN;
        check(!CampaignCheckpointValidation.Validate(campaign, invalid, out _), "NaN global elapsed time cannot enter the campaign");
        invalid = JsonSerializer.Deserialize<CampaignSave>(JsonSerializer.Serialize(save, Json), Json);
        invalid.level = ""; invalid.checkpoint = null;
        check(!CampaignCheckpointValidation.Validate(campaign, invalid, out _), "Empty current level is not a completed campaign without all chapters");
    }

    private static CampaignCheckpoint InitialCheckpoint(CampaignLevel level)
    {
        var result = new CampaignCheckpoint
        {
            level = level.id, progress = new CampaignProgress(level).Capture(), playerX = level.spawn.x, playerY = level.spawn.y,
            health = 100, playerHealth = new HealthModel().CaptureState(), inventory = new WeaponModel(false).CaptureState(), bottle = true
        };
        var actors = new List<CampaignActorState>();
        foreach (var data in level.actors)
        {
            var maximum = data.health > 0 ? data.health : data.role == "vehicle" ? 240 : 100;
            var state = new HealthModel(maximum, data.role == "vehicle" ? .08f : .24f).CaptureState();
            actors.Add(new CampaignActorState { id = data.id, x = data.x, y = data.y, health = state.current, healthState = state, freed = data.role != "hostage" });
        }
        result.actors = actors.ToArray();
        var enemies = new List<CampaignEnemyState>();
        foreach (var data in level.enemies) enemies.Add(new CampaignEnemyState { id = data.id, x = data.x, y = data.y });
        result.enemies = enemies.ToArray();
        var doors = new List<CampaignDoorState>();
        foreach (var data in level.doors) doors.Add(new CampaignDoorState { id = data.id, locked = data.locked });
        result.doors = doors.ToArray(); result.activePickups = level.pickups;
        return result;
    }
    private static CampaignCheckpoint Clone(CampaignCheckpoint value) => JsonSerializer.Deserialize<CampaignCheckpoint>(JsonSerializer.Serialize(value, Json), Json);
}
