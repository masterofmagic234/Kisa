using System;
using Arkon.Core;

public static class VehicleCases
{
    private static bool Near(float a, float b, float tolerance = .001f) => Math.Abs(a - b) <= tolerance;
    public static void Run(Action<bool, string> check)
    {
        CheckPowertrain(check);
        CheckReverse(check);
        CheckTrackAndCanonicalProgress(check);
        CheckProjection(check);
        CheckContacts(check);
        CheckRaceState(check);
    }
    private static void CheckPowertrain(Action<bool, string> check)
    {
        var engine = new RacePowertrain();
        check(engine.TorqueAt(0) == 135 && engine.TorqueAt(1425) == 160 && engine.TorqueAt(4500) == 230 && engine.TorqueAt(7000) == 170,
            "Vehicle torque uses original piecewise interpolation and endpoint clamps");
        // Golden values emitted by the actual public GDScript on Godot4.6.3, not recomputed by this test.
        check(Near(engine.RoadRpm(10, 1), 3619.551054f, .002f), "Vehicle wheel speed, ratio, final drive and RPM retain source units");
        var speed = 0f;
        for (var tick = 1; tick <= 600; tick++)
        {
            speed = engine.Step(speed, 1, 0, 1f / 60, RaceCalibration.PlayerMaxSpeed);
            if (tick == 1) check(Near(speed, .08256f, .00001f) && Near(engine.EngineRpm, 965.138706f, .003f), "Vehicle launch first step matches Godot oracle");
            if (tick == 60) check(Near(speed, 4.819712f, .002f) && engine.Gear == 1, "Vehicle first second of launch matches source acceleration");
            if (tick == 180) check(Near(speed, 14.381996f, .003f) && Near(engine.EngineRpm, 4858.40112f, .1f), "Vehicle three-second torque-converter launch matches source");
        }
        check(Near(speed, 36.227622f, .004f) && engine.Gear == 3 && engine.ShiftSerial == 2 && Near(engine.EngineRpm, 4063.7605f, .2f),
            "Vehicle ten-second automatic gear sequence matches original source golden trace");
        var serial = engine.ShiftSerial; var rpm = engine.EngineRpm;
        check(engine.Step(100, 1, 0, 0, 1) == 100 && engine.ShiftSerial == serial && engine.EngineRpm == rpm,
            "Zero-time powertrain step preserves original speed and state, including an over-cap input");
        var limit = new RacePowertrain();
        check(Near(limit.Step(20, 1, 0, 1f / 60, 1), 19.995835f, .001f), "Shoulder drive limit cuts torque without teleporting speed to the cap");
        var up = new RacePowertrain(); up.Step(40, 1, 0, 1f / 60, RaceCalibration.PlayerMaxSpeed);
        check(up.Gear == 2 && up.ShiftUp && up.ShiftSerial == 1 && Near(up.ShiftDuration, .32f) && Near(up.ShiftRemaining, .30333333f),
            "Automatic upshift immediately selects next gear and opens original .32-second torque-transfer window");
        var down = new RacePowertrain { Gear = 4 }; down.Step(20, 1, 0, 1f / 60, RaceCalibration.PlayerMaxSpeed);
        check(down.Gear == 3 && !down.ShiftUp && Near(down.ShiftDuration, .24f) && Near(down.ShiftRemaining, .22333333f),
            "Kickdown retains lower-gear RPM interlock and shorter original shift window");
        var brakeA = new RacePowertrain(); var brakeB = new RacePowertrain();
        check(brakeA.Step(20, 1, 1, .1f, 100) == brakeB.Step(20, 0, 1, .1f, 100), "Brake suppresses throttle instead of fighting it");
        var stopped = brakeA.Step(0, 0, 1, .1f, 100);
        check(stopped == 0 && brakeA.Gear == 1 && brakeA.ShiftRemaining == 0, "Stopped vehicle returns to first gear without inventing reverse");
    }
    private static void CheckReverse(Action<bool, string> check)
    {
        var drive = new RaceDriveInterlock(); var speed = 0f;
        for (var i = 0; i < 38; i++) speed = drive.Step(speed, 0, 1, 1f / 60, true, 70);
        check(speed == 0 && !drive.ReverseActive, "Player brake-hold cannot select reverse before .65 seconds at rest");
        speed = drive.Step(speed, 0, 1, 1f / 60, true, 70);
        check(speed < 0 && drive.ReverseActive && drive.Gear == -1, "39 stationary 60Hz hold ticks select the emergency reverse crawl");
        for (var i = 0; i < 120; i++) speed = drive.Step(speed, 0, 1, 1f / 60, true, 70);
        check(Near(speed, -.32f, .00001f) && Near(-speed / RaceCalibration.SpeedUnitsPerKmH, 1), "Emergency reverse saturates at exactly one kilometre per hour in original race units");
        var reversingImpulse = drive.ApplyContactVelocity(new Vec2(2, -30));
        check(reversingImpulse.Speed == -.32f && reversingImpulse.LateralVelocity == 2, "An already selected crawl preserves signed impact velocity but bounds reverse speed");
        for (var i = 0; i < 120; i++) speed = drive.Step(speed, 0, 1, 1f / 60, true, 70, 1, 2);
        check(Near(speed, -.1536f, .00001f), "Offroad and puncture reduce powered reverse to the original .75 × .64 crawl cap");
        speed = drive.Step(speed, 1, 0, .1f, true, 70);
        check(!drive.ReverseActive && speed < 0, "Releasing the reverse request coasts toward zero without a forward-speed teleport");
        for (var i = 0; i < 20; i++) speed = drive.Step(speed, 0, 0, .1f, false, 70);
        check(speed == 0 && !drive.ReverseActive && drive.ReverseHold == 0, "Disabled input stops reverse and clears its interlock");
        var ai = new RaceDriveInterlock(false);
        for (var i = 0; i < 120; i++) speed = ai.Step(0, 0, 1, 1f / 60, true, 70);
        check(speed == 0 && !ai.ReverseActive, "Pursuit AI cannot select the player-only emergency reverse");
        var forward = new RaceDriveInterlock(); speed = 15;
        for (var i = 0; i < 30; i++) speed = forward.Step(speed, 0, 1, 1f / 60, true, 70);
        check(speed > 0 && !forward.ReverseActive && forward.ReverseHold == 0, "Holding brake while moving does not accumulate a stationary reverse timer");
        var frontalImpulse = forward.ApplyContactVelocity(new Vec2(2, -30));
        check(frontalImpulse.Speed == 2 && frontalImpulse.LateralVelocity == 2 && !forward.ReverseActive,
            "A forward car cannot be thrown into powered reverse by a frontal collision impulse");
    }
    private static void CheckTrackAndCanonicalProgress(Action<bool, string> check)
    {
        var pattern = RaceCalibration.DefaultSprint.BuildPattern(); var track = RaceMath.AccumulateTrackX(pattern);
        check(pattern.Length == 222 && RaceCalibration.DefaultSprint.TotalSegments == 222 && RaceMath.TrackClosureError(pattern) == 0,
            "Released sprint keeps all 222 segments and authored zero net turn shift");
        var block = RaceMath.AccumulateTrackX(new[] { 2, 2, 0, 1, 1 });
        check(Near(block[0], 28) && Near(block[1], 56) && Near(block[2], 56) && Near(block[3], 28) && Near(block[4], 0),
            "Track turn shifts apply once per contiguous block, with original cosine easing");
        check(RaceMath.TrackClosureError(new[] { 2, 2, 0 }) == 56, "Invalid track closure is reported, never silently bent to close");
        var xs = new float[] { 0, 10, 30, 60, 100 };
        check(RaceMath.TrackCenterX(-2, xs) == 0 && RaceMath.TrackCenterX(8, xs) == 100 && Near(RaceMath.TrackCenterX(1.25f, xs), 14.0625f)
            && Near(RaceMath.TrackCenterSlope(2.5f, xs), 30) && RaceMath.TrackCenterSlope(4, xs) == 0,
            "Clamped Catmull-Rom centerline and endpoint slopes match Godot source samples");
        var world = RaceMath.TrackWorldPosition(42.25f, track, pattern.Length, 40);
        check(Near(RaceMath.NearestTrackProgress(world, track, pattern.Length, 40, 0), 42.25f, .002f),
            "Large collision displacement invalidates local progress hint and recovers through full-track scan");
        check(Near(RaceMath.SafeCornerSpeed(10, track, RaceCalibration.PlayerMaxSpeed, .84f, 12), 48.01759f, .003f),
            "Corner assist preserves original geometry and braking-distance limit");
        var route = new RaceRouteState(10); route.PlaceOnGrid(3);
        check(route.Progress == 0 && route.WorldZ == -21 && Near(route.PhysicalProgress, -.525f), "Start-grid separation is physical only, without negative canonical race progress");
        route.AdvanceSegments(10.25f);
        check(route.Lap == 1 && route.SegmentIndex == 0 && Near(route.SegmentProgress, .25f) && Near(route.WorldZ, 389), "Forward lap wrap preserves continuous canonical Z and grid offset");
        route.SetFromPhysicalWorldZ(-25); route.AdvanceSegments(.1f);
        check(route.Lap == 0 && route.SegmentIndex == 0 && Near(route.SegmentProgress, 0, .00001f) && Near(route.WorldZ, -21),
            "Contact corrections update canonical route state, so the next tick does not undo corrected world Z");
        route.AdvanceSegments(-.01f);
        check(route.Lap == -1 && route.SegmentIndex == 9 && Near(route.SegmentProgress, .99f), "Reverse travel wraps segment zero into the previous lap");
        check(RaceMath.FormatTime(-1) == "--:--.--" && RaceMath.FormatTime(125.679f) == "2:05.67", "Race clock retains original centisecond truncation");
        check(Near(RaceCalibration.RoadHeight(3120), 18) && Near(RaceCalibration.RoadHeight(3520), 20)
            && RaceCalibration.GroundHeight(6, 3520) == 0 && RaceCalibration.GroundHeight(0, 5500) == -10,
            "Bridge and tunnel height calibration preserves road/terrain separation");
        var bounds = RaceBarriers.Limits(3200, pattern, track);
        var center = RaceMath.TrackCenterX(3200 / 40f, track);
        check(Near(bounds.X - center, -4.65f) && Near(bounds.Y - center, 4.65f), "Physical bridge rail uses source clearance and centerline");
        var sweep = RaceBarriers.Sweep(new Vec2(center, 3200), new Vec2(center + 20, 3201), pattern, track, 0);
        check(sweep.Contact == 1 && sweep.WorldX <= bounds.Y + .001f, "Swept barrier prevents body penetration rather than clamping a later render offset");
    }
    private static void CheckProjection(Action<bool, string> check)
    {
        var viewport = new Vec2(800, 600);
        var lens = new RaceProjectionParameters(1, 1, 0, .1f, 300);
        var polygon = new[] { new RaceVec3(-1, -1, .2f), new RaceVec3(1, -1, 2), new RaceVec3(1, 1, 2), new RaceVec3(-1, 1, .2f) };
        var clipped = RaceProjection.ClipCamera(polygon, viewport, lens); var bounded = clipped.Length >= 3; var touchedNear = false;
        foreach (var p in clipped)
        {
            var v = lens.ProjectCameraPoint(p, viewport);
            bounded &= p.Z >= .79999f && Scalar.IsFinite(v.X) && Scalar.IsFinite(v.Y) && v.X >= -.001f && v.X <= 800.001f && v.Y >= -.001f && v.Y <= 600.001f;
            touchedNear |= Near(p.Z, .8f, .00001f);
        }
        check(bounded && touchedNear, "Camera-space clipping occurs before perspective divide and keeps the .8 near plane finite");
        check(RaceProjection.ClipCamera(new[] { new RaceVec3(-1, 0, -.2f), new RaceVec3(1, 0, -.2f), new RaceVec3(0, 1, -.2f) }, viewport, lens).Length == 0,
            "A polygon entirely behind the near plane produces no screen geometry");
        var roll = new RaceProjectionParameters(1, 1, 0, .1f, 300, 1, (float)Math.PI * .5f);
        var rolled = roll.ProjectCameraPoint(new RaceVec3(1, 0, 2), viewport);
        check(Near(rolled.X, 400) && Near(rolled.Y, 260), "Roll rotates the lens numerator around the horizon pivot");
        var ribbon = RaceProjection.Ribbon(new RaceVec3(-1, 0, 2), new RaceVec3(2, 0, 10), 1, viewport, lens, p => p);
        check(ribbon.Length == 4 && Near((ribbon[0] - ribbon[3]).Length, 40) && Near((ribbon[1] - ribbon[2]).Length, 8),
            "Roadside rail keeps independently projected endpoint thickness and tapers into distance");
        var source = new[] { new Vec2(-1, -1), new Vec2(3, -1), new Vec2(3, 3), new Vec2(-1, 3) };
        var textured = RaceProjection.ClipTexturedScreen(source, new[] { Vec2.Zero, new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1) }, new RaceRect(Vec2.Zero, new Vec2(2, 2)));
        var uvValid = textured.Points.Length == 4 && textured.Uv.Length == 4;
        for (var i = 0; i < textured.Points.Length; i++) uvValid &= Near(textured.Uv[i].X, (textured.Points[i].X + 1) / 4) && Near(textured.Uv[i].Y, (textured.Points[i].Y + 1) / 4);
        check(uvValid, "Screen clipping interpolates texture coordinates with the very same edge fractions");
    }
    private sealed class Body : IRaceContactBody
    {
        public Vec2 Position { get; set; }
        public float HeadingYaw { get; set; }
        public float InverseMass { get; set; } = 1;
        public Vec2 ContactVelocity { get; set; }
        public void ResolveContact(Vec2 correction) { Position += correction; }
        public void ApplyContactVelocity(Vec2 value) { ContactVelocity = value; }
    }
    private static void CheckContacts(Action<bool, string> check)
    {
        var a = new Body { Position = new Vec2(5, 0) }; var b = new Body { Position = Vec2.Zero };
        check(RaceVehicleContacts.Contact(a, b, new Vec2(-5, 0), Vec2.Zero).X < -6.8f,
            "Swept SAT catches bodies that crossed during a tick even when final rectangles no longer overlap");
        a.Position = Vec2.Zero; b.Position = new Vec2(1, 0); a.ContactVelocity = new Vec2(1, 0); b.ContactVelocity = Vec2.Zero;
        var touched = RaceVehicleContacts.Solve(new IRaceContactBody[] { a, b }, new[] { Vec2.Zero, new Vec2(1, 0) });
        check(touched.Count == 1 && Near((b.Position - a.Position).X, 1.825f) && Near(a.ContactVelocity.X, .5f) && Near(b.ContactVelocity.X, .5f),
            "Four-pass contact solver separates stopped bodies and exchanges only closing normal momentum");
    }
    private static void CheckRaceState(Action<bool, string> check)
    {
        var state = new RaceState(); state.Setup(3, 4); state.AcornsTotal = 7; state.RaceStarted = true;
        check(RaceQueries.CanControl(state) && state.AiProgress.Length == 3 && !RaceQueries.IsFinished(state), "Race setup creates opponents and starts controllable state without finishing the mission");
        state.MissionFailed = true;
        check(!RaceQueries.CanControl(state) && RaceQueries.IsFinished(state), "Failure removes control even without race-finished flag");
        state.ResetRace();
        check(state.TotalLaps == 3 && state.RacerCount == 4 && state.AcornsTotal == 7 && state.Countdown == 3 && state.BestLap == -1 && state.Hp == 100 && !state.RaceStarted,
            "Race reset clears runtime state while retaining source authored counts");
    }
}
