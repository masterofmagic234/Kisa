using System;
using System.Collections;
using Arkon.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arkon.Unity.Diagnostics
{
    /// <summary>
    /// Real PlayMode regression probe, not a gameplay scene or a .NET test.
    /// It creates and unloads a temporary runtime scene without saving assets.
    /// </summary>
    public sealed class PhysicsLifecycleProbe : MonoBehaviour
    {
        public Action<bool, string> Completed;
        private Scene testScene, previousScene;
        private float previousTimeScale;
        private bool running;
        private int checks;

        private void Require(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }

        private GameObject Root(string label, bool active = true)
        {
            var value = new GameObject(label);
            SceneManager.MoveGameObjectToScene(value, testScene);
            value.SetActive(active);
            return value;
        }

        private IEnumerator Start()
        {
            running = true; previousTimeScale = Time.timeScale; Time.timeScale = 1;
            previousScene = SceneManager.GetActiveScene();
            testScene = SceneManager.CreateScene("Arkon lifecycle probe " + GetInstanceID());
            var exercise = Exercise(); string failure = null;
            while (true)
            {
                bool next = false; object pending = null;
                try { next = exercise.MoveNext(); if (next) pending = exercise.Current; }
                catch (Exception error) { failure = error.ToString(); }
                if (failure != null || !next) break;
                yield return pending;
            }
            Time.timeScale = previousTimeScale;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (testScene.IsValid() && testScene.isLoaded) yield return SceneManager.UnloadSceneAsync(testScene);
            running = false;
            Completed?.Invoke(failure == null, failure ?? "Native physics lifecycle passed " + checks + " assertions.");
            Destroy(gameObject);
        }

        private IEnumerator Exercise()
        {
            var sleeping = Root("Inactive scene world", false);
            var inactiveWorld = sleeping.AddComponent<Physics2DPort>();
            var inactivePool = sleeping.AddComponent<ProjectilePool2D>();
            Require(!inactivePool.Spawn(Vec2.Zero, Vec2.Right, 650, SourceCollisionMasks.Walls, 0, 0, null),
                "An inactive pool accepted a spawn before Awake.");

            var actorObject = Root("Lifecycle actor", false);
            var actor = actorObject.AddComponent<TopdownActor2D>();
            actor.ReadKeyboardMouse = false;
            actorObject.SetActive(true);
            yield return null;
            var world = SceneObjects.Find<Physics2DPort>(actor);
            Require(world && world != inactiveWorld && world.isActiveAndEnabled,
                "Actor initialization selected the inactive world instead of a ready scene world.");
            Require(actor.Weapons.ConsumeShot(), "Cannot seed the actor's cooldown before transition.");
            var frozenCooldown = actor.Weapons.Cooldown;
            var frozenPosition = actor.Position;
            actor.TransitionBlockedQuery = () => true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(actor.Weapons.Cooldown == frozenCooldown && Vec2.Distance(actor.Position, frozenPosition) < .001f,
                "A live transition gate did not freeze the actor's weapon timer and body.");
            actor.TransitionBlockedQuery = null;
            var pool = world.GetComponent<ProjectilePool2D>();
            Require(pool && pool.isActiveAndEnabled, "The scene-owned projectile pool did not initialize.");
            var impacts = 0;
            Require(pool.Spawn(Vec2.Zero, new Vec2(100, 0), 1000, SourceCollisionMasks.Walls, 0, 0, _ => impacts++),
                "The initialized pool rejected a valid projectile.");
            for (var i = 0; i < 120 && impacts == 0; i++) yield return new WaitForFixedUpdate();
            Require(impacts == 1, "A real FixedUpdate projectile failed to complete exactly once.");
            pool.enabled = false;
            Require(!pool.Spawn(Vec2.Zero, Vec2.Right, 650, SourceCollisionMasks.Walls, 0, 0, null),
                "A disabled pool accepted a spawn.");
            pool.enabled = true;

            var oldImpacts = 0; var freshImpacts = 0; var freshAccepted = false;
            Require(pool.Spawn(Vec2.Zero, Vec2.Zero, 650, SourceCollisionMasks.Walls, 0, 0, _ =>
            {
                oldImpacts++;
                world.gameObject.SetActive(false);
                world.gameObject.SetActive(true);
                freshAccepted = pool.Spawn(new Vec2(300, 0), new Vec2(400, 0), 1000,
                    SourceCollisionMasks.Walls, 0, 0, impact => freshImpacts++);
            }), "The reentrant lifecycle projectile could not start.");
            for (var i = 0; i < 120 && freshImpacts == 0; i++) yield return new WaitForFixedUpdate();
            Require(oldImpacts == 1 && freshAccepted && freshImpacts == 1,
                "Disable/enable inside an impact retired or duplicated the replacement projectile lease.");

            var interactions = VerifyActions();
            while (interactions.MoveNext()) yield return interactions.Current;
        }

        private IEnumerator VerifyActions()
        {
            var root = Root("Native interaction world");
            var world = root.AddComponent<TopdownWorld2D>();
            var actorObject = Root("Native interaction actor", false);
            // Keep the previous lifecycle actor outside the interaction radius.
            actorObject.transform.position = ArkonCoordinates.ToUnity(new Vec2(300, 300));
            var actor = actorObject.AddComponent<TopdownActor2D>();
            actor.AssignedWorld = root.GetComponent<Physics2DPort>();
            actor.ReadKeyboardMouse = false;
            actorObject.SetActive(true);
            world.Target = actor;

            var enemyObject = Root("Native stunned target", false);
            enemyObject.transform.position = ArkonCoordinates.ToUnity(new Vec2(321, 300));
            var enemy = enemyObject.AddComponent<EnemyPort2D>();
            enemy.AssignedWorld = world;
            enemy.AIEnabled = false;
            enemyObject.SetActive(true);

            var pickupObject = Root("Native manual pickup", false);
            pickupObject.transform.position = ArkonCoordinates.ToUnity(new Vec2(321, 290));
            var pickup = pickupObject.AddComponent<PickupPort2D>();
            pickup.AssignedWorld = world;
            pickup.Kind = "shotgun";
            pickupObject.SetActive(true);

            var doorObject = Root("Native door", false);
            doorObject.transform.position = ArkonCoordinates.ToUnity(new Vec2(336, 310));
            var door = doorObject.AddComponent<DoorPort2D>();
            door.AssignedWorld = world;
            doorObject.SetActive(true);
            yield return null;
            yield return new WaitForFixedUpdate();

            enemy.Stun(3);
            Require(world.QueryAction(actor).Enemy == enemy && enemy.Model.State == EnemyState.Stunned,
                "An action preview must offer the stunned enemy ahead of a pickup or door without consuming it.");
            world.CombatPaused = true;
            Require(world.QueryAction(actor).Kind == TopdownActionKind.None,
                "A paused world exposed an action preview.");
            world.CombatPaused = false;
            actor.FeedInput(Vec2.Zero, Vec2.Right, false, true, false, false);
            yield return new WaitForFixedUpdate();
            Require(enemy.Model.State == EnemyState.Dead && world.QueryAction(actor).Pickup == pickup && !pickup.Model.Consumed,
                "The native action did not execute the previewed enemy and offer the pickup next.");

            actor.FeedInput(Vec2.Zero, Vec2.Right, false, true, false, false);
            yield return new WaitForFixedUpdate();
            Require(actor.Weapons.CurrentId == "shotgun" && world.QueryAction(actor).Door == door && !door.Model.IsOpening,
                "The native action did not apply the previewed pickup and then offer the closed door.");

            actor.CombatEnabled = false;
            Require(world.QueryAction(actor).Door == door, "An unarmed actor lost the ordinary door action.");
            actor.FeedInput(Vec2.Zero, Vec2.Right, false, true, false, false);
            yield return new WaitForFixedUpdate();
            Require((door.Model.IsOpening || door.Model.IsOpen) && world.QueryAction(actor).Kind == TopdownActionKind.None,
                "The native action did not open the previewed door.");
        }

        private void OnDestroy()
        {
            if (running) Time.timeScale = previousTimeScale;
        }
    }
}
