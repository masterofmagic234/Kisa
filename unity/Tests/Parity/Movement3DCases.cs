using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Arkon.Core;

public static class Movement3DCases
{
    private static bool Near(float a, float b, float tolerance = .00001f) => Math.Abs(a - b) <= tolerance;
    private static bool Near(MotionVector3 a, MotionVector3 b, float tolerance = .00001f) => (a - b).Length <= tolerance;
    private static MotionVector3 Vector(JsonElement value) => new MotionVector3(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());

    public static void Run(Action<bool, string> check)
    {
        var source = Environment.GetEnvironmentVariable("ARKON_GODOT_SOURCE") ?? Path.Combine(Directory.GetCurrentDirectory(), "white_guard");
        var oracle = Environment.GetEnvironmentVariable("ARKON_MOVEMENT3D_ORACLE") ?? Path.GetFullPath(Path.Combine(source, "..", "unity", "Tests", "Fixtures", "godot168-movement3d.json"));
        using (var document = JsonDocument.Parse(File.ReadAllText(oracle)))
        {
            var values = document.RootElement;
            foreach (var entry in values.GetProperty("source_files").EnumerateObject())
            {
                var path = Path.Combine(source, entry.Name);
                string actual;
                using (var algorithm = SHA256.Create()) actual = BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
                check(actual == entry.Value.GetString(), "3D movement oracle source SHA256 matches " + entry.Name);
            }
            CheckMovement(values, check);
            CheckRecordedStairs(values, check);
        }
        CheckStairConditions(check);
    }
    private static void CheckMovement(JsonElement values, Action<bool, string> check)
    {
        var x = new MotionVector3(1, 0, 0); var z = new MotionVector3(0, 0, 1);
        check(Near(FirstPersonMovementMath.VelocityForInput(x, z, new Vec2(0, -1), 4), Vector(values.GetProperty("forward"))),
            "3D forward motion matches the real public Godot helper and keeps -Z as source forward");
        check(Near(FirstPersonMovementMath.VelocityForInput(x, z, new Vec2(1, -1).Normalized, 4), Vector(values.GetProperty("diagonal"))),
            "Normalized source input keeps diagonal walking at four metres per second");
        var yaw = (float)Math.PI * .5f;
        var basisX = new MotionVector3((float)Math.Cos(yaw), 0, -(float)Math.Sin(yaw));
        var basisZ = new MotionVector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw));
        check(Near(FirstPersonMovementMath.VelocityForInput(basisX, basisZ, new Vec2(0, -1), 4), Vector(values.GetProperty("yaw90")), .000001f),
            "Yawed basis movement matches the Godot oracle without silently adopting Unity's opposite forward convention");
        var tiltedZ = new MotionVector3(0, -(float)Math.Sin(.5), (float)Math.Cos(.5));
        check(Near(FirstPersonMovementMath.VelocityForInput(x, tiltedZ, new Vec2(0, -1), 4), Vector(values.GetProperty("tilted"))),
            "Flattening a tilted basis does not renormalize lost vertical speed, matching source helper behavior");
        var motor = new FirstPersonMotor();
        check(!motor.ApplyLookPixels(new Vec2(100, -200), false) && motor.Yaw == 0, "Uncaptured pointer cannot rotate the FPS actor");
        check(motor.ApplyLookPixels(new Vec2(100, -200), true) && Near(motor.Yaw, -.28f) && Near(motor.Pitch, .56f),
            "FPS look retains .0028 radians per source pixel and the original pitch direction");
        motor.ApplyLookPixels(new Vec2(0, -10000), true);
        check(motor.Pitch == 1.4f, "FPS pitch clamps at the original 1.4-radian upper limit");
        motor.SetOrientation(0, 0); motor.PrepareMovement(new Vec2(0, -1), true, 1f / 60);
        check(Near(motor.Velocity, new MotionVector3(0, -.5f, -4)), "Grounded FPS motion applies the original half-metre-per-second downward contact pull");
        motor.SetVerticalVelocity(0); motor.PrepareMovement(new Vec2(0, -1), false, .1f);
        check(Near(motor.Velocity.Y, -1.8f), "Airborne FPS gravity integrates eighteen metres per second squared");
        var before = motor.Velocity; motor.TransitionBlocked = true;
        check(!motor.PrepareMovement(Vec2.Zero, true, .1f) && Near(motor.Velocity, before), "Gameplay transition blocks movement without consuming the current velocity");
        motor.TransitionBlocked = false; motor.Dead = true;
        check(!motor.PrepareMovement(Vec2.Zero, true, .1f) && !motor.ApplyLookPixels(new Vec2(1, 1), true), "Death blocks both FPS locomotion and pointer look");
    }
    private sealed class RecordedSweep : IBodySweep3D
    {
        private readonly JsonElement records;
        public int Calls;
        public bool RequestsMatch = true;
        public RecordedSweep(JsonElement records) { this.records = records; }
        public MotionProbe3D TestMotion(MotionVector3 from, MotionVector3 motion, float safeMargin, int maximumCollisions)
        {
            if (Calls >= records.GetArrayLength()) throw new InvalidOperationException("Step planner requested an unrecorded source query.");
            var record = records[Calls++];
            RequestsMatch &= Near(from, Vector(record.GetProperty("from"))) && Near(motion, Vector(record.GetProperty("motion")))
                && Near(safeMargin, .001f) && maximumCollisions == 4;
            var normals = new List<MotionVector3>();
            foreach (var normal in record.GetProperty("normals").EnumerateArray()) normals.Add(Vector(normal));
            return new MotionProbe3D(record.GetProperty("collided").GetBoolean(), Vector(record.GetProperty("travel")), normals.ToArray());
        }
    }
    private static void CheckRecordedStairs(JsonElement values, Action<bool, string> check)
    {
        foreach (var name in new[] { "low_step", "high_step" })
        {
            var recorded = values.GetProperty(name); var body = new RecordedSweep(recorded.GetProperty("queries"));
            var accepted = GroundedStepMotion3D.TryPlan(body, Vector(recorded.GetProperty("origin")), new MotionVector3(1, 0, 0),
                MotionVector3.Up, recorded.GetProperty("grounded").GetBoolean(), -.5f, 52 * (float)Math.PI / 180, .001f, out var plan);
            check(accepted == recorded.GetProperty("stepped").GetBoolean() && body.Calls == 4 && body.RequestsMatch,
                "Stair policy replays the real Godot capsule-query sequence, origins and normals for " + name);
            check(accepted && Near(plan.Rise(MotionVector3.Up), recorded.GetProperty("predicted_rise").GetSingle())
                && plan.Rise(MotionVector3.Up) >= .002f && plan.Rise(MotionVector3.Up) <= .201f,
                "Recorded stair rise stays in the source acceptance range for " + name);
        }
        // A .25m riser can admit a PARTIAL step due to capsule curvature. The
        // source .20 constant limits accepted rise, not the total obstacle height.
        check(values.GetProperty("high_step").GetProperty("displacement")[0].GetSingle() < .5f,
            "Native Godot oracle distinguishes partial contact recovery from a full-height stair climb");
    }
    private sealed class AnalyticStepWorld : IBodySweep3D
    {
        public float StepHeight = .15f, Edge = .25f, Ceiling = float.PositiveInfinity;
        public bool NoLanding, SteepLanding, FloorOnly;
        public int Calls;
        public MotionProbe3D TestMotion(MotionVector3 from, MotionVector3 motion, float margin, int max)
        {
            Calls++;
            if (motion.Y > 0 && from.Y + motion.Y > Ceiling)
                return new MotionProbe3D(true, new MotionVector3(0, Math.Max(0, Ceiling - from.Y), 0), -MotionVector3.Up);
            if (motion.X > 0)
            {
                if (FloorOnly) return new MotionProbe3D(true, motion, MotionVector3.Up);
                if (from.X < Edge && from.X + motion.X >= Edge && from.Y < StepHeight)
                    return new MotionProbe3D(true, new MotionVector3(Edge - from.X, 0, 0), new MotionVector3(-1, 0, 0));
            }
            if (motion.Y < 0 && !NoLanding)
            {
                var floor = from.X >= Edge ? StepHeight : 0;
                if (from.Y >= floor && from.Y + motion.Y <= floor)
                    return new MotionProbe3D(true, new MotionVector3(0, floor - from.Y, 0), SteepLanding ? new MotionVector3(.9f, .3f, 0).Normalized : MotionVector3.Up);
            }
            return new MotionProbe3D(false, motion);
        }
    }
    private static bool Plan(AnalyticStepWorld world, out StepPlan3D plan, bool grounded = true, float vertical = -.5f)
        => GroundedStepMotion3D.TryPlan(world, MotionVector3.Zero, new MotionVector3(1, 0, 0), MotionVector3.Up,
            grounded, vertical, 52 * (float)Math.PI / 180, .001f, out plan);
    private static void CheckStairConditions(Action<bool, string> check)
    {
        var low = new AnalyticStepWorld();
        check(Plan(low, out var plan) && Near(plan.Rise(MotionVector3.Up), .15f) && Near(plan.Forward.X, 1),
            "Clear up/forward/down sweeps find a real .15m landing without assigning the actor's transform");
        check(!Plan(new AnalyticStepWorld { Ceiling = .1f }, out _), "A low ceiling rejects the stair before any movement executes");
        check(!Plan(new AnalyticStepWorld { StepHeight = .25f }, out _), "A blocking point-body riser with no extra forward progress cannot be accepted as a step");
        check(!Plan(new AnalyticStepWorld { NoLanding = true }, out _), "A void beyond an obstacle is not a stair landing");
        check(!Plan(new AnalyticStepWorld { SteepLanding = true }, out _), "A landing steeper than the original floor angle is rejected");
        check(!Plan(new AnalyticStepWorld { StepHeight = .001f }, out _), "Sub-two-millimetre contact jitter is not promoted to stair motion");
        check(!Plan(new AnalyticStepWorld { FloorOnly = true }, out _), "A walkable floor contact alone is not a blocking wall");
        var airborne = new AnalyticStepWorld();
        check(!Plan(airborne, out _, false) && airborne.Calls == 0, "Airborne actor cannot trigger stair sweeps");
        check(!Plan(airborne, out _, true, .1f) && airborne.Calls == 0, "Ascending actor cannot trigger grounded stair sweeps");
    }
}
