using System;
using System.Linq;
using Arkon.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arkon.Unity.Editor
{
    [InitializeOnLoad]
    public static class FirstPersonPhysicsChecks
    {
        private const string Pending = "Arkon.PhysX.Pending";
        private const string Passed = "Arkon.PhysX.Passed";
        private const string Deadline = "Arkon.PhysX.Deadline";
        private const float FixedStep = 1f / 60;
        private const float SafeMargin = .001f;
        private const float FloorAngle = 52;
        private const float CapsuleHeight = 1.8f;
        private const float CapsuleRadius = .25f;
        private static readonly MotionVector3 ForwardSweep = new MotionVector3(1, 0, 0);
        private static bool ready;

        static FirstPersonPhysicsChecks()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += CheckDeadline;
        }

        [MenuItem("Arkon/Migration/Verify Native FPS Physics")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Pending, false))
                throw new InvalidOperationException("Start the PhysX checks once from EditMode.");

            SessionState.SetBool(Pending, true);
            SessionState.SetBool(Passed, false);
            SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 120);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) ready = true;
            if (state != PlayModeStateChange.EnteredEditMode) return;

            SessionState.SetBool(Pending, false);
            var passed = SessionState.GetBool(Passed, false);
            if (passed) Debug.Log("Native PhysX checks passed (floor, airborne motion, ceiling and stair fixtures).");
            else Debug.LogError("Native PhysX checks failed or exited before completion.");
            if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        }

        private static void CheckDeadline()
        {
            if (!SessionState.GetBool(Pending, false)) return;
            if (ready)
            {
                ready = false;
                Verify();
                return;
            }
            if (EditorApplication.timeSinceStartup <= SessionState.GetFloat(Deadline, 0)) return;

            Debug.LogError("Native PhysX checks timed out.");
            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else EditorApplication.isPlaying = false;
        }

        private static void Verify()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Pending, false)) return;
            try
            {
                // CapsuleBodySweep3D uses Physics.CapsuleCastAll in the default physics scene.
                // Separate distant fixtures keep their colliders out of each other's queries.
                var scene = SceneManager.CreateScene("Arkon native FPS physics fixtures");
                VerifyFloor(scene, new Vector3(64, 0, 64));
                VerifyCeiling(scene, new Vector3(96, 0, 64));
                VerifyStair(scene, new Vector3(128, 0, 64), .15f, 1f, .1539461f);
                VerifyStair(scene, new Vector3(160, 0, 64), .25f, .4453125f, .199907f);
                SessionState.SetBool(Passed, true);
            }
            catch (Exception error) { Debug.LogException(error); }
            finally { EditorApplication.isPlaying = false; }
        }

        private static void VerifyFloor(Scene scene, Vector3 offset)
        {
            AddFloor(scene, offset);
            var body = AddCapsule(scene, offset + Vector3.up * 1.4f).Controller;
            var motor = new FirstPersonMotor();
            Require(!body.isGrounded, "An elevated capsule was grounded before moving.");
            var landingTick = 0;
            for (var tick = 1; tick <= 30; tick++)
            {
                motor.PrepareMovement(Vec2.Zero, body.isGrounded, FixedStep);
                var flags = body.Move(CapsuleBodySweep3D.ToUnity(motor.Velocity * FixedStep));
                if (tick == 1)
                    Require(Mathf.Abs(motor.Velocity.Y + .3f) < .001f && (flags & CollisionFlags.Below) == 0,
                        "Airborne fall did not start at 18 m/s² with no floor contact.");
                if ((flags & CollisionFlags.Below) == 0) continue;
                landingTick = tick;
                motor.SetVerticalVelocity(0);
                break;
            }

            Require(landingTick >= 12 && landingTick <= 18 && body.isGrounded,
                $"A .50 m fall did not settle on the native floor at the expected time (tick {landingTick}).");
            Require(Mathf.Abs(body.transform.position.y - CapsuleHeight * .5f) < .015f && motor.Velocity.Y == 0,
                "Native floor contact left the capsule above or inside the floor, or retained downward velocity.");
            var start = body.transform.position;
            motor.PrepareMovement(new Vec2(0, -1), true, FixedStep);
            var walk = body.Move(CapsuleBodySweep3D.ToUnity(motor.Velocity * FixedStep));
            var displacement = body.transform.position - start;
            Require((walk & CollisionFlags.Below) != 0 && body.isGrounded && Mathf.Abs(displacement.y) < .015f &&
                Mathf.Abs(displacement.z - 4 * FixedStep) < .001f,
                $"One grounded walking tick did not move along Unity +Z at 4 m/s ({displacement}).");
            Debug.Log($"PhysX floor: landed at tick {landingTick}, body height {body.transform.position.y:F6} m, walking delta {displacement:F6} m.");
        }

        private static void VerifyCeiling(Scene scene, Vector3 offset)
        {
            var sweeps = AddStair(scene, offset, .15f);
            var body = sweeps.Controller;
            AddBox(scene, "Ceiling", offset + new Vector3(-.6f, 2f, 0), new Vector3(1, .2f, 2));
            Physics.SyncTransforms();
            var origin = CapsuleBodySweep3D.ToSource(body.transform.position);
            var ceiling = sweeps.TestMotion(origin, MotionVector3.Up * GroundedStepMotion3D.Height, SafeMargin, 4);
            Require(ceiling.Collided && ceiling.Travel.Y < .12f && ceiling.Normals.Any(normal => normal.Y < -.99f),
                "Upward capsule cast missed the ceiling 0.10 m above the player.");
            Require(!TryStep(sweeps, origin, body.isGrounded, out _), "The stair planner accepted a step through a low ceiling.");
            var above = body.Move(Vector3.up * GroundedStepMotion3D.Height);
            Require((above & CollisionFlags.Above) != 0 && body.transform.position.y + CapsuleHeight * .5f < 1.915f,
                "Native CharacterController penetrated the low ceiling.");
            Debug.Log($"PhysX ceiling: upward query travel {ceiling.Travel.Y:F6} m before native ceiling contact.");
        }

        private static void VerifyStair(Scene scene, Vector3 offset, float height, float sourceX, float sourceY)
        {
            var sweeps = AddStair(scene, offset, height);
            var body = sweeps.Controller;
            var start = body.transform.position;
            var origin = CapsuleBodySweep3D.ToSource(start);
            var ordinary = sweeps.TestMotion(origin, ForwardSweep, SafeMargin, 4);
            Require(ordinary.Collided && ordinary.Travel.X < .6f && ordinary.Normals.Any(normal => normal.Y < Mathf.Cos(FloorAngle * Mathf.Deg2Rad)),
                $"Horizontal capsule cast did not find the {height:F2} m riser.");
            var accepted = TryStep(sweeps, origin, body.isGrounded, out var step);
            if (accepted)
            {
                Require(step.Forward.X >= ordinary.Travel.X + .0005f &&
                    step.Rise(MotionVector3.Up) >= GroundedStepMotion3D.MinimumRise &&
                    step.Rise(MotionVector3.Up) <= GroundedStepMotion3D.MaximumRise,
                    $"Accepted {height:F2} m stair violated forward-progress or rise bounds.");
                body.Move(CapsuleBodySweep3D.ToUnity(step.Up));
                body.Move(CapsuleBodySweep3D.ToUnity(step.Forward));
                body.Move(CapsuleBodySweep3D.ToUnity(step.Down));
                body.Move(Vector3.up * (FirstPersonMotor.GroundedPull * FixedStep));
            }
            else
            {
                body.Move(CapsuleBodySweep3D.ToUnity(ForwardSweep + MotionVector3.Up * (FirstPersonMotor.GroundedPull * FixedStep)));
            }

            var moved = body.transform.position - start;
            Debug.Log($"PhysX stair {height:F2} m: step accepted={accepted}, ordinary cast X={ordinary.Travel.X:F6}, " +
                $"planned X={step.Forward.X:F6}, Y={step.Rise(MotionVector3.Up):F6}, native X={moved.x:F6}, Y={moved.y:F6}, " +
                $"delta from Godot X={moved.x - sourceX:F6}, Y={moved.y - sourceY:F6} m.");
            if (height < GroundedStepMotion3D.Height)
                Require(accepted && moved.x > .98f && moved.x < 1.02f && Mathf.Abs(moved.y - height) < .02f && body.isGrounded,
                    "The low stair did not land the capsule on the upper floor after one horizontal move.");
            else
                // The rounded Godot capsule accepted a partial step. Log any native difference.
                Require(moved.x >= 0 && moved.x < .61f && moved.y < .22f,
                    "A single test move let the capsule fully mount the .25 m riser.");
        }

        private static bool TryStep(CapsuleBodySweep3D sweeps, MotionVector3 origin, bool grounded, out StepPlan3D plan) =>
            GroundedStepMotion3D.TryPlan(sweeps, origin, ForwardSweep, MotionVector3.Up, grounded,
                FirstPersonMotor.GroundedPull, FloorAngle * Mathf.Deg2Rad, SafeMargin, out plan);

        private static CapsuleBodySweep3D AddStair(Scene scene, Vector3 offset, float height)
        {
            AddFloor(scene, offset);
            AddBox(scene, "Riser", offset + new Vector3(1, height * .5f, 0), new Vector3(2, height, 2));
            var sweeps = AddCapsule(scene, offset + new Vector3(-.6f, .9f, 0));
            sweeps.Controller.Move(Vector3.up * (FirstPersonMotor.GroundedPull * FixedStep));
            Require(sweeps.Controller.isGrounded, "The staircase fixture did not begin grounded.");
            return sweeps;
        }

        private static CapsuleBodySweep3D AddCapsule(Scene scene, Vector3 position)
        {
            var actor = new GameObject("Native FPS capsule");
            SceneManager.MoveGameObjectToScene(actor, scene);
            actor.transform.position = position;
            var body = actor.AddComponent<CharacterController>();
            body.height = CapsuleHeight;
            body.radius = CapsuleRadius;
            body.center = Vector3.zero;
            body.stepOffset = 0;
            body.skinWidth = SafeMargin;
            body.minMoveDistance = 0;
            body.slopeLimit = FloorAngle;
            var sweeps = actor.AddComponent<CapsuleBodySweep3D>();
            Physics.SyncTransforms();
            return sweeps;
        }

        private static void AddFloor(Scene scene, Vector3 offset) =>
            AddBox(scene, "Floor", offset + new Vector3(0, -.1f, 0), new Vector3(10, .2f, 10));

        private static void AddBox(Scene scene, string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            SceneManager.MoveGameObjectToScene(box, scene);
            box.transform.position = position;
            box.AddComponent<BoxCollider>().size = size;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
