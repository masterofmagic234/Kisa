using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Behavior port of public race_powertrain.gd (Arkon v1.68). Four-speed automatic, SI forces.</summary>
    public sealed class RacePowertrain
    {
        private static readonly float[] RatiosData = { 2.785f, 1.545f, 1, .694f };
        private static readonly float[,] TorqueCurve = { { 850, 135 }, { 2000, 185 }, { 3500, 225 }, { 4500, 230 }, { 5500, 205 }, { 6500, 170 } };
        public static IReadOnlyList<float> Ratios { get; } = Array.AsReadOnly(RatiosData);
        public const float FinalDrive = 4.083f, TireRadius = .30f, MassKg = 1280;
        public const float IdleRpm = 850, RedlineRpm = 6500;
        public const float ReverseMaxSpeed = RaceCalibration.SpeedUnitsPerKmH;
        public const float ReverseAcceleration = ReverseMaxSpeed * 1.8f;
        public float TopSpeedLimit = RaceCalibration.PlayerMaxSpeed;
        public float TorqueMultiplier = 1;
        public int Gear = 1;
        public float EngineRpm = IdleRpm, EngineLoad;
        public float ShiftRemaining, ShiftDuration = .32f;
        public int ShiftSerial;
        public bool ShiftUp = true;
        public float ShiftCooldown => shiftCooldown;
        private float shiftStartRpm = IdleRpm, shiftCooldown;

        public float RoadRpm(float mps, int inGear) => mps / TireRadius * RatiosData[inGear - 1] * FinalDrive * 60 / RaceNumerics.Tau;
        public float TorqueAt(float rpm)
        {
            for (var i = 1; i < TorqueCurve.GetLength(0); i++)
                if (rpm <= TorqueCurve[i, 0])
                    return RaceNumerics.Lerp(TorqueCurve[i - 1, 1], TorqueCurve[i, 1], Scalar.Clamp((rpm - TorqueCurve[i - 1, 0]) / (TorqueCurve[i, 0] - TorqueCurve[i - 1, 0]), 0, 1));
            return TorqueCurve[TorqueCurve.GetLength(0) - 1, 1];
        }
        private void Shift(int toGear)
        {
            ShiftUp = toGear > Gear; shiftStartRpm = EngineRpm; Gear = toGear;
            ShiftDuration = ShiftUp ? .32f : .24f; ShiftRemaining = ShiftDuration; shiftCooldown = .70f; ShiftSerial++;
        }
        public float Step(float speed, float throttle, float brake, float dt, float driveLimit)
        {
            if (dt <= 0) return speed;
            var mps = Math.Max(speed / RaceCalibration.SimulationUnitsPerMps, 0);
            var pedal = brake <= .01f ? Scalar.Clamp(throttle, 0, 1) : 0;
            shiftCooldown = Math.Max(shiftCooldown - dt, 0);
            var coupledRpm = RoadRpm(mps, Gear);
            if (mps < .15f && pedal < .01f) { Gear = 1; ShiftRemaining = 0; coupledRpm = 0; }
            else if (ShiftRemaining <= 0 && shiftCooldown <= 0)
            {
                var upRpm = RaceNumerics.Lerp(2700, 6100, pedal);
                if (Gear < RatiosData.Length && coupledRpm > upRpm) Shift(Gear + 1);
                else if (Gear > 1)
                {
                    var lowerRpm = RoadRpm(mps, Gear - 1);
                    if (coupledRpm < 1400 || (pedal > .85f && coupledRpm < 2900 && lowerRpm < 5600)) Shift(Gear - 1);
                }
            }
            coupledRpm = RoadRpm(mps, Gear);
            var converterSlip = 1600 * pedal * (1 - RaceNumerics.SmoothStep(2, 18, mps));
            var targetRpm = Scalar.Clamp(Math.Max(IdleRpm, coupledRpm + converterSlip), IdleRpm, RedlineRpm);
            var torqueTransfer = 1f;
            if (ShiftRemaining > 0)
            {
                ShiftRemaining = Math.Max(ShiftRemaining - dt, 0);
                var progress = 1 - ShiftRemaining / ShiftDuration;
                EngineRpm = RaceNumerics.Lerp(shiftStartRpm, targetRpm, RaceNumerics.SmoothStep(0, 1, progress));
                torqueTransfer = RaceNumerics.Lerp(.22f, 1, RaceNumerics.SmoothStep(.35f, 1, progress));
            }
            else EngineRpm = RaceNumerics.Lerp(EngineRpm, targetRpm, 1 - (float)Math.Exp(-10 * dt));
            EngineLoad = pedal * torqueTransfer;
            var force = TorqueAt(EngineRpm) * RatiosData[Gear - 1] * FinalDrive * .86f / TireRadius * EngineLoad;
            force *= RaceNumerics.Lerp(1.30f, 1, RaceNumerics.SmoothStep(0, 10, mps));
            force = Math.Min(force * TorqueMultiplier, MassKg * 4.3f);
            // The shoulder torque limit does not snap existing speed to that limit.
            if (speed > driveLimit + .01f) force = 0;
            var aero = .5f * 1.225f * .62f * mps * mps;
            var rolling = MassKg * 9.81f * .013f * RaceNumerics.SmoothStep(0, .8f, mps);
            var engineBraking = (1 - pedal) * Scalar.Clamp((coupledRpm - IdleRpm) / 4500, 0, 1) * MassKg * .65f;
            var acceleration = (force - aero - rolling - engineBraking) / MassKg - Scalar.Clamp(brake, 0, 1) * 8.8f;
            return Scalar.Clamp((mps + acceleration * dt) * RaceCalibration.SimulationUnitsPerMps, 0, TopSpeedLimit);
        }
        public float ReverseStep(float speed, float dt, float limit = ReverseMaxSpeed)
        {
            Gear = 1; ShiftRemaining = 0;
            EngineRpm = RaceNumerics.Lerp(EngineRpm, 950, 1 - (float)Math.Exp(-8 * Math.Max(dt, 0)));
            EngineLoad = .12f;
            return Scalar.MoveToward(Math.Min(speed, 0), -Scalar.Clamp(limit, 0, ReverseMaxSpeed), ReverseAcceleration * Math.Max(dt, 0));
        }
    }

    /// <summary>Exact _step_drive policy from race_car_controller.gd, extracted without the Godot barrier owner.</summary>
    public sealed class RaceDriveInterlock
    {
        public const float ReverseHoldSeconds = .65f;
        public readonly RacePowertrain Powertrain;
        public bool IsPlayer;
        public bool ReverseActive { get; private set; }
        public float ReverseHold => (float)reverseHold;
        public int Gear => ReverseActive ? -1 : Powertrain.Gear;
        private double reverseHold;
        public RaceDriveInterlock(bool isPlayer = true, RacePowertrain powertrain = null)
        { IsPlayer = isPlayer; Powertrain = powertrain ?? new RacePowertrain(); }
        public static Vec2 ContactVelocity(float speed, float lateralVelocity, float impactVelocity)
        {
            return new Vec2(lateralVelocity + impactVelocity, Math.Sign(speed) * (float)Math.Sqrt(Math.Max(0, speed * speed - lateralVelocity * lateralVelocity)));
        }
        public RaceDriveVelocity ApplyContactVelocity(Vec2 value)
        {
            // Collision impulses cannot select reverse. Only the held player interlock can.
            var z = ReverseActive ? value.Y : Math.Max(0, value.Y);
            var speed = new Vec2(value.X, z).Length;
            if (z < 0) speed = -Math.Min(speed, RacePowertrain.ReverseMaxSpeed);
            return new RaceDriveVelocity(value.X, speed);
        }
        public float Step(float speed, float throttle, float brake, float dt, bool enabled, float driveLimit, float offroadAmount = 0, float punctureRemaining = 0)
        {
            if (!IsPlayer) return Powertrain.Step(speed, enabled ? throttle : 0, enabled ? brake : .55f, dt, driveLimit);
            var requested = enabled && brake > .5f && throttle <= .01f;
            if (speed < -.00001f)
            {
                if (requested)
                {
                    ReverseActive = true; reverseHold = ReverseHoldSeconds;
                    var crawlLimit = RacePowertrain.ReverseMaxSpeed * RaceNumerics.Lerp(1, .75f, offroadAmount);
                    if (punctureRemaining > 0) crawlLimit *= .64f;
                    return Powertrain.ReverseStep(speed, dt, crawlLimit);
                }
                ReverseActive = false; reverseHold = 0;
                Powertrain.Step(0, 0, .55f, dt, driveLimit);
                return Scalar.MoveToward(speed, 0, RacePowertrain.ReverseMaxSpeed * 3 * dt);
            }
            ReverseActive = false;
            if (speed > .00001f) reverseHold = 0;
            var next = Powertrain.Step(speed, enabled ? throttle : 0, enabled ? brake : .55f, dt, driveLimit);
            if (requested && next <= .00001f)
            {
                reverseHold += dt;
                if (reverseHold >= ReverseHoldSeconds) { ReverseActive = true; return Powertrain.ReverseStep(0, dt); }
            }
            else reverseHold = 0;
            return next;
        }
    }

    public readonly struct RaceDriveVelocity
    {
        public readonly float LateralVelocity, Speed;
        public RaceDriveVelocity(float lateralVelocity, float speed) { LateralVelocity = lateralVelocity; Speed = speed; }
    }
}
