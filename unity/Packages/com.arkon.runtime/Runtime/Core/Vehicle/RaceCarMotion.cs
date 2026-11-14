using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Source pseudo-3D chassis. X points right, Z points forward, and angles are radians.</summary>
    public sealed class RaceCarMotion : IRaceContactBody
    {
        public const float MaximumHeadingYaw = .84f;
        public const float CornerGrip = 12f;
        private const float SteerResponse = 8f;
        private const float LateralGripResponse = 15f;
        private const float HeadingResponse = 7f;
        private const float ShoulderEdge = RaceCalibration.RoadWidth * .5f + .85f + .25f;
        private const float ShoulderWidth = 2.2f;
        private const float MinimumDriveSpeed = 6f;

        private readonly IReadOnlyList<int> pattern;
        private readonly IReadOnlyList<float> track;
        private readonly RaceDriveInterlock drive;
        private float steerInput, throttle, brake;
        private float heading, steeringLock = .52f, impactVelocity;

        public RaceRouteState Route { get; }
        public RacePowertrain Powertrain => drive.Powertrain;
        public bool IsPlayer => drive.IsPlayer;
        public int Gear => drive.Gear;
        public float SpeedLimitMultiplier { get; set; } = .92f;
        public float Speed { get; private set; }
        public float WorldX { get; private set; }
        public float WorldZ => Route.WorldZ;
        public float LateralOffset { get; private set; }
        public float LateralVelocity { get; private set; }
        public float SteerApplied { get; private set; }
        public float SteeringAngle { get; private set; }
        public float HeadingYaw => heading;
        public float TravelYaw { get; private set; }
        public float SlipAngle { get; private set; }
        public float DistanceTravelled { get; private set; }
        public float LongitudinalAcceleration { get; private set; }
        public float OffroadAmount { get; private set; }
        public float WallContact { get; private set; }
        public float PunctureRemaining { get; private set; }
        public float TyreSqueal { get; private set; }
        public float TyreSkidStrength { get; private set; }
        public Vec2 Position => new Vec2(WorldX, WorldZ);
        public float InverseMass => 1;
        public Vec2 ContactVelocity => RaceDriveInterlock.ContactVelocity(Speed, LateralVelocity, impactVelocity);

        public RaceCarMotion(bool isPlayer, IReadOnlyList<int> pattern, IReadOnlyList<float> track,
            int gridSlot = 0, float lane = 0)
        {
            if (pattern == null) throw new ArgumentNullException(nameof(pattern));
            if (track == null) throw new ArgumentNullException(nameof(track));
            if (pattern.Count != track.Count) throw new ArgumentException("One centerline sample is required per segment.");
            this.pattern = pattern;
            this.track = track;
            drive = new RaceDriveInterlock(isPlayer);
            Route = new RaceRouteState(pattern.Count);
            Route.PlaceOnGrid(gridSlot);
            LateralOffset = lane;
            WorldX = RoadCenterAt(WorldZ) + lane;
        }

        public static float SteeringLock(float speedRatio) => RaceNumerics.Lerp(.52f, .22f, Scalar.Clamp(speedRatio, 0, 1));

        public static float SteerForHeading(float desiredHeading, float speedRatio) => Scalar.Clamp(
            desiredHeading / (MaximumHeadingYaw * RaceNumerics.Lerp(1, .48f, speedRatio)), -1, 1);

        public float RoadCenterAt(float z) => RaceMath.TrackCenterX(z / RaceCalibration.SegmentHeight, track);

        public void SetInputs(float steer, float acceleration, float braking)
        {
            steerInput = Scalar.Clamp(steer, -1, 1);
            throttle = Scalar.Clamp(acceleration, 0, 1);
            brake = Scalar.Clamp(braking, 0, 1);
        }

        public void Tick(float delta, bool allowControl)
        {
            var dt = Math.Max(delta, 0);
            var maximumSpeed = RaceCalibration.PlayerMaxSpeed * (IsPlayer ? 1 : SpeedLimitMultiplier);
            var previousSpeed = Speed;
            var previousZ = Route.PhysicalProgress * RaceCalibration.SegmentHeight;
            var previousCenter = RaceMath.TrackCenterX(Route.PhysicalProgress, track);
            var previousOffset = LateralOffset;
            PunctureRemaining = Math.Max(0, PunctureRemaining - dt);

            var gripDemand = 0f;
            if (allowControl)
            {
                var limit = PunctureRemaining > 0 ? maximumSpeed * .64f : maximumSpeed;
                if (Math.Abs(LateralOffset) > ShoulderEdge)
                    limit *= RaceNumerics.Lerp(.85f, .55f, ShoulderAmount(LateralOffset));
                Speed = drive.Step(Speed, throttle, brake, dt, true, limit, OffroadAmount, PunctureRemaining);
                gripDemand = Steer(dt, maximumSpeed);
            }
            else
            {
                Speed = drive.Step(Speed, throttle, brake, dt, false, maximumSpeed);
                LateralVelocity = 0;
                SteerApplied = Scalar.MoveToward(SteerApplied, 0, SteerResponse * dt);
                SteeringAngle = 0;
            }

            var impactDecay = (float)Math.Exp(-3.2f * dt);
            if (allowControl && Speed > 3) LateralOffset += impactVelocity * (1 - impactDecay) / 3.2f;
            impactVelocity = allowControl && Speed > 3 ? impactVelocity * impactDecay : 0;

            // Total speed includes lateral travel. Apply the swept bounds before
            // advancing canonical track progress or presenting world coordinates.
            var forwardSpeed = RaceDriveInterlock.ContactVelocity(Speed, LateralVelocity, 0).Y;
            var start = new Vec2(previousCenter + previousOffset, previousZ);
            var target = new Vec2(previousCenter + LateralOffset, previousZ + forwardSpeed * dt);
            var motion = RaceBarriers.Sweep(start, target, pattern, track, heading);
            WallContact = motion.Contact;
            var driving = allowControl && throttle > .01f && brake < .01f;
            var minimumSpeed = driving ? MinimumDriveSpeed : 0;
            if (WallContact > 0)
            {
                Speed = Speed < 0 ? Speed * .55f : Math.Max(minimumSpeed, Speed * .55f);
                LateralVelocity = (motion.WorldX - start.X) / Math.Max(dt, .0001f);
            }

            Route.AdvanceSegments((motion.WorldZ - previousZ) / RaceCalibration.SegmentHeight);
            WorldX = motion.WorldX;
            LateralOffset = WorldX - RaceMath.TrackCenterX(Route.PhysicalProgress, track);
            OffroadAmount = ShoulderAmount(LateralOffset);
            if (Math.Abs(LateralOffset) > ShoulderEdge && Speed >= 0)
            {
                var penalty = RaceNumerics.Lerp(4, 8, OffroadAmount);
                Speed = Math.Max(Speed - penalty * dt, Math.Min(Speed, minimumSpeed));
            }

            TravelYaw = Math.Abs(Speed) > .001f ? (float)Math.Atan2(LateralVelocity, forwardSpeed) : HeadingYaw;
            var rollingHeading = Speed < 0 ? WrapAngle(TravelYaw - (float)Math.PI) : TravelYaw;
            SlipAngle = Scalar.Clamp(WrapAngle(HeadingYaw - rollingHeading), -.20f, .20f);
            LongitudinalAcceleration = (Speed - previousSpeed) / Math.Max(dt, .0001f);
            DistanceTravelled += Speed * dt;
            UpdateTyreLoad(dt, allowControl, gripDemand);
        }

        private float Steer(float dt, float maximumSpeed)
        {
            var speedRatio = Scalar.Clamp(Math.Abs(Speed) / maximumSpeed, 0, 1);
            var response = Math.Abs(steerInput) < Math.Abs(SteerApplied) ? SteerResponse * 1.35f : SteerResponse;
            SteerApplied = Scalar.MoveToward(SteerApplied, steerInput, response * dt);
            steeringLock = RaceNumerics.Lerp(steeringLock, SteeringLock(speedRatio), 1 - (float)Math.Exp(-6 * dt));
            SteeringAngle = SteerApplied * steeringLock;
            var moving = Scalar.Clamp((Speed - .8f) / 3, 0, 1);
            var targetHeading = SteerApplied * MaximumHeadingYaw * RaceNumerics.Lerp(1, .48f, speedRatio);
            var yawChange = WrapAngle(targetHeading - heading) * (1 - (float)Math.Exp(-HeadingResponse * moving * dt));
            var yawLimit = CornerGrip / Math.Max(Speed, 3);

            if (Speed < 0)
            {
                moving = Scalar.Clamp(Math.Abs(Speed) / .16f, 0, 1);
                heading = Scalar.Clamp(heading + Speed / 2.55f * (float)Math.Tan(SteeringAngle) * dt,
                    -MaximumHeadingYaw, MaximumHeadingYaw);
            }
            else heading += Scalar.Clamp(yawChange, -yawLimit * dt, yawLimit * dt);

            var previous = LateralVelocity;
            var desired = (float)Math.Sin(heading) * Speed * moving;
            var grip = LateralGripResponse * RaceNumerics.Lerp(1, .90f, speedRatio);
            LateralVelocity = moving > 0 ? RaceNumerics.Lerp(previous, desired, 1 - (float)Math.Exp(-grip * dt)) : 0;
            if (moving > 0) LateralOffset += (LateralVelocity + previous) * .5f * dt;
            return Math.Abs(yawChange) / Math.Max(yawLimit * dt, .00001f);
        }

        private void UpdateTyreLoad(float dt, bool allowControl, float gripDemand)
        {
            var kmh = Math.Abs(Speed) / RaceCalibration.SpeedUnitsPerKmH;
            var slip = RaceNumerics.SmoothStep(.055f, .12f, Math.Abs(SlipAngle));
            var turn = RaceNumerics.SmoothStep(1.5f, 3, gripDemand) * RaceNumerics.SmoothStep(.65f, .95f, Math.Abs(SteerApplied));
            var squeal = Math.Max(slip, turn) * RaceNumerics.SmoothStep(110, 170, kmh);
            var braking = allowControl ? RaceNumerics.SmoothStep(.35f, .95f, brake) : 0;
            var target = Math.Max(squeal, braking) * RaceNumerics.SmoothStep(8, 28, kmh) * (1 - OffroadAmount);
            TyreSqueal = kmh > 1 ? RaceNumerics.Lerp(TyreSqueal, target, 1 - (float)Math.Exp(-14 * dt)) : 0;
            TyreSkidStrength = kmh > 8 ? TyreSqueal : 0;
        }

        public void ApplySideImpact(float impulse, float speedLoss)
        {
            if (Speed < 3) return;
            impactVelocity = Scalar.Clamp(impactVelocity + impulse, -4, 4);
            Speed *= 1 - Scalar.Clamp(speedLoss, 0, .12f);
        }

        public void HitSpikes()
        {
            if (PunctureRemaining > 0) return;
            PunctureRemaining = 3.6f;
            Speed *= .62f;
        }

        public void ApplySpeedRetention(float fraction) => Speed *= Scalar.Clamp(fraction, 0, 1);

        public void ResolveContact(Vec2 correction)
        {
            var motion = RaceBarriers.Sweep(Position, Position + correction, pattern, track, heading);
            Route.SetFromPhysicalWorldZ(motion.WorldZ);
            WorldX = motion.WorldX;
            LateralOffset = WorldX - RoadCenterAt(WorldZ);
            WallContact = Math.Max(WallContact, motion.Contact);
        }

        public void ApplyContactVelocity(Vec2 value)
        {
            var corrected = drive.ApplyContactVelocity(value);
            LateralVelocity = corrected.LateralVelocity;
            Speed = corrected.Speed;
        }

        private static float ShoulderAmount(float lateral) => Scalar.Clamp((Math.Abs(lateral) - ShoulderEdge) / ShoulderWidth, 0, 1);
        private static float WrapAngle(float yaw) => RaceNumerics.PosMod(yaw + (float)Math.PI, RaceNumerics.Tau) - (float)Math.PI;
    }
}
