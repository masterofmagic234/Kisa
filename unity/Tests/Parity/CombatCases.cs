using System;
using System.Collections.Generic;
using Arkon.Core;

namespace Arkon.Parity
{
    /// <summary>
    /// Behavioral source-parity cases, independent of Unity adapters. They do
    /// not claim to test Rigidbody casts, wall collision or playable scenes.
    /// </summary>
    public static class CombatCases
    {
        private static bool Near(float a, float b, float epsilon = .0001f) => Math.Abs(a - b) < epsilon;

        public static void Run(Action<bool, string> check)
        {
            Health(check);
            Weapons(check);
            InputAndMovement(check);
        }

        private static void Health(Action<bool, string> check)
        {
            var model = new HealthModel();
            var changes = 0; var damageEvents = 0; var deaths = 0;
            var eventOrder = new List<string>();
            var lastSource = 0; var lastDamage = 0;
            model.Changed += (current, maximum) => { changes++; eventOrder.Add("changed"); };
            model.Damaged += (amount, source) =>
            {
                damageEvents++; lastSource = source; lastDamage = amount; eventOrder.Add("damaged");
            };
            model.Death += () => { deaths++; eventOrder.Add("death"); };
            check(model.Current == 100 && model.Maximum == 100 && !model.Dead, "Health starts at released 100 HP.");
            check(!model.ApplyDamage(0) && !model.ApplyDamage(-1) && changes == 0
                && model.InvulnerabilityRemaining == 0, "Rejected damage changes neither health nor immunity/events.");
            check(model.ApplyDamage(30, 73) && model.Current == 70 && lastSource == 73 && lastDamage == 30,
                "Accepted damage reaches the owner with its source ID.");
            check(!model.ApplyDamage(30) && model.Current == 70 && damageEvents == 1,
                "The same-frame duplicate hit is rejected.");
            model.Tick(.23f);
            check(!model.ApplyDamage(1), "Invulnerability lasts through the released .24-second window.");
            model.Tick(.011f);
            check(model.ApplyDamage(10) && model.Current == 60, "Damage is accepted after immunity expires.");
            model.Heal(35);
            check(model.Current == 95, "The released +35 medkit amount is supported.");
            model.Heal(int.MaxValue);
            check(model.Current == 100, "Healing clamps at maximum without integer overflow.");
            model.Tick(.24f);
            eventOrder.Clear();
            check(model.ApplyDamage(999) && model.Current == 0 && model.Dead,
                "Lethal damage clamps health at zero.");
            check(string.Join(",", eventOrder) == "changed,damaged,death" && lastDamage == 999,
                "Lethal events retain source ordering and report the incoming damage amount.");
            model.Heal(100);
            check(!model.ForceKill() && !model.ApplyDamage(1) && model.Current == 0 && deaths == 1,
                "Dead actors cannot heal, take another hit or emit another death.");
            model.Reset(50);
            check(!model.Dead && model.Current == 50 && model.Maximum == 50 && model.CanTakeDamage,
                "Reset starts a fresh actor lifetime and clears immunity.");
            var reentrant = new HealthModel(1, 0); var once = 0;
            reentrant.Changed += (current, maximum) => { if (current == 0) reentrant.ForceKill(); };
            reentrant.Death += () => once++;
            reentrant.ApplyDamage(100);
            check(once == 1, "Death remains idempotent during an observer's reentrant ForceKill.");
        }

        private static void Weapons(Action<bool, string> check)
        {
            check(Near(WeaponCatalog.Pistol.FireInterval, .28f) && WeaponCatalog.Pistol.Damage == 100
                && Near(WeaponCatalog.Pistol.ProjectileSpeed, 650), "Pistol uses the released .tres balance rather than .18-second base defaults.");
            var shotgun = WeaponCatalog.Shotgun;
            check(Near(shotgun.FireInterval, .72f) && shotgun.Pellets == 5
                && Near(shotgun.SpreadAngles[0], -.12f) && Near(shotgun.SpreadAngles[1], -.06f)
                && Near(shotgun.SpreadAngles[2], 0) && Near(shotgun.SpreadAngles[3], .06f)
                && Near(shotgun.SpreadAngles[4], .12f), "Shotgun retains five authored spread angles in radians.");
            check(Near(WeaponCatalog.Bat.FireInterval, .38f) && !WeaponCatalog.Bat.ConsumesAmmo
                && WeaponCatalog.Bat.Melee && Near(WeaponCatalog.Bat.ActionRange, 48),
                "Bat retains melee, ammo-free and action-range settings.");
            var finite = new WeaponModel(false, initialAmmo: 2);
            check(finite.ConsumeShot() && finite.Ammo == 1 && !finite.ConsumeShot(),
                "A finite pistol shot consumes one round and blocks an immediate duplicate.");
            finite.Tick(.27f);
            check(!finite.CanFire, "The pistol cannot fire before its cooldown completes.");
            finite.Tick(.011f);
            check(finite.ConsumeShot() && finite.Ammo == 0, "The second shot is accepted at the next permitted interval.");
            finite.Tick(1);
            check(!finite.CanFire, "An empty finite weapon stays empty after the cooldown expires.");
            finite.Equip(WeaponCatalog.ShotgunId, 2);
            finite.ConsumeShot();
            finite.Equip(WeaponCatalog.PistolId);
            check(finite.Ammo == 0 && finite.GetReserve(WeaponCatalog.ShotgunId) == 1,
                "Switching cannot convert remaining shotgun shells into pistol rounds.");
            finite.Equip(WeaponCatalog.PistolId, 4);
            finite.ConsumeShot();
            var before = finite.Cooldown;
            check(!finite.Equip("missing", 50) && finite.CurrentId == WeaponCatalog.PistolId
                && finite.Ammo == 3 && Near(finite.Cooldown, before),
                "Unknown weapons leave the current weapon, reserve and cooldown unchanged.");
            finite.Equip(WeaponCatalog.ShotgunId, -50);
            check(finite.Ammo == 1 && finite.Cooldown == 0,
                "Negative pickups do not remove reserve; equip retains the source cooldown reset.");
            finite.Equip(WeaponCatalog.BatId);
            check(finite.Ammo == 0 && finite.ConsumeShot() && !finite.ConsumeShot(),
                "Melee needs no ammunition but still owns a cooldown.");
            var unlimited = new WeaponModel();
            check(unlimited.InfiniteAmmo && unlimited.Ammo == -1 && unlimited.ConsumeShot(),
                "The released player scene starts with infinite ammunition and its -1 sentinel.");
            unlimited.Tick(.28f);
            check(unlimited.ConsumeShot() && unlimited.Ammo == -1 && unlimited.GetReserve("pistol") == -1,
                "Infinite shots do not consume or rewrite their reserve.");
        }

        private static void InputAndMovement(Action<bool, string> check)
        {
            var latch = new InputLatch();
            latch.Sample(new Vec2(1, 1), new Vec2(0, -1), true, true, true, true);
            latch.Sample(new Vec2(1, 1), new Vec2(.1f, 0), true, false, false, true);
            var first = latch.Consume(); var second = latch.Consume();
            check(first.ActionPressed && first.ThrowPressed && !second.ActionPressed && !second.ThrowPressed,
                "A press survives repeated input sampling and is consumed by only one physics tick.");
            check(first.FireHeld && second.FireHeld && Near(first.Movement.Length, 1)
                && first.Aim.Equals(new Vec2(0, -1)),
                "Held fire persists, diagonal input is normalized, and tiny aim input retains useful aim.");
            latch.Sample(Vec2.Right, Vec2.Right, true, true, true, false, false);
            var unarmed = latch.Consume();
            check(!unarmed.FireHeld && !unarmed.ThrowPressed && unarmed.ActionPressed,
                "Combat disabling suppresses firing/throwing while allowing context action input.");
            latch.Clear(); var clear = latch.Consume();
            check(clear.Movement.Equals(Vec2.Zero) && !clear.FireHeld && !clear.ActionPressed
                && !clear.ThrowPressed && clear.Aim.Equals(Vec2.Right),
                "Clearing controls removes pending/held input and preserves aim.");
            var motor = new TopdownMotor();
            var right = new TopdownInput(Vec2.Right, Vec2.Right);
            motor.Step(right, 1f / 60);
            check(Near(motor.Velocity.X, 15), "The first released 60-Hz movement step accelerates by 15 pixels/s.");
            var position = motor.Velocity / 60;
            for (var i = 1; i < 20; i++) { motor.Step(right, 1f / 60); position += motor.Velocity / 60; }
            check(Near(motor.Velocity.Length, 120) && position.X > 25 && position.X < 50,
                "Unobstructed motion reaches released speed and matches the source 20-tick displacement interval.");
            motor.Step(new TopdownInput(new Vec2(1, 1), Vec2.Right), 1);
            check(Near(motor.Velocity.Length, 120), "Diagonal movement does not exceed released move speed.");
            motor.Step(new TopdownInput(Vec2.Right, Vec2.Right, sprintHeld: true), 1);
            check(Near(motor.Velocity.Length, 141.6f), "Sprint retains the released 1.18 multiplier.");
            motor.ControlsEnabled = false;
            var blocked = motor.Step(new TopdownInput(Vec2.Right, Vec2.Right, true, true, true), .1f);
            check(!blocked.FireRequested && !blocked.ActionRequested && !blocked.ThrowRequested
                && motor.Velocity.Length < 40, "Disabled controls suppress actions and brake using source friction.");
            motor.ControlsEnabled = true;
            motor.ApplyFlashbang(8);
            check(Near(motor.FlashbangRemaining, 1.7f) && motor.Velocity.Equals(Vec2.Zero),
                "Flashbang clamps to 1.7 seconds and immediately clears motion.");
            motor.ApplyFlashbang(.2f);
            check(Near(motor.FlashbangRemaining, 1.7f), "A shorter flashbang cannot shorten an existing stun.");
            check(!motor.Step(new TopdownInput(Vec2.Right, Vec2.Right, true), .5f).FireRequested,
                "Flashbang blocks controls while active.");
            check(motor.Step(new TopdownInput(Vec2.Right, Vec2.Right, true), 1.3f).FireRequested,
                "Controls return in the tick where the flashbang expires.");
            var walking = new TopdownMotor { CombatEnabled = false };
            walking.Step(new TopdownInput(new Vec2(0, -1), Vec2.Right), 1);
            walking.SetResolvedVelocity(Vec2.Zero);
            check(Near(walking.FacingRadians, 0),
                "Wall-resolved zero velocity retains the earlier facing instead of the provisional movement angle.");
        }
    }
}
