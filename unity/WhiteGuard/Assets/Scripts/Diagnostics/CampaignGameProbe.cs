using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arkon.Core;
using Arkon.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using WhiteGuard.Gameplay;

namespace WhiteGuard.Diagnostics
{
    /// <summary>
    /// Real PlayMode startup/lifetime probe for all authored campaign maps.
    /// It does not defeat enemies, complete objectives, render the final art or play the campaign to its ending.
    /// </summary>
    public sealed class CampaignGameProbe : MonoBehaviour
    {
        [Serializable] private sealed class MapResult
        {
            public string id = "";
            public int entities = 0, enemies = 0, actors = 0, navigationSamples = 0;
        }
        [Serializable] private sealed class ProbeReport
        {
            public string status = "", unityVersion = "", campaignContentSha256 = "", testedCommit = "", failure = "";
            public string scope = "Nine authored map startup, input/pause gates, initial checkpoint retry and teardown. Not a full playthrough, GPU or listening test.";
            public int assertions = 0, narrativeCallbacks = 0;
            public bool fullCampaignCompleted = false, userSavePathUsed = false;
            public MapResult[] maps = Array.Empty<MapResult>();
        }

        public Action<bool, string> Completed;
        private readonly List<MapResult> maps = new List<MapResult>();
        private Scene previousScene, testScene;
        private CampaignGameLoop loop;
        private float previousTimeScale;
        private string saveDirectory, savePath, asynchronousFailure;
        private bool running;
        private int checks, narrativeCallbacks, feedbackCalls;
        private readonly ProbeReport report = new ProbeReport();

        private void Require(bool condition, string message)
        { checks++; if (!condition) throw new InvalidOperationException(message); }

        private IEnumerator Start()
        {
            running = true; previousTimeScale = Time.timeScale; previousScene = SceneManager.GetActiveScene();
            var exercise = Exercise(); string failure = null;
            while (true)
            {
                bool next = false; object pending = null;
                try { next = exercise.MoveNext(); if (next) pending = exercise.Current; }
                catch (Exception error) { failure = error.ToString(); }
                if (failure != null || !next) break;
                yield return pending;
            }
            if (loop)
            {
                loop.NarrativeRequested -= CompleteNarrative;
                loop.Feedback -= OnFeedback;
                try { loop.ReturnToMenu(); }
                catch (Exception error) { if (failure == null) failure = "Probe map cleanup failed: " + error; }
            }
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            if (testScene.IsValid() && testScene.isLoaded) yield return SceneManager.UnloadSceneAsync(testScene);
            Time.timeScale = previousTimeScale;
            var cleanupError = RemoveProbeSave();
            if (failure == null && cleanupError != null) failure = cleanupError;
            running = false;
            report.status = failure == null ? "PASS_MAP_STARTUP" : "FAIL_MAP_STARTUP";
            report.unityVersion = Application.unityVersion;
            report.testedCommit = Environment.GetEnvironmentVariable("WHITEGUARD_TESTED_COMMIT") ?? "not recorded";
            report.failure = failure ?? ""; report.assertions = checks;
            report.narrativeCallbacks = narrativeCallbacks; report.maps = maps.ToArray();
            Completed?.Invoke(failure == null, JsonUtility.ToJson(report, true));
            Destroy(gameObject);
        }

        private IEnumerator Exercise()
        {
            Time.timeScale = 1;
            testScene = SceneManager.CreateScene("White Guard campaign startup probe " + GetInstanceID());
            Require(SceneManager.SetActiveScene(testScene), "Cannot activate the temporary campaign scene.");
            saveDirectory = Path.Combine(Application.temporaryCachePath, "whiteguard-native-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveDirectory); savePath = Path.Combine(saveDirectory, "campaign.json");
            var cameraObject = NewRoot("Probe camera"); var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 4;
            camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 0;
            loop = NewRoot("Campaign probe host").AddComponent<CampaignGameLoop>();
            loop.SavePathOverride = savePath;
            loop.NarrativeRequested += CompleteNarrative; loop.Feedback += OnFeedback;
            var source = Resources.Load<TextAsset>("WhiteGuardCampaign/campaign");
            Require(source, "The actual campaign resource was not imported.");
            using (var sha = SHA256.Create()) report.campaignContentSha256 =
                BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source.text))).Replace("-", "").ToLowerInvariant();
            loop.Configure(source, camera); loop.SetInputEnabled(false);
            Require(Path.GetFullPath(loop.SavePath) == Path.GetFullPath(savePath), "The probe did not isolate its save path.");
            Require(!loop.SaveAvailable, "A unique native probe unexpectedly found an old save.");
            var expected = new[] { "p00", "m01", "m02", "m03", "m04", "m05", "m06", "m07", "e00" };
            Require(loop.Campaign.levels.Length == expected.Length, "The source campaign does not contain all nine authored episodes.");
            for (var i = 0; i < expected.Length; i++) Require(loop.Campaign.levels[i].id == expected[i], "Campaign chronology differs at " + expected[i]);

            loop.StartNew();
            var wait = WaitForPlaying("p00"); while (wait.MoveNext()) yield return wait.Current;
            VerifyMap("p00");
            Require(loop.Actors.Count > 0 && HasVehicle(), "The convoy map did not create its actual protected actors and vehicle.");
            Require(File.Exists(savePath) && !File.Exists(savePath + ".tmp"), "The initial native checkpoint was not written atomically.");
            var initial = JsonUtility.FromJson<CampaignSave>(File.ReadAllText(savePath));
            Require(initial != null && initial.checkpoint != null && initial.level == "p00" && initial.checkpoint.level == "p00"
                && initial.checkpoint.actors.Length == loop.Actors.Count && initial.checkpoint.enemies.Length == loop.Enemies.Count,
                "The first checkpoint does not describe the created native actors.");
            Require(string.IsNullOrEmpty(loop.LastError), "Initial checkpoint failure: " + loop.LastError);

            loop.World.CombatPaused = true;
            Require(loop.Player.Weapons.ConsumeShot(), "Cannot seed an input-gate cooldown.");
            var position = loop.Player.Position; var cooldown = loop.Player.Weapons.Cooldown;
            loop.Player.FeedInput(Vec2.Right, Vec2.Right, true, true, true, true);
            yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
            Require(Vec2.Distance(position, loop.Player.Position) < .001f && cooldown == loop.Player.Weapons.Cooldown,
                "Disabled campaign input advanced native movement or the gated weapon timer.");
            Require(!loop.Player.ReadKeyboardMouse && loop.Player.TransitionBlockedQuery != null && loop.Player.TransitionBlockedQuery(),
                "The scene-owned live input gate is not bound.");
            loop.Pause(true);
            var pausedTotal = loop.TotalSeconds; position = loop.Player.Position; cooldown = loop.Player.Weapons.Cooldown;
            var protectedPosition = loop.Actors[0].Position;
            yield return new WaitForSecondsRealtime(.15f);
            Require(loop.State == CampaignGameState.Paused && Time.timeScale == 0 && loop.TotalSeconds == pausedTotal
                && Vec2.Distance(position, loop.Player.Position) < .001f && cooldown == loop.Player.Weapons.Cooldown
                && Vec2.Distance(protectedPosition, loop.Actors[0].Position) < .001f,
                "Campaign pause failed to freeze real scene time, player or protected-body movement.");
            loop.Pause(false);
            Require(loop.State == CampaignGameState.Playing && Time.timeScale == 1, "The scene did not resume from pause.");

            // Reload the entry checkpoint through the actual public retry path.
            var oldPlayer = loop.Player; var oldWorld = loop.World;
            var beforeEntities = loop.Entities.Count; var beforeActors = loop.Actors.Count;
            var beforeEnemies = loop.Enemies.Count; var beforePickups = CountEntities("pickup");
            loop.Retry();
            wait = WaitForPlaying("p00"); while (wait.MoveNext()) yield return wait.Current;
            yield return null;
            VerifyMap("p00", false);
            Require(!oldPlayer && !oldWorld, "Retry left the previous native player/world alive.");
            Require(loop.Entities.Count == beforeEntities && loop.Actors.Count == beforeActors
                && loop.Enemies.Count == beforeEnemies && CountEntities("pickup") == beforePickups,
                "Retry duplicated actors, geometry, enemies or pickups.");
            Require(Vec2.Distance(loop.Player.Position, new Vec2(initial.checkpoint.playerX, initial.checkpoint.playerY)) < .01f,
                "Retry did not restore the actual checkpoint position.");

            // This only loads each authored map. It never grants objectives or claims a campaign clear.
            for (var i = 1; i < loop.Campaign.levels.Length; i++)
            {
                oldWorld = loop.World; oldPlayer = loop.Player;
                loop.StartLevel(loop.Campaign.levels[i].id);
                wait = WaitForPlaying(loop.Campaign.levels[i].id); while (wait.MoveNext()) yield return wait.Current;
                yield return null;
                VerifyMap(loop.Campaign.levels[i].id);
                Require(!oldWorld && !oldPlayer, "Map transition retained the previous native generation.");
                loop.Pause(true);
            }
            Require(maps.Count == expected.Length && narrativeCallbacks >= expected.Length,
                "The startup probe did not exercise the complete set of authored map/narrative entries.");

            // Keep detached C# models to check that destroying a scene cannot dispatch old campaign feedback.
            var oldHealth = loop.Player.GetComponent<DamageReceiver2D>().Health;
            var oldWeapon = loop.Player.Weapons;
            oldPlayer = loop.Player; oldWorld = loop.World;
            loop.ReturnToMenu();
            Require(loop.State == CampaignGameState.Menu && !loop.Player && !loop.World
                && loop.Entities.Count == 0 && loop.Actors.Count == 0 && loop.Enemies.Count == 0,
                "Returning to menu did not release the owned map registries.");
            yield return null;
            Require(!oldPlayer && !oldWorld && loop.GetComponentsInChildren<TopdownWorld2D>(true).Length == 0,
                "Returning to menu left a native combat world behind.");
            var oldFeedback = feedbackCalls;
            oldHealth.Tick(1); oldHealth.ApplyDamage(1); oldWeapon.Tick(1); oldWeapon.ConsumeShot();
            Require(loop.State == CampaignGameState.Menu && feedbackCalls == oldFeedback,
                "Detached actor models still dispatched observable campaign feedback after teardown.");
            Require(string.IsNullOrEmpty(loop.LastError), "The map startup probe observed a persistence/runtime error: " + loop.LastError);
        }

        private IEnumerator WaitForPlaying(string id)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + 45;
            while (loop && loop.State != CampaignGameState.Playing)
            {
                if (asynchronousFailure != null) throw new InvalidOperationException(asynchronousFailure);
                if (loop.State == CampaignGameState.Failed) throw new InvalidOperationException("Map startup failed: " + id + ": " + loop.FailureReason);
                if (Time.realtimeSinceStartupAsDouble > deadline) throw new TimeoutException("Native map startup timed out: " + id);
                yield return null;
            }
            Require(loop && loop.Level != null && loop.Level.id == id && loop.World && loop.World.NavigationReady && loop.Player,
                "Map entered Playing without its native world/navigation/player: " + id);
        }

        private void VerifyMap(string id, bool record = true)
        {
            Require(loop.Level.id == id && loop.World.gameObject.scene == testScene && loop.Player.gameObject.scene == testScene,
                "Map objects escaped their scene owner: " + id);
            Require(loop.GetComponentsInChildren<TopdownActor2D>(true).Length == 1
                && loop.GetComponentsInChildren<TopdownWorld2D>(true).Length == 1, "Map has duplicate player/world components: " + id);
            Require(loop.Enemies.Count == loop.Level.enemies.Length && loop.Actors.Count == loop.Level.actors.Length
                && CountEntities("pickup") == loop.Level.pickups.Length, "Native map counts differ from authored content: " + id);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in loop.Entities)
                Require(entity.Object && entity.Object.scene == testScene && ids.Add(entity.Kind + ":" + entity.Id),
                    "Missing, foreign or duplicate native entity: " + id + "/" + entity.Id);
            Physics2D.SyncTransforms();
            Require(loop.World.IsBodyPathClear(loop.Player.Position, loop.Player.Position), "The real player spawned inside a solid wall: " + id);
            var exit = loop.Level.exit;
            Require(loop.World.Navigation.FindPath(loop.Player.Position, new Vec2(exit.x + exit.w / 2, exit.y + exit.h / 2)).Count > 0,
                "Native sampled navigation cannot reach the authored exit: " + id);
            var playerHitbox = false;
            foreach (var collider in loop.Player.GetComponentsInChildren<Collider2D>())
                if (collider.isTrigger && collider.gameObject.layer == ArkonCoordinates.PlayerHitboxLayer) playerHitbox = true;
            Require(playerHitbox && loop.Player.GetComponent<DamageReceiver2D>().Health.Maximum == 100,
                "Player native hitbox/health was not configured: " + id);
            foreach (var enemy in loop.Enemies)
                Require(enemy && enemy.Model != null && enemy.HitboxCollider && enemy.HitboxCollider.isTrigger
                    && enemy.HitboxReceiver.Health.Maximum == 1 && enemy.HitboxReceiver.Health.InvulnerabilityDuration == 0,
                    "Enemy native body/hitbox failed to initialize: " + id);
            foreach (var actor in loop.Actors)
                Require(actor && actor.Definition != null && actor.Alive && loop.FindActor(actor.Definition.id) == actor,
                    "Campaign actor identity/lifetime failed to initialize: " + id);
            if (record) maps.Add(new MapResult { id = id, entities = loop.Entities.Count, enemies = loop.Enemies.Count,
                actors = loop.Actors.Count, navigationSamples = loop.World.Navigation.Samples });
        }

        private int CountEntities(string kind)
        { var count = 0; foreach (var entity in loop.Entities) if (entity.Kind == kind) count++; return count; }
        private bool HasVehicle()
        { foreach (var actor in loop.Actors) if (actor.Definition.role == "vehicle" && actor.Alive) return true; return false; }
        private GameObject NewRoot(string label)
        { var value = new GameObject(label); SceneManager.MoveGameObjectToScene(value, testScene); return value; }
        private void CompleteNarrative(NarrativeCard[] cards, Action finished)
        {
            if (cards == null || finished == null) { asynchronousFailure = "Missing narrative data/completion callback."; return; }
            foreach (var card in cards)
                if (card == null || string.IsNullOrWhiteSpace(card.text)) { asynchronousFailure = "An authored narrative card is empty."; return; }
            narrativeCallbacks++; finished();
        }
        private void OnFeedback(string id, Vec2 position) { feedbackCalls++; }
        private string RemoveProbeSave()
        {
            if (string.IsNullOrEmpty(saveDirectory)) return null;
            try { if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, true); return null; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { return "Could not clean the isolated probe save: " + error.Message; }
        }
        private void OnDestroy()
        {
            if (!running) return;
            if (loop) { loop.enabled = false; loop.gameObject.SetActive(false); }
            Time.timeScale = previousTimeScale;
            if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
            RemoveProbeSave();
        }
    }
}
