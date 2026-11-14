using System;
using System.Collections;
using Arkon.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arkon.Unity.Editor
{
    [InitializeOnLoad]
    public static class TopdownWorldChecks
    {
        private const string Active = "Arkon.TopdownWorldChecks.Active";
        private const string Deadline = "Arkon.TopdownWorldChecks.Deadline";
        private const string Passed = "Arkon.TopdownWorldChecks.Passed";
        private const string Result = "Arkon.TopdownWorldChecks.Result";
        private static IEnumerator exercise;
        private static float previousTimeScale;
        private static int checks;

        static TopdownWorldChecks()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Update;
        }

        [MenuItem("Arkon/Migration/Verify Native Top-down Worlds")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Active, false))
                throw new InvalidOperationException("Start the top-down world checks once from EditMode.");

            SessionState.SetBool(Active, true);
            SessionState.SetBool(Passed, false);
            SessionState.SetString(Result, "PlayMode exited before the top-down world checks completed.");
            SetDeadline(120);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SetDeadline(30);
                checks = 0;
                previousTimeScale = Time.timeScale;
                Time.timeScale = 1;
                exercise = Verify();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && exercise != null)
            {
                Time.timeScale = previousTimeScale;
                exercise = null;
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.SetBool(Active, false);
                var passed = SessionState.GetBool(Passed, false);
                var result = SessionState.GetString(Result, "Top-down world checks did not record a result.");
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
                Complete(false, "Native top-down world checks timed out.");
                return;
            }
            if (!EditorApplication.isPlaying || exercise == null) return;
            try
            {
                if (!exercise.MoveNext()) Complete(true, $"Native top-down world checks passed {checks} assertions.");
            }
            catch (Exception error) { Complete(false, error.ToString()); }
        }

        private static void Complete(bool passed, string message)
        {
            SessionState.SetBool(Passed, passed);
            SessionState.SetString(Result, message);
            SessionState.EraseFloat(Deadline);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            else OnPlayModeChanged(PlayModeStateChange.EnteredEditMode);
        }

        private static IEnumerator Verify()
        {
            Require(Mathf.Abs(Time.fixedDeltaTime - 1f / 60) < .000001f && Physics2D.simulationMode == SimulationMode2D.FixedUpdate,
                "Expected native 2D FixedUpdate simulation at the source 60 Hz timestep.");
            var sceneA = SceneManager.CreateScene("Arkon top-down world A");
            var sceneB = SceneManager.CreateScene("Arkon top-down world B");
            Require(sceneA.GetPhysicsScene2D().Equals(Physics2D.defaultPhysicsScene) &&
                sceneB.GetPhysicsScene2D().Equals(Physics2D.defaultPhysicsScene), "The fixtures must share the default native physics scene.");

            var from = new Vec2(66, 66);
            var occluded = new Vec2(198, 66);
            var exposed = new Vec2(198, 162);
            var worldA = CreateWorld(sceneA, occluded);
            var worldB = CreateWorld(sceneB, new Vec2(132, 114));
            var actorA = worldA.Target;
            var actorB = worldB.Target;
            AddWall(sceneA, "Corner", new Vec2(138, 78), new Vec2(12, 48));
            var oldWallCell = new GridCell(16, 14);
            var newWallCell = new GridCell(20, 14);
            var movedWall = AddWall(sceneA, "Wall moved during restart", worldA.Navigation.CellCenter(oldWallCell), new Vec2(12, 12));
            Physics2D.SyncTransforms();
            while (!worldA.NavigationReady || !worldB.NavigationReady) yield return null;

            Require(worldA.Target == actorA && worldB.Target == actorB && worldA.TargetAlive && worldB.TargetAlive,
                "Starting two worlds did not retain their own active actor bindings.");
            Require(worldA.Navigation.IsSolid(oldWallCell) && !worldB.Navigation.IsSolid(oldWallCell) && !worldA.Navigation.IsSolid(newWallCell),
                "The initial navigation grids did not distinguish the wall in world A from open world B.");
            Require(!worldA.HasLineOfSight(from, occluded) && worldB.HasLineOfSight(from, occluded) &&
                !worldA.IsBodyPathClear(from, occluded) && worldB.IsBodyPathClear(from, occluded),
                "A native ray or circle sweep did not filter colliders by scene.");
            Require(worldA.Navigation.FindPath(from, occluded).Count > worldB.Navigation.FindPath(from, occluded).Count,
                "World A did not plan a longer route around its wall than clear world B.");

            worldA.RowsPerSlice = 1;
            worldA.BuildNavigation(worldA.WorldSizePixels);
            while (worldA.Navigation.Samples == 0) yield return null;
            Require(!worldA.NavigationReady, "The replacement grid finished before the mid-build cancellation could be exercised.");
            var cancelledGeneration = worldA.Navigation.Generation;
            var cancelledSamples = worldA.Navigation.Samples;
            var unaffectedGeneration = worldB.Navigation.Generation;
            worldA.enabled = false;
            movedWall.transform.position = ArkonCoordinates.ToUnity(worldA.Navigation.CellCenter(newWallCell));
            var checkAfter = Time.fixedTimeAsDouble + .10;
            while (Time.fixedTimeAsDouble < checkAfter) yield return null;
            Require(!worldA.NavigationReady && !worldA.TargetAlive && !worldA.GetComponent<ProjectilePool2D>().isActiveAndEnabled &&
                worldA.Navigation.Generation == cancelledGeneration && worldA.Navigation.Samples == cancelledSamples,
                "Disabling world A failed to suspend sampling, retire its target binding, or disable its pool.");
            Require(worldB.NavigationReady && worldB.Navigation.Generation == unaffectedGeneration && worldB.TargetAlive,
                "Disabling world A changed world B's active grid or actor binding.");

            worldA.RowsPerSlice = 24;
            worldA.enabled = true;
            Require(!worldA.NavigationReady && worldA.TargetAlive && worldA.GetComponent<ProjectilePool2D>().isActiveAndEnabled &&
                worldA.Navigation.Generation > cancelledGeneration && worldA.Navigation.Samples == 0,
                "Re-enabling world A failed to bind its actor and restart navigation from an empty generation.");
            while (!worldA.NavigationReady) yield return null;
            Require(!worldA.Navigation.IsSolid(oldWallCell) && worldA.Navigation.IsSolid(newWallCell) && !worldB.Navigation.IsSolid(newWallCell) &&
                worldA.Navigation.Samples == worldA.Navigation.Width * worldA.Navigation.Height,
                "The rebuilt grid failed to reflect the moved collider exactly once in its own scene.");
            Debug.Log($"Physics2D navigation: cancelled after {cancelledSamples} samples and rebuilt {worldA.Navigation.Samples} samples in world A.");

            var enemyRoot = CreateRoot(sceneA, "Gunman", from);
            var enemy = enemyRoot.AddComponent<EnemyPort2D>();
            enemy.AssignedWorld = worldA;
            enemy.PatrolRadiusPixels = 0;
            var shots = 0;
            worldA.CombatFeedback += (kind, _) => { if (kind == "enemy_shot") shots++; };
            enemyRoot.SetActive(true);
            checkAfter = Time.fixedTimeAsDouble + .55;
            while (Time.fixedTimeAsDouble < checkAfter) yield return null;
            Require(enemy.Model != null && enemy.Model.State == EnemyState.Idle && shots == 0,
                "The gunman acquired or fired at a target behind its native corner wall.");

            actorA.GetComponent<Rigidbody2D>().position = ArkonCoordinates.ToUnity(exposed);
            Physics2D.SyncTransforms();
            var exposedAt = Time.fixedTimeAsDouble;
            while (Time.fixedTimeAsDouble <= exposedAt) yield return null;
            Require(worldA.HasLineOfSight(enemy.Position, actorA.Position), "The exposed actor is still occluded by the corner.");
            var portA = worldA.GetComponent<Physics2DPort>();
            var portB = worldB.GetComponent<Physics2DPort>();
            Require(portA.Sweep(from, exposed, SourceCollisionMasks.PlayerHitboxes, 0, out var hitA) &&
                hitA.ActorId == actorA.GetComponent<ActorIdentity2D>().Id &&
                portB.Sweep(from, exposed, SourceCollisionMasks.PlayerHitboxes, 0, out var hitB) &&
                hitB.ActorId == actorB.GetComponent<ActorIdentity2D>().Id && Vec2.Distance(from, hitB.Position) < Vec2.Distance(from, hitA.Position),
                "The native line through both actors failed to hit each world's own hitbox, with world B's closer on that line.");

            checkAfter = exposedAt + .25;
            while (Time.fixedTimeAsDouble < checkAfter) yield return null;
            Require(enemy.Model.State == EnemyState.Alert && shots == 0, "The exposed actor did not trigger alert and a windup before firing.");
            var healthA = actorA.GetComponent<DamageReceiver2D>().Health;
            var healthB = actorB.GetComponent<DamageReceiver2D>().Health;
            checkAfter = Time.fixedTimeAsDouble + 1.3;
            while (healthA.Current == 100 && Time.fixedTimeAsDouble < checkAfter) yield return null;
            Require(shots == 1 && healthA.Current == 92 && healthB.Current == 100,
                $"The corner shot should do exactly 8 damage only to actor A. Shots={shots}, A HP={healthA.Current}, B HP={healthB.Current}.");

            var unloadA = SceneManager.UnloadSceneAsync(sceneA);
            while (!unloadA.isDone) yield return null;
            Require(worldB.NavigationReady && worldB.TargetAlive && worldB.Navigation.Generation == unaffectedGeneration,
                "Unloading world A altered the surviving world's current navigation or actor.");
            var unloadB = SceneManager.UnloadSceneAsync(sceneB);
            while (!unloadB.isDone) yield return null;
        }

        private static GameObject CreateRoot(Scene scene, string name, Vec2 position)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = ArkonCoordinates.ToUnity(position);
            return root;
        }

        private static TopdownWorld2D CreateWorld(Scene scene, Vec2 actorPosition)
        {
            var worldRoot = CreateRoot(scene, "Physics world", Vec2.Zero);
            var world = worldRoot.AddComponent<TopdownWorld2D>();
            world.BuildNavigationOnStart = true;
            world.WorldSizePixels = new Vector2(300, 216);
            worldRoot.SetActive(true);

            var actorRoot = CreateRoot(scene, "Local player", actorPosition);
            var actor = actorRoot.AddComponent<TopdownActor2D>();
            actor.ReadKeyboardMouse = false;
            actor.AssignedWorld = world.GetComponent<Physics2DPort>();
            actorRoot.SetActive(true);
            world.BindTarget(actor);
            return world;
        }

        private static GameObject AddWall(Scene scene, string name, Vec2 at, Vec2 size)
        {
            var wall = CreateRoot(scene, name, at);
            wall.layer = ArkonCoordinates.WallsLayer;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(size.X, size.Y) / ArkonCoordinates.PixelsPerUnit;
            wall.SetActive(true);
            return wall;
        }

        private static void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
