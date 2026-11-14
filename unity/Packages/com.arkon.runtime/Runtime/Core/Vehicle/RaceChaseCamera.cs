using System;

namespace Arkon.Core
{
    /// <summary>Shared road and model projection use one chase-camera pose.</summary>
    public sealed class RaceChaseCamera
    {
        public const float Behind = 6;
        private static readonly int DefaultSprintSegments = RaceCalibration.DefaultSprint.TotalSegments;
        public float WorldX { get; private set; }
        public float WorldZ { get; private set; }
        public float LateralOffset { get; private set; }
        public float BehindDistance { get; private set; } = Behind;
        public float Roll { get; private set; }
        public float Zoom { get; private set; } = 1;
        public float HorizonOffset { get; private set; }
        public float Yaw { get; private set; }
        public float RoadHeight { get; private set; }
        public float RoadGrade { get; private set; }
        private bool initialized;

        public void Reset(RaceCarMotion car)
        {
            initialized = false;
            LateralOffset = Roll = HorizonOffset = Yaw = 0;
            BehindDistance = Behind;
            Zoom = 1;
            Update(car, 0);
        }

        public void Update(RaceCarMotion car, float delta)
        {
            var steer = Scalar.Clamp(car.SteerApplied, -1, 1);
            var speed = Scalar.Clamp(car.Speed / RaceCalibration.PlayerMaxSpeed, 0, 1);
            RoadHeight = RaceCalibration.RoadHeight(car.WorldZ);
            RoadGrade = RaceCalibration.RoadGrade(car.WorldZ);
            Yaw = car.HeadingYaw;
            BehindDistance = Behind * (float)Math.Cos(Yaw);
            WorldZ = car.WorldZ - BehindDistance;
            var targetX = car.WorldX - Behind * (float)Math.Sin(Yaw);
            var blend = 1 - (float)Math.Exp(-7.5f * Math.Max(delta, 0));
            WorldX = initialized ? RaceNumerics.Lerp(WorldX, targetX, blend) : targetX;
            initialized = true;
            if (car.Route.TrackSize == DefaultSprintSegments && WorldZ >= RaceCalibration.TunnelStart && WorldZ < RaceCalibration.TunnelEnd)
            {
                var centerline = car.RoadCenterAt(WorldZ);
                const float clearance = 6.1f - .25f;
                WorldX = Scalar.Clamp(WorldX, centerline - clearance, centerline + clearance);
            }

            LateralOffset = WorldX - (car.WorldX - car.LateralOffset);
            Roll = RaceNumerics.Lerp(Roll, -steer * speed * (float)(Math.PI / 180), blend);
            Zoom = RaceNumerics.Lerp(Zoom, RaceNumerics.Lerp(1, .94f, speed), blend);
            HorizonOffset = RaceNumerics.Lerp(HorizonOffset, Scalar.Clamp(car.LongitudinalAcceleration / 40, -1, 1) * .008f, blend);
        }
    }
}
