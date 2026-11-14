using System;
using System.Collections;
using Arkon.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arkon.Unity.Editor
{
    /// <summary>Native PlayMode checks using an in-memory scene and the production Level 3 adapters.</summary>
    [InitializeOnLoad]
    public static class TopdownGameplayChecks
    {
        private const string Active = "Arkon.TopdownChecks.Active";
        private const string Deadline = "Arkon.TopdownChecks.Deadline";
        private const string Passed = "Arkon.TopdownChecks.Passed";
        private const string Result = "Arkon.TopdownChecks.Result";
        private static IEnumerator exercise;
        private static double lastTick;
        private static float previousTimeScale;
        private static int checks;

        static TopdownGameplayChecks()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += Update;
        }

        // Batch use omits -quit so PlayMode runs before the Editor exits.
        [MenuItem("Arkon/Migration/Verify Native Top-down Gameplay")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Active, false))
                throw new InvalidOperationException("Start the top-down checks once from EditMode.");

            SessionState.SetBool(Active, true);
            SessionState.SetBool(Passed, false);
            SessionState.SetString(Result, "PlayMode exited before the top-down checks completed.");
            SetDeadline(120);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(Active, false)) return;

            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                SetDeadline(30);
                checks = 0;
                lastTick = Time.fixedTimeAsDouble;
                previousTimeScale = Time.timeScale;
                Time.timeScale = 1;
                exercise = Exercise();
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                exercise = null;
                var passed = SessionState.GetBool(Passed, false);
                var result = SessionState.GetString(Result, "Missing top-down check result.");
                SessionState.SetBool(Active, false);
                if (passed) Debug.Log(result); else Debug.LogError(result);
                if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
            }
        }

        private static void SetDeadline(float seconds) =>
            SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + seconds);

        private static void Update()
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Deadline, float.PositiveInfinity))
            {
                Finish(false, "Native top-down checks timed out.");
                return;
            }

            if (exercise == null || !EditorApplication.isPlaying) return;
            // Wait for real PlayMode physics to advance before reading adapter state.
            var tick = Time.fixedTimeAsDouble;
            if (tick <= lastTick) return;
            lastTick = tick;

            try
            {
                if (!exercise.MoveNext()) Finish(true, $"Native top-down gameplay passed {checks} assertions.");
            }
            catch (Exception error) { Finish(false, error.ToString()); }
        }

        private static void Finish(bool passed, string message)
        {
            if (exercise != null) Time.timeScale = previousTimeScale;
            exercise = null;
            SessionState.SetBool(Passed, passed);
            SessionState.SetString(Result, message);
            SessionState.EraseFloat(Deadline);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else OnPlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private static GameObject CreateRoot(Scene scene, string name, Vec2 position)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = ArkonCoordinates.ToUnity(position);
            return root;
        }

        private static void PlaceActor(TopdownActor2D actor, Vec2 position)
        {
            actor.FeedInput(Vec2.Zero, Vec2.Zero, false, false, false, false);
            actor.Motor.ClearControls();
            actor.GetComponent<Rigidbody2D>().position = ArkonCoordinates.ToUnity(position);
            Physics2D.SyncTransforms();
        }

        private static IEnumerator Exercise()
        {
            Require(Mathf.Abs(Time.fixedDeltaTime - 1f / 60) < .000001f, "Expected the source 60 Hz fixed timestep.");
            Require(Physics2D.simulationMode == SimulationMode2D.FixedUpdate, "Expected native FixedUpdate simulation.");

            var scene = SceneManager.CreateScene("Arkon top-down gameplay checks");
            var worldRoot = CreateRoot(scene, "Scene world", Vec2.Zero);
            var world = worldRoot.AddComponent<TopdownWorld2D>();
            worldRoot.SetActive(true);

            var approach = new Vec2(0, 28);
            var actorRoot = CreateRoot(scene, "Actor", approach);
            var actor = actorRoot.AddComponent<TopdownActor2D>();
            actor.ReadKeyboardMouse = false;
            actor.InfiniteAmmo = false;
            actor.AssignedWorld = world.GetComponent<Physics2DPort>();
            actorRoot.SetActive(true);
            world.BindTarget(actor);

            var doorRoot = CreateRoot(scene, "Door", Vec2.Zero);
            var door = doorRoot.AddComponent<DoorPort2D>();
            door.AssignedWorld = world;
            doorRoot.SetActive(true);
            Physics2D.SyncTransforms();
            yield return null;

            var contactSpeed = 0f;
            var opened = 0;
            var slammed = 0;
            var closed = 0;
            door.Opened += _ => opened++;
            door.Slammed += _ => slammed++;
            door.Closed += _ => closed++;
            actor.GetComponent<KinematicBodyPort2D>().BlockingCollisions += (contacts, speed) =>
            {
                foreach (var collider in contacts)
                    if (collider == door.SolidCollider) contactSpeed = Mathf.Max(contactSpeed, speed);
            };

            var towardDoor = new Vec2(0, -1);
            const float belowThreshold = 83.5f;
            actor.FeedInput(towardDoor * (belowThreshold / 120), towardDoor, false, false, false, false);
            var deadline = Time.fixedTimeAsDouble + 1;
            while (contactSpeed == 0 && Time.fixedTimeAsDouble < deadline) yield return null;
            Require(Mathf.Abs(contactSpeed - belowThreshold) < .001f, $"Expected a real door contact at 83.5 px/s, got {contactSpeed}.");
            Require(slammed == 0 && door.SolidCollider.enabled && actor.Position.Y > 9,
                "The closed door must stop the actor without slamming below 84 px/s.");

            PlaceActor(actor, approach);
            contactSpeed = 0;
            actor.FeedInput(towardDoor * .70f, towardDoor, false, false, false, false);
            deadline = Time.fixedTimeAsDouble + 1;
            while (slammed == 0 && Time.fixedTimeAsDouble < deadline) yield return null;
            Require(Mathf.Abs(contactSpeed - 84) < .001f, $"Expected a real door contact at 84 px/s, got {contactSpeed}.");
            Require(slammed == 1 && !door.SolidCollider.enabled, "The threshold contact must slam the door once and disable solidity.");

            PlaceActor(actor, Vec2.Zero);
            deadline = Time.fixedTimeAsDouble + 4;
            while (opened < 2 && Time.fixedTimeAsDouble < deadline) yield return null;
            Require(opened == 2 && closed == 0 && door.Model.IsOpen && !door.SolidCollider.enabled,
                "The native overlap at the authored closed pose must reopen the occupied door.");
            Require(Vec2.Distance(actor.Position, Vec2.Zero) < .01f, "The actor moved out of the doorway during the occupied test.");

            var pickupPosition = new Vec2(0, 100);
            PlaceActor(actor, pickupPosition);
            deadline = Time.fixedTimeAsDouble + 4;
            while (closed == 0 && Time.fixedTimeAsDouble < deadline) yield return null;
            Require(closed == 1 && door.SolidCollider.enabled, "The unobstructed door must close and restore its native collider.");
            Debug.Log("Native top-down door contact, slam threshold, and occupancy checks passed.");

            var pickupRoot = CreateRoot(scene, "Ammo pickup above the standing actor", pickupPosition);
            var pickup = pickupRoot.AddComponent<PickupPort2D>();
            pickup.AssignedWorld = world;
            pickup.Kind = "ammo";
            var collected = 0;
            var cooldownAtCollection = 0f;
            pickup.Collected += (_, recipient) =>
            {
                if (recipient != actor) return;
                collected++;
                cooldownAtCollection = actor.Weapons.Cooldown;
            };
            pickupRoot.SetActive(true);
            Physics2D.SyncTransforms();
            var model = pickup.Model;
            var receiver = actor.GetComponent<DamageReceiver2D>();

            // Give both trigger entry and periodic overlap checks time to see a full-health actor.
            deadline = Time.fixedTimeAsDouble + .25;
            while (Time.fixedTimeAsDouble < deadline) yield return null;
            Require(model.Kind == "medkit" && !model.Consumed && collected == 0 && receiver.Health.Current == 100,
                "The ammo source pickup must stay available under a full-health actor.");
            Require(actor.Weapons.Equip(WeaponCatalog.ShotgunId, 6) && actor.Weapons.ConsumeShot(),
                "Could not set a weapon cooldown and finite ammo for the pickup test.");
            var ammo = actor.Weapons.Ammo;
            Require(receiver.ApplyDamage(50, 0), "The standing actor did not take the test damage.");

            deadline = Time.fixedTimeAsDouble + .25;
            while (collected == 0 && Time.fixedTimeAsDouble < deadline) yield return null;
            yield return null;
            Require(model.Consumed && collected == 1 && receiver.Health.Current == 85,
                "The medkit overlap retry must heal 35 exactly once without a new trigger entry.");
            Require(Vec2.Distance(actor.Position, pickupPosition) < .01f, "The actor moved during the underfoot pickup test.");
            Require(actor.Weapons.CurrentId == WeaponCatalog.ShotgunId && actor.Weapons.Ammo == ammo && cooldownAtCollection > 0,
                "Medkit collection changed the actor's weapon, ammo, or cooldown.");
            Debug.Log("Native top-down stationary medkit overlap and weapon preservation checks passed.");

            var unload = SceneManager.UnloadSceneAsync(scene);
            if (unload != null)
                while (!unload.isDone) yield return null;
        }
    }
}
