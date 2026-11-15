using System;
using System.Text.Json;
using Arkon.Core;

namespace Arkon.Parity
{
    /// <summary>Exact checkpoint state must not masquerade as damage or firing.</summary>
    public static class ModelSnapshotCases
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
        private static string State(WeaponModel model) => JsonSerializer.Serialize(model.CaptureState(), Json);
        private static string State(HealthModel model) => JsonSerializer.Serialize(model.CaptureState(), Json);
        private static WeaponState Copy(WeaponState state) => JsonSerializer.Deserialize<WeaponState>(JsonSerializer.Serialize(state, Json), Json);

        public static void Run(Action<bool, string> check)
        {
            var health = new HealthModel(); health.ApplyDamage(31, 8); health.Tick(.1f);
            var savedHealth = health.CaptureState(); var restored = new HealthModel();
            var changed = 0; var damaged = 0; var deaths = 0;
            restored.Changed += (_, __) => changed++; restored.Damaged += (_, __) => damaged++; restored.Death += () => deaths++;
            check(restored.TryRestoreState(savedHealth, out var healthError) && healthError == "" && State(restored) == State(health),
                "Checkpoint restores exact HP and remaining invulnerability instead of applying new damage");
            check(changed == 1 && damaged == 0 && deaths == 0, "Health restore publishes one UI change without synthetic damage/death callbacks");
            savedHealth.current = 1;
            check(health.Current == 69 && restored.Current == 69, "Health capture and restore do not retain the mutable snapshot object");
            restored.Tick(restored.InvulnerabilityRemaining);
            check(restored.ApplyDamage(1) && restored.Current == 68, "Restored invulnerability expires after its saved remainder instead of receiving a fresh duration");

            var dead = new HealthModel(); dead.ForceKill(); changed = damaged = deaths = 0;
            check(restored.TryRestoreState(dead.CaptureState()) && restored.Dead && restored.Current == 0 && changed == 1 && deaths == 0,
                "Restoring a dead actor sets life state without replaying death consequences");
            check(restored.TryRestoreState(new HealthModel().CaptureState()) && !restored.Dead && restored.Current == 100,
                "A valid fresh life state can replace a dead model without fake healing/damage");
            foreach (var bad in new[]
            {
                new HealthState { maximum = 200, current = 100 },
                new HealthState { maximum = 100, current = -1, dead = true },
                new HealthState { maximum = 100, current = 101 },
                new HealthState { maximum = 100, current = 0 },
                new HealthState { maximum = 100, current = 100, dead = true },
                new HealthState { maximum = 100, current = 100, invulnerabilityRemaining = float.NaN },
                new HealthState { maximum = 100, current = 100, invulnerabilityRemaining = float.PositiveInfinity },
                new HealthState { maximum = 100, current = 100, invulnerabilityRemaining = -.1f },
                new HealthState { maximum = 100, current = 100, invulnerabilityRemaining = .25f }
            })
            {
                var before = State(restored); var events = changed;
                check(!restored.TryRestoreState(bad, out healthError) && healthError != "" && State(restored) == before && changed == events,
                    "Invalid health snapshot is rejected atomically without UI events");
            }
            check(!restored.TryRestoreState(null), "Null health snapshot is rejected");

            var weapon = new WeaponModel(false); weapon.Equip("shotgun", 5); weapon.ConsumeShot(); weapon.Tick(.13f);
            var savedWeapon = weapon.CaptureState(); var inventory = new WeaponModel(false);
            var changes = 0; var shots = 0; var order = "";
            inventory.Changed += (_, __) => { changes++; order += "C"; };
            inventory.ShotConsumed += (_, __) => { shots++; order += "S"; };
            check(inventory.TryRestoreState(savedWeapon, out var weaponError) && weaponError == "" && State(inventory) == State(weapon),
                "Checkpoint restores all reserves, selected weapon and in-flight cooldown exactly");
            check(changes == 1 && shots == 0 && order == "C", "Restoring a positive cooldown does not synthesize a shot or enemy noise");
            check(!inventory.ConsumeShot() && shots == 0, "A restored unexpired cooldown still blocks firing");
            inventory.Tick(inventory.Cooldown); order = "";
            check(inventory.ConsumeShot() && order == "CS" && shots == 1 && inventory.Ammo == 3,
                "A genuine shot publishes existing Changed before one ShotConsumed and consumes exactly one round");
            inventory.Equip("pistol");
            check(shots == 1 && inventory.Ammo == 12, "Weapon switching changes UI without emitting a shot event or corrupting another reserve");
            savedWeapon.reserves[0].count = 999; savedWeapon.currentId = "bat";
            check(weapon.CurrentId == "shotgun" && weapon.GetReserve("pistol") == 12, "Weapon capture returns detached reserve objects");

            var checkpoint = weapon.CaptureState();
            var invalid = Copy(checkpoint); invalid.currentId = "unknown"; Reject(inventory, invalid, check, "Unknown current weapon");
            invalid = Copy(checkpoint); invalid.ammo++; Reject(inventory, invalid, check, "Mismatched equipped ammo");
            invalid = Copy(checkpoint); invalid.cooldown = float.NaN; Reject(inventory, invalid, check, "NaN cooldown");
            invalid = Copy(checkpoint); invalid.cooldown = float.PositiveInfinity; Reject(inventory, invalid, check, "Infinite cooldown");
            invalid = Copy(checkpoint); invalid.cooldown = -.1f; Reject(inventory, invalid, check, "Negative cooldown");
            invalid = Copy(checkpoint); invalid.cooldown = 50; Reject(inventory, invalid, check, "Unbounded cooldown");
            invalid = Copy(checkpoint); invalid.reserves[0].id = "unknown"; Reject(inventory, invalid, check, "Unknown reserve ID");
            invalid = Copy(checkpoint); invalid.reserves[1].id = invalid.reserves[0].id; Reject(inventory, invalid, check, "Duplicate reserve ID");
            invalid = Copy(checkpoint); invalid.reserves[0].count = -1; Reject(inventory, invalid, check, "Negative finite ammunition");
            invalid = Copy(checkpoint); invalid.reserves = new[] { invalid.reserves[0] }; Reject(inventory, invalid, check, "Missing configured reserve");
            invalid = Copy(checkpoint); invalid.reserves[0] = null; Reject(inventory, invalid, check, "Null reserve entry");
            Reject(inventory, null, check, "Null weapon snapshot");

            var infinite = new WeaponModel(); infinite.Equip("shotgun"); infinite.ConsumeShot();
            check(inventory.TryRestoreState(infinite.CaptureState()) && inventory.InfiniteAmmo && inventory.Ammo == -1,
                "Infinite source inventory preserves its minus-one reserve sentinel and cooldown");
            infinite.Equip("bat");
            check(inventory.TryRestoreState(infinite.CaptureState()) && inventory.CurrentId == "bat" && inventory.Ammo == 0,
                "Melee state restores without inventing an ammunition reserve for the bat");
        }

        private static void Reject(WeaponModel model, WeaponState state, Action<bool, string> check, string message)
        {
            var before = State(model); var changes = 0; var shots = 0;
            Action<string, int> changed = (_, __) => changes++; Action<string, int> shot = (_, __) => shots++;
            model.Changed += changed; model.ShotConsumed += shot;
            var accepted = model.TryRestoreState(state, out var error);
            model.Changed -= changed; model.ShotConsumed -= shot;
            check(!accepted && error != "" && State(model) == before && changes == 0 && shots == 0, message + " rejected atomically without events");
        }
    }
}
