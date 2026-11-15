using System;
using System.Collections.Generic;
using Arkon.Core;

namespace WhiteGuard.Gameplay
{
    /// <summary>Validate the whole serialized candidate before replacing an active run.</summary>
    public static class CampaignCheckpointValidation
    {
        public static bool Validate(CampaignData campaign, CampaignSave save, out string error)
        {
            error = "";
            if (campaign == null || save == null || save.schema != 1 || save.campaign != campaign.id ||
                save.completedLevels == null || save.facts == null || save.deaths < 0 || save.kills < 0 || !Finite(save.totalSeconds) || save.totalSeconds < 0)
                return Reject(out error, "Неверные сведения о кампании.");
            var levels = new Dictionary<string, CampaignLevel>(StringComparer.Ordinal);
            var allowedFacts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var level in campaign.levels ?? Array.Empty<CampaignLevel>())
            {
                levels.Add(level.id, level); allowedFacts.Add("complete:" + level.id);
                foreach (var interaction in level.interactions ?? Array.Empty<CampaignInteraction>())
                    foreach (var fact in interaction.grants ?? Array.Empty<string>()) allowedFacts.Add(fact);
            }
            var completed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in save.completedLevels)
                if (id == null || !levels.ContainsKey(id) || !completed.Add(id)) return Reject(out error, "Неизвестная или повторная пройденная глава.");
            var facts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fact in save.facts)
                if (fact == null || !allowedFacts.Contains(fact) || !facts.Add(fact)) return Reject(out error, "Неизвестный или повторный факт истории.");
            if (string.IsNullOrEmpty(save.level))
                return save.checkpoint == null && completed.Count == levels.Count || Reject(out error, "Кампания ещё не завершена.");
            if (!levels.TryGetValue(save.level, out var active)) return Reject(out error, "Глава сохранения отсутствует.");
            return save.checkpoint == null || Validate(active, save.checkpoint, out error);
        }

        public static bool Validate(CampaignLevel level, CampaignCheckpoint state, out string error)
        {
            error = "";
            if (level == null || state == null || state.level != level.id || state.progress == null ||
                state.health <= 0 || state.playerHealth == null || state.inventory == null || state.playerHealth.current != state.health ||
                !Point(level, state.playerX, state.playerY) || state.interacted == null || state.consumedPickups == null ||
                state.neutralized == null || state.actors == null || state.enemies == null || state.doors == null || state.activePickups == null)
                return Reject(out error, "Контрольная точка неполна.");
            var progress = new CampaignProgress(level);
            if (!progress.Restore(state.progress)) return Reject(out error, "Неверное состояние целей.");
            if (progress.Failed) return Reject(out error, "Контрольная точка уже провалена.");
            if (!new HealthModel(100, .24f).TryRestoreState(state.playerHealth, out error)) return false;
            if (state.playerHealth.dead) return Reject(out error, "В контрольной точке Мазур погиб.");
            if (state.inventory.infiniteAmmo || !new WeaponModel(false).TryRestoreState(state.inventory, out error)) return Reject(out error, "Неверный инвентарь.");
            var interactions = new HashSet<string>(StringComparer.Ordinal);
            foreach (var interaction in level.interactions ?? Array.Empty<CampaignInteraction>()) interactions.Add(interaction.id);
            if (!UniqueKnown(state.interacted, interactions)) return Reject(out error, "Неизвестное взаимодействие.");
            var actors = new Dictionary<string, CampaignActor>(StringComparer.Ordinal);
            foreach (var actor in level.actors ?? Array.Empty<CampaignActor>()) actors.Add(actor.id, actor);
            var actorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var actorState in state.actors)
            {
                if (actorState == null || actorState.id == null || !actorIds.Add(actorState.id) || !actors.TryGetValue(actorState.id, out var actor) ||
                    actorState.healthState == null || actorState.health != actorState.healthState.current || !Point(level, actorState.x, actorState.y) ||
                    actorState.routeIndex < 0 || actorState.routeIndex >= Math.Max(1, actor.route?.Length ?? 0))
                    return Reject(out error, "Неверное состояние персонажа.");
                if ((actor.role != "hostage" && !actorState.freed) || (actorState.carried && (!actorState.freed || actor.role == "vehicle" || actor.role == "passenger")))
                    return Reject(out error, "Неверное состояние сопровождения персонажа.");
                var maximum = actor.health > 0 ? actor.health : actor.role == "vehicle" ? 240 : 100;
                if (!new HealthModel(maximum, actor.role == "vehicle" ? .08f : .24f).TryRestoreState(actorState.healthState, out error)) return false;
                if (actor.required && actorState.healthState.dead) return Reject(out error, "Обязательный персонаж погиб.");
            }
            if (actorIds.Count != actors.Count) return Reject(out error, "В контрольной точке отсутствует персонаж.");
            var actorStatesById = new Dictionary<string, CampaignActorState>(StringComparer.Ordinal);
            foreach (var actorState in state.actors) actorStatesById.Add(actorState.id, actorState);
            foreach (var actor in actors.Values)
                if (actor.role == "passenger" && (string.IsNullOrEmpty(actor.transport) ||
                    !actorStatesById.TryGetValue(actor.transport, out var transport) ||
                    (actor.required && transport.healthState.dead))) return Reject(out error, "Неверное состояние транспорта пассажира.");
            foreach (var interaction in level.interactions ?? Array.Empty<CampaignInteraction>())
                if (Array.IndexOf(state.interacted, interaction.id) >= 0 && (interaction.action == "rescue" || interaction.action == "carry") &&
                    (!actorStatesById.TryGetValue(interaction.target ?? "", out var rescued) || !rescued.freed || (interaction.action == "carry" && !rescued.carried)))
                    return Reject(out error, "Результат спасения не совпадает с состоянием персонажа.");
            var enemies = new Dictionary<string, CampaignEnemy>(StringComparer.Ordinal);
            var pickupKinds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var enemy in level.enemies ?? Array.Empty<CampaignEnemy>())
            { enemies.Add(enemy.id, enemy); pickupKinds.Add(enemy.id + "_weapon", enemy.kind == "melee" || enemy.kind == "butcher" ? "bat" : "pistol"); }
            if (!UniqueKnown(state.neutralized, new HashSet<string>(enemies.Keys, StringComparer.Ordinal))) return Reject(out error, "Неизвестный отступивший противник.");
            var neutralized = new HashSet<string>(state.neutralized, StringComparer.Ordinal);
            var enemyIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemyState in state.enemies)
                if (enemyState == null || enemyState.id == null || !enemies.ContainsKey(enemyState.id) || !enemyIds.Add(enemyState.id) ||
                    !Point(level, enemyState.x, enemyState.y)) return Reject(out error, "Неверное состояние противника.");
            foreach (var id in enemies.Keys)
                if (!neutralized.Contains(id) && !enemyIds.Contains(id)) return Reject(out error, "В контрольной точке отсутствует противник.");
            foreach (var pickup in level.pickups ?? Array.Empty<CampaignPickup>()) pickupKinds.Add(pickup.id, pickup.kind);
            if (!UniqueKnown(state.consumedPickups, new HashSet<string>(pickupKinds.Keys, StringComparer.Ordinal))) return Reject(out error, "Неизвестный подобранный предмет.");
            var pickupIds = new HashSet<string>(StringComparer.Ordinal);
            var consumed = new HashSet<string>(state.consumedPickups, StringComparer.Ordinal);
            foreach (var pickup in state.activePickups)
                if (pickup == null || pickup.id == null || !pickupKinds.TryGetValue(pickup.id, out var kind) || kind != pickup.kind ||
                    consumed.Contains(pickup.id) || !pickupIds.Add(pickup.id) || !Point(level, pickup.x, pickup.y)) return Reject(out error, "Неверный оставшийся предмет.");
            var doors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var door in level.doors ?? Array.Empty<CampaignDoor>()) doors.Add(door.id);
            var doorIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var door in state.doors)
                if (door == null || door.id == null || !doors.Contains(door.id) || !doorIds.Add(door.id)) return Reject(out error, "Неверное состояние двери.");
            return doorIds.Count == doors.Count || Reject(out error, "В контрольной точке отсутствует дверь.");
        }

        private static bool UniqueKnown(string[] values, HashSet<string> known)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values) if (value == null || !known.Contains(value) || !seen.Add(value)) return false;
            return true;
        }
        private static bool Point(CampaignLevel level, float x, float y) => Finite(x) && Finite(y) && x >= 0 && y >= 0 && x <= level.width && y <= level.height;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Reject(out string error, string reason) { error = reason; return false; }
    }
}
