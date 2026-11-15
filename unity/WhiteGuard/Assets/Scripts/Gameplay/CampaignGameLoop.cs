using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arkon.Core;
using Arkon.Unity;
using UnityEngine;

namespace WhiteGuard.Gameplay
{
    public enum CampaignGameState { Menu, Loading, Narrative, Playing, Paused, Failed, Debrief, Finished }

    /// <summary>
    /// Scene owner for the authored White Guard campaign. Native Arkon components
    /// perform movement/hits; this host owns objectives, story actors and checkpoint lifetime.
    /// </summary>
    public sealed class CampaignGameLoop : MonoBehaviour, IObjectiveWorld, IEnemyTargetSelector
    {
        public const float CarriedBodyRadiusPixels = CampaignActorDimensions.CarriedBodyRadius;
        public CampaignData Campaign { get; private set; }
        public CampaignLevel Level { get; private set; }
        public CampaignProgress Progress { get; private set; }
        public TopdownActor2D Player { get; private set; }
        public TopdownWorld2D World { get; private set; }
        public CampaignGameState State { get; private set; } = CampaignGameState.Menu;
        public string FailureReason { get; private set; } = "";
        public string LastError { get; private set; } = "";
        public string InteractionHint
        {
            get
            {
                if (!GameplayActive || !Player || !World) return "";
                UpdateInteraction();
                if (ActiveInteraction != null) return ActiveInteraction.text;
                var action = World.QueryAction(Player);
                switch (action.Kind)
                {
                    case TopdownActionKind.Finish: return "Обезвредить оглушённого противника";
                    case TopdownActionKind.Pickup: return "Подобрать оружие";
                    case TopdownActionKind.Door: return "Открыть дверь";
                    default: return "";
                }
            }
        }
        public CampaignInteraction ActiveInteraction { get; private set; }
        public int CurrentLevelIndex { get; private set; } = -1;
        public int Deaths { get; private set; }
        public int Kills { get; private set; }
        public bool IsDebugSession { get; private set; }
        public float TotalSeconds { get; private set; }
        public bool GameplayActive => State == CampaignGameState.Playing && World && !World.Transitioning;
        public bool SaveAvailable => Campaign != null && File.Exists(NormalSavePath);
        public float LoadingProgress => World == null || World.Navigation.Width * World.Navigation.Height == 0 ? 0 :
            Mathf.Clamp01((float)World.Navigation.Samples / (World.Navigation.Width * World.Navigation.Height));
        public IReadOnlyList<CampaignEntity> Entities => entities;
        public IReadOnlyList<CampaignNpc> Actors => actors;
        public IReadOnlyList<EnemyPort2D> Enemies => enemies;
        public IReadOnlyCollection<string> Facts => facts;
        public IReadOnlyCollection<string> CompletedLevels => completedLevels;
        /// <summary>Optional automation sandbox; production leaves this unset.</summary>
        public string SavePathOverride { get; set; }
        public string NormalSavePath => string.IsNullOrWhiteSpace(SavePathOverride)
            ? Path.Combine(Application.persistentDataPath, "white-guard-campaign-v1.json") : Path.GetFullPath(SavePathOverride);
        public string SavePath
        {
            get
            {
                var normal = NormalSavePath;
                return IsDebugSession ? Path.Combine(Path.GetDirectoryName(normal),
                    Path.GetFileNameWithoutExtension(normal) + ".debug" + Path.GetExtension(normal)) : normal;
            }
        }

        public event Action StateChanged;
        public event Action<NarrativeCard[], Action> NarrativeRequested;
        public event Action<CampaignEntity> EntityCreated;
        public event Action<string> Notice;
        public event Action<CampaignLevel> LevelCompleted;
        public event Action ProgressChanged;
        public event Action<string, Vec2> Feedback;

        private readonly List<CampaignEntity> entities = new List<CampaignEntity>();
        private readonly List<CampaignNpc> actors = new List<CampaignNpc>();
        private readonly List<EnemyPort2D> enemies = new List<EnemyPort2D>();
        private readonly Dictionary<string, EnemyEntry> enemyById = new Dictionary<string, EnemyEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, DoorEntry> doors = new Dictionary<string, DoorEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, PickupEntry> pickups = new Dictionary<string, PickupEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, CampaignNpc> actorById = new Dictionary<string, CampaignNpc>(StringComparer.Ordinal);
        private readonly Dictionary<string, CampaignRect> regions = new Dictionary<string, CampaignRect>(StringComparer.Ordinal);
        private readonly Dictionary<EnemyPort2D, Vec2> retreats = new Dictionary<EnemyPort2D, Vec2>();
        private readonly HashSet<string> interacted = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> consumed = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> neutralized = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> facts = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> completedLevels = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<EnemyTargetCandidate> targetCandidates = new List<EnemyTargetCandidate>();
        private readonly Dictionary<int, EnemyTarget2D> targetHandles = new Dictionary<int, EnemyTarget2D>();
        private Camera gameplayCamera;
        private Transform mapRoot;
        private CampaignMapBuilder builder;
        private CampaignCheckpoint checkpoint;
        private Coroutine loading;
        private int generation;
        private bool completionRaised, suppressCallbacks, inputEnabled = true;
        private string lastObjectiveText = "";
        private string contentHash;

        private sealed class EnemyEntry { public CampaignEnemy Data; public EnemyPort2D Port; }
        private sealed class DoorEntry { public CampaignDoor Data; public DoorPort2D Port; }
        private sealed class PickupEntry { public CampaignPickup Data; public PickupPort2D Port; }

        public void Configure(TextAsset source, Camera camera)
        {
            if (!source) throw new ArgumentNullException(nameof(source));
            var data = JsonUtility.FromJson<CampaignData>(source.text);
            Validate(data);
            using (var hash = SHA256.Create()) contentHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(source.text))).Replace("-", "").ToLowerInvariant();
            Campaign = data; gameplayCamera = camera; FailureReason = LastError = "";
            SetState(CampaignGameState.Menu);
        }

        public void StartNew()
        {
            RequireConfigured();
            IsDebugSession = false;
            completedLevels.Clear(); facts.Clear(); Deaths = Kills = 0; TotalSeconds = 0; checkpoint = null;
            StartLevel(string.IsNullOrEmpty(Campaign.firstLevel) ? Campaign.levels[0].id : Campaign.firstLevel);
        }

        public bool ContinueSaved()
        {
            RequireConfigured();
            try
            {
                var save = JsonUtility.FromJson<CampaignSave>(File.ReadAllText(NormalSavePath));
                if (save != null && save.contentHash != contentHash) throw new InvalidDataException("Сохранение создано для другой версии кампании.");
                if (!CampaignCheckpointValidation.Validate(Campaign, save, out var validationError)) throw new InvalidDataException(validationError);
                LastError = "";
                IsDebugSession = false;
                completedLevels.Clear(); foreach (var id in save.completedLevels) completedLevels.Add(id);
                facts.Clear(); foreach (var fact in save.facts) if (!string.IsNullOrEmpty(fact)) facts.Add(fact);
                Deaths = Math.Max(0, save.deaths); Kills = Math.Max(0, save.kills); TotalSeconds = Math.Max(0, save.totalSeconds);
                if (string.IsNullOrEmpty(save.level)) { checkpoint = null; TearDownMap(); SetState(CampaignGameState.Finished); return true; }
                var restored = save.checkpoint;
                if (restored != null && (restored.level != save.level || restored.health <= 0)) restored = null;
                BeginLevel(save.level, restored); return true;
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is UnauthorizedAccessException)
            {
                LastError = "Не удалось прочитать сохранение: " + error.Message;
                Notice?.Invoke(LastError); return false;
            }
        }

        public void StartLevel(string id) { RequireConfigured(); BeginLevel(id, null); }
        public bool StartDebugLevel(string id)
        {
            RequireConfigured();
            var index = IndexOf(id);
            if (index < 0) throw new ArgumentException("Unknown debug campaign level: " + id, nameof(id));
            if (!IsDebugSession && (GameplayActive || State == CampaignGameState.Paused))
            {
                SaveCheckpoint();
                // Preserve the live ordinary run when storage could not accept
                // its checkpoint; a debug jump must not silently discard it.
                if (!string.IsNullOrEmpty(LastError)) return false;
            }
            IsDebugSession = true;
            completedLevels.Clear(); facts.Clear(); Deaths = Kills = 0; TotalSeconds = 0; checkpoint = null;
            for (var i = 0; i < index; i++)
            {
                var previous = Campaign.levels[i]; completedLevels.Add(previous.id); facts.Add("complete:" + previous.id);
                foreach (var interaction in Safe(previous.interactions))
                    foreach (var fact in Safe(interaction.grants)) if (!string.IsNullOrEmpty(fact)) facts.Add(fact);
            }
            BeginLevel(id, null);
            return true;
        }
        public void Retry() { if (Level != null) BeginLevel(Level.id, checkpoint); }
        public void RestartLevel() { if (Level != null) BeginLevel(Level.id, null); }

        private void BeginLevel(string id, CampaignCheckpoint restore)
        {
            var index = IndexOf(id);
            if (index < 0) throw new ArgumentException("Unknown campaign level: " + id, nameof(id));
            if (loading != null) { StopCoroutine(loading); loading = null; }
            generation++; var owner = generation; Time.timeScale = 1;
            TearDownMap(); CurrentLevelIndex = index; Level = Campaign.levels[index];
            checkpoint = restore;
            Progress = new CampaignProgress(Level);
            interacted.Clear(); consumed.Clear(); neutralized.Clear(); regions.Clear();
            if (restore != null)
            {
                foreach (var value in Safe(restore.interacted)) interacted.Add(value);
                foreach (var value in Safe(restore.consumedPickups)) consumed.Add(value);
                foreach (var value in Safe(restore.neutralized)) neutralized.Add(value);
            }
            foreach (var region in Safe(Level.regions)) regions.Add(region.id, region);
            if (Level.exit != null && !string.IsNullOrEmpty(Level.exit.id) && !regions.ContainsKey(Level.exit.id)) regions.Add(Level.exit.id, Level.exit);
            completionRaised = false; suppressCallbacks = false; ActiveInteraction = null; FailureReason = ""; lastObjectiveText = "";
            SetState(CampaignGameState.Loading);
            loading = StartCoroutine(BuildLevel(owner, restore));
        }

        private IEnumerator BuildLevel(int owner, CampaignCheckpoint restore)
        {
            Exception buildError = null;
            try
            {
                var root = new GameObject("White Guard · " + Level.id); root.transform.SetParent(transform, false); mapRoot = root.transform;
                builder = new CampaignMapBuilder(this, mapRoot);
                builder.Build(Level, gameplayCamera); World.EnemyTargetSelector = this;
            }
            catch (Exception error) { buildError = error; }
            if (buildError != null)
            {
                LastError = "Не удалось открыть локацию: " + buildError.Message;
                Debug.LogException(buildError, this); Fail(LastError); loading = null; yield break;
            }
            yield return null; // All actor Start methods bind to the scene-owned world.
            var deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (owner == generation && World && !World.NavigationReady)
            {
                if (Time.realtimeSinceStartupAsDouble > deadline) { LastError = "Не удалось подготовить маршруты локации."; Fail(LastError); loading = null; yield break; }
                yield return null;
            }
            if (owner != generation || !World || !Player) yield break;
            if (restore != null)
            {
                suppressCallbacks = true;
                try { RestoreCheckpoint(restore); }
                catch (Exception error)
                {
                    suppressCallbacks = false; checkpoint = null; LastError = "Контрольная точка повреждена: " + error.Message;
                    Fail(LastError); loading = null; yield break;
                }
                suppressCallbacks = false;
            }
            loading = null;
            if (restore == null) ShowNarrative(Level.before, () => EnterGameplay(owner));
            else EnterGameplay(owner);
        }

        private void EnterGameplay(int owner)
        {
            if (owner != generation || !World || !Player) return;
            World.LevelComplete = false; World.CombatPaused = false; World.Transitioning = false;
            var nativeGate = Player.TransitionBlockedQuery;
            Player.TransitionBlockedQuery = () => !this || State != CampaignGameState.Playing || !inputEnabled || nativeGate?.Invoke() == true;
            Player.ReadKeyboardMouse = inputEnabled; Player.Motor.ControlsEnabled = true;
            UpdateEnemyActivation();
            SetState(CampaignGameState.Playing);
            Progress.Tick(0, this); lastObjectiveText = Progress.ActiveText; ProgressChanged?.Invoke();
            SaveCheckpoint();
        }

        public void Pause(bool value)
        {
            if (value && State == CampaignGameState.Playing)
            {
                Player.Motor.ClearControls(); Time.timeScale = 0; SetState(CampaignGameState.Paused);
            }
            else if (!value && State == CampaignGameState.Paused)
            { Time.timeScale = 1; SetState(CampaignGameState.Playing); }
        }

        public void SetInputEnabled(bool value)
        {
            inputEnabled = value;
            if (Player) { Player.ReadKeyboardMouse = value; if (!value) Player.Motor.ClearControls(); }
        }

        public void ReturnToMenu()
        {
            if (GameplayActive || State == CampaignGameState.Paused) SaveCheckpoint();
            if (loading != null) { StopCoroutine(loading); loading = null; }
            generation++; Time.timeScale = 1; TearDownMap(); SetState(CampaignGameState.Menu);
        }

        public void Advance()
        {
            if (State != CampaignGameState.Debrief && State != CampaignGameState.Finished) return;
            if (CurrentLevelIndex + 1 < Campaign.levels.Length) StartLevel(Campaign.levels[CurrentLevelIndex + 1].id);
            else { TearDownMap(); SetState(CampaignGameState.Finished); Persist(""); }
        }

        private void Update()
        {
            if (!GameplayActive) return;
            TotalSeconds += Time.deltaTime;
            Progress.Tick(Time.deltaTime, this);
            if (Progress.Failed) { Fail(Progress.FailureReason); return; }
            if (lastObjectiveText != Progress.ActiveText) { lastObjectiveText = Progress.ActiveText; ProgressChanged?.Invoke(); }
            UpdateInteraction(); UpdateRetreats(); UpdateCarrying(); UpdateEnemyActivation();
            if (Level.combat && inputEnabled)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) Equip("pistol");
                if (Input.GetKeyDown(KeyCode.Alpha2)) Equip("shotgun");
                if (Input.GetKeyDown(KeyCode.Alpha3)) Equip("bat");
            }
            if (Progress.AllObjectivesComplete && Level.exit != null && Level.exit.Contains(Player.Position.X, Player.Position.Y)) CompleteLevel();
        }

        public bool Equip(string id)
        {
            if (!GameplayActive || !Player || !Level.combat) return false;
            if (id != "bat" && Player.Weapons.GetReserve(id) <= 0) { Notice?.Invoke("Нет патронов для этого оружия."); return false; }
            return Player.Weapons.Equip(id);
        }

        private void UpdateInteraction()
        {
            ActiveInteraction = null; var best = float.PositiveInfinity;
            foreach (var interaction in Safe(Level.interactions))
            {
                if (interacted.Contains(interaction.id) || !Progress.PrerequisitesMet(interaction.requires)) continue;
                var position = P(interaction); var distance = Vec2.Distance(Player.Position, position);
                if (distance > (interaction.radius > 0 ? interaction.radius : 38) || distance >= best) continue;
                if (!World.HasLineOfSight(Player.Position, position)) continue;
                ActiveInteraction = interaction; best = distance;
            }
        }

        public void Interact()
        {
            if (GameplayActive && World && Player) World.PerformAction(Player);
        }
        private bool HasMissionAction(TopdownActor2D actor)
        {
            if (!GameplayActive || actor != Player) return false;
            UpdateInteraction(); return ActiveInteraction != null;
        }
        private bool TryMissionAction(TopdownActor2D actor)
        {
            if (!GameplayActive || actor != Player) return false;
            UpdateInteraction(); var interaction = ActiveInteraction;
            if (interaction == null) return false;
            interacted.Add(interaction.id);
            foreach (var value in Safe(interaction.grants)) if (!string.IsNullOrEmpty(value)) facts.Add(value);
            switch (interaction.action)
            {
                case "rescue": FindActor(interaction.target)?.Free(); break;
                case "carry": FindActor(interaction.target)?.Carry(); break;
                case "stand_down": StandDown(interaction.target, interaction.region); break;
                case "unlock": if (doors.TryGetValue(interaction.target ?? "", out var door)) door.Port.Locked = false; break;
            }
            Progress.Tick(0, this); ProgressChanged?.Invoke();
            if (!string.IsNullOrEmpty(interaction.body)) Notice?.Invoke(interaction.body);
            else Feedback?.Invoke("interact", Player.Position);
            if (interaction.checkpoint || interaction.action == "checkpoint") SaveCheckpoint();
            ActiveInteraction = null;
            return true;
        }

        private void CompleteLevel()
        {
            if (!GameplayActive || completionRaised) return;
            completionRaised = true; completedLevels.Add(Level.id); facts.Add("complete:" + Level.id);
            World.LevelComplete = true; World.CombatPaused = true; World.Transitioning = true;
            World.GetComponent<ProjectilePool2D>().enabled = false;
            checkpoint = null;
            var next = CurrentLevelIndex + 1 < Campaign.levels.Length ? Campaign.levels[CurrentLevelIndex + 1].id : "";
            Persist(next);
            var owner = generation;
            ShowNarrative(Level.after, () =>
            {
                if (owner != generation) return;
                SetState(CampaignGameState.Debrief); LevelCompleted?.Invoke(Level);
            });
        }

        private void ShowNarrative(NarrativeCard[] cards, Action finished)
        {
            if (World) { World.Transitioning = true; World.CombatPaused = true; }
            if (cards == null || cards.Length == 0 || NarrativeRequested == null) { finished(); return; }
            var owner = generation; var called = false; SetState(CampaignGameState.Narrative);
            NarrativeRequested.Invoke(cards, () => { if (called || owner != generation) return; called = true; finished(); });
        }

        public void Fail(string reason)
        {
            if (State == CampaignGameState.Failed || State == CampaignGameState.Menu || State == CampaignGameState.Finished || suppressCallbacks) return;
            var gameplayFailure = State == CampaignGameState.Playing || State == CampaignGameState.Paused;
            if (gameplayFailure) Deaths++;
            FailureReason = reason;
            if (World)
            {
                World.CombatPaused = true; World.Transitioning = true;
                World.GetComponent<ProjectilePool2D>().enabled = false;
            }
            if (Player) Player.Motor.ClearControls();
            Time.timeScale = 1; SetState(CampaignGameState.Failed);
            if (gameplayFailure) Persist(Level == null ? "" : Level.id);
        }

        public void SaveCheckpoint()
        {
            if ((State != CampaignGameState.Playing && State != CampaignGameState.Paused) || !Player ||
                Player.GetComponent<DamageReceiver2D>().Health.Dead || Level == null || Progress == null || Progress.Failed || !World) return;
            var capture = new CampaignCheckpoint
            {
                level = Level.id, progress = Progress.Capture(), playerX = Player.Position.X, playerY = Player.Position.Y,
                health = Player.GetComponent<DamageReceiver2D>().Health.Current,
                playerHealth = Player.GetComponent<DamageReceiver2D>().Health.CaptureState(), inventory = Player.Weapons.CaptureState(),
                bottle = World.HasBottle, interacted = Copy(interacted), consumedPickups = Copy(consumed), neutralized = Copy(neutralized)
            };
            var actorStates = new List<CampaignActorState>();
            foreach (var actor in actors)
                if (actor) actorStates.Add(new CampaignActorState { id = actor.Definition.id, x = actor.Position.X, y = actor.Position.Y,
                    health = actor.Health.Current, healthState = actor.Health.CaptureState(), freed = actor.Freed, carried = actor.Carried, routeIndex = actor.RouteIndex });
            capture.actors = actorStates.ToArray();
            var enemyStates = new List<CampaignEnemyState>();
            foreach (var entry in enemyById.Values)
                if (entry.Port && entry.Port.Model != null) enemyStates.Add(new CampaignEnemyState { id = entry.Data.id,
                    x = entry.Port.Position.X, y = entry.Port.Position.Y, dead = entry.Port.Model.State == EnemyState.Dead });
            capture.enemies = enemyStates.ToArray();
            var doorStates = new List<CampaignDoorState>();
            foreach (var entry in doors.Values)
                if (entry.Port && entry.Port.Model != null) doorStates.Add(new CampaignDoorState { id = entry.Data.id,
                    open = entry.Port.Model.IsOpen || entry.Port.Model.IsOpening, locked = entry.Port.Locked });
            capture.doors = doorStates.ToArray();
            var activePickups = new List<CampaignPickup>();
            foreach (var entry in pickups.Values) if (entry.Port && !consumed.Contains(entry.Data.id)) activePickups.Add(entry.Data);
            capture.activePickups = activePickups.ToArray(); checkpoint = capture;
            Persist(Level.id);
        }

        private void RestoreCheckpoint(CampaignCheckpoint state)
        {
            if (!CampaignCheckpointValidation.Validate(Level, state, out var validationError)) throw new InvalidDataException(validationError);
            if (!Progress.Restore(state.progress)) throw new InvalidDataException("Неверные цели контрольной точки.");
            Position(Player.gameObject, state.playerX, state.playerY);
            Player.GetComponent<DamageReceiver2D>().Health.TryRestoreState(state.playerHealth);
            Player.Weapons.TryRestoreState(state.inventory); World.HasBottle = state.bottle;
            foreach (var actorState in Safe(state.actors))
            {
                var actor = FindActor(actorState.id); if (!actor) continue;
                Position(actor.gameObject, actorState.x, actorState.y); actor.Health.TryRestoreState(actorState.healthState);
                actor.Restore(actorState.freed, actorState.routeIndex, actorState.carried);
            }
            foreach (var enemyState in Safe(state.enemies))
            {
                if (!enemyById.TryGetValue(enemyState.id, out var entry) || !entry.Port) continue;
                Position(entry.Port.gameObject, enemyState.x, enemyState.y);
                if (enemyState.dead) entry.Port.Kill();
            }
            foreach (var doorState in Safe(state.doors))
            {
                if (!doors.TryGetValue(doorState.id, out var door) || !door.Port) continue;
                door.Port.Locked = doorState.locked;
                if (doorState.open) door.Port.Interact(Player.Position);
            }
            foreach (var pickup in Safe(state.activePickups))
                if (pickup != null && !pickups.ContainsKey(pickup.id) && !consumed.Contains(pickup.id)) builder.Pickup(pickup, World);
            UpdateCarrying();
            Physics2D.SyncTransforms();
            foreach (var actor in actors)
                if (actor && actor.Definition.required && !actor.Alive) throw new InvalidDataException("Обязательный персонаж отсутствует в контрольной точке.");
        }

        private void Persist(string nextLevel)
        {
            if (Campaign == null) return;
            try
            {
                var data = new CampaignSave { campaign = Campaign.id, contentHash = contentHash, level = nextLevel, checkpoint = checkpoint,
                    completedLevels = Copy(completedLevels), facts = Copy(facts), deaths = Deaths, kills = Kills, totalSeconds = TotalSeconds };
                AtomicTextFile.Write(SavePath, JsonUtility.ToJson(data, true));
                LastError = "";
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { LastError = "Не удалось записать контрольную точку: " + error.Message; Notice?.Invoke(LastError); }
        }

        internal void AssignWorld(TopdownWorld2D world)
        {
            World = world; world.CombatFeedback += OnFeedback;
            world.HasExternalAction = HasMissionAction;
            world.TryHandleExternalAction = TryMissionAction;
        }
        internal void AssignPlayer(TopdownActor2D player)
        {
            Player = player;
            player.GetComponent<DamageReceiver2D>().Health.Death += OnPlayerDeath;
            player.GetComponent<DamageReceiver2D>().Health.Damaged += OnPlayerDamaged;
            player.Weapons.ShotConsumed += OnPlayerWeaponChanged;
            player.PlayerShotHit += OnPlayerHit;
        }
        internal void EmitEntity(CampaignEntity entity) { entities.Add(entity); EntityCreated?.Invoke(entity); }
        internal void RegisterDoor(CampaignDoor data, DoorPort2D door)
        {
            doors.Add(data.id, new DoorEntry { Data = data, Port = door });
            door.Opened += OnDoorOpened; door.Slammed += OnDoorSlammed;
        }
        internal void RegisterEnemy(CampaignEnemy data, EnemyPort2D enemy)
        {
            enemyById.Add(data.id, new EnemyEntry { Data = data, Port = enemy }); enemies.Add(enemy);
            enemy.Killed += OnEnemyKilled; enemy.Stunned += OnEnemyStunned;
        }
        internal void RegisterPickup(CampaignPickup data, PickupPort2D pickup)
        {
            pickups.Add(data.id, new PickupEntry { Data = data, Port = pickup }); pickup.Collected += OnPickup;
        }
        internal void RegisterActor(CampaignActor data, CampaignNpc actor)
        {
            actors.Add(actor); actorById.Add(data.id, actor); actor.Died += OnActorDeath; actor.Fired += OnActorFired;
        }
        internal bool IsPickupConsumed(string id) => consumed.Contains(id);
        internal bool IsNeutralized(string id) => neutralized.Contains(id);
        private void OnPlayerDeath() => Fail("Мазур погиб. Вернитесь к контрольной точке.");
        private void OnPlayerDamaged(int amount, int source) { if (GameplayActive) Feedback?.Invoke("player_hurt", Player.Position); }
        private void OnPlayerWeaponChanged(string id, int ammo)
        { if (GameplayActive && Player.Weapons.Cooldown > 0) Feedback?.Invoke(id == "bat" ? "swing" : "player_shot", Player.Position); }
        private void OnDoorOpened(DoorPort2D door) { if (GameplayActive) Feedback?.Invoke("door", door.Position); }
        private void OnDoorSlammed(DoorPort2D door) { if (GameplayActive) Feedback?.Invoke("slam", door.Position); }
        private void OnPlayerHit(Vec2 at, Vec2 direction) => Feedback?.Invoke("hit", at);
        private void OnFeedback(string id, Vec2 at) => Feedback?.Invoke(id, at);
        private void OnEnemyStunned(EnemyPort2D enemy, float duration) => Feedback?.Invoke("stun", enemy.Position);
        private void OnActorFired(CampaignNpc actor, Vec2 aim) => Feedback?.Invoke("ally_shot", actor.Position);
        private void OnActorDeath(CampaignNpc actor)
        {
            if (suppressCallbacks) return;
            Feedback?.Invoke("actor_down", actor.Position);
            if (actor.Definition.required) Fail(actor.Definition.name + " погибает. Задача провалена.");
        }
        private void OnPickup(PickupPort2D item, TopdownActor2D actor)
        {
            foreach (var entry in pickups) if (entry.Value.Port == item) { consumed.Add(entry.Key); break; }
        }
        private void OnEnemyKilled(EnemyPort2D enemy)
        {
            if (suppressCallbacks || !GameplayActive) return;
            Kills++; Feedback?.Invoke("enemy_down", enemy.Position);
            foreach (var entry in enemyById.Values)
                if (entry.Port == enemy)
                {
                    var id = entry.Data.id + "_weapon";
                    if (!pickups.ContainsKey(id) && !consumed.Contains(id)) builder.Pickup(new CampaignPickup
                    { id = id, kind = enemy.Kind == EnemyKind.Gunman ? "pistol" : "bat", x = enemy.Position.X, y = enemy.Position.Y }, World);
                    break;
                }
        }

        public CampaignNpc FindActor(string id) => id != null && actorById.TryGetValue(id, out var actor) ? actor : null;
        public Vec2 ClampPoint(Vec2 point) => new Vec2(Mathf.Clamp(point.X, 14, Level.width - 14), Mathf.Clamp(point.Y, 14, Level.height - 14));
        public Vec2 FollowerPoint(CampaignNpc actor)
        {
            var index = Math.Max(0, actors.IndexOf(actor));
            var forward = Player.Motor.Velocity.LengthSquared > 4 ? Player.Motor.Velocity.Normalized : Player.Motor.AimDirection;
            if (actor.Carried) return ClampPoint(Player.Position - forward * 23);
            var lateral = new Vec2(-forward.Y, forward.X) * (index % 2 == 0 ? -20 : 20);
            return ClampPoint(Player.Position - forward * (46 + index / 2 * 21) + lateral);
        }
        public EnemyPort2D NearestEnemy(Vec2 from, float maximumDistance)
        {
            EnemyPort2D result = null; var nearest = maximumDistance;
            foreach (var entry in enemyById.Values)
            {
                var enemy = entry.Port;
                if (!IsEnemyHostile(entry)) continue;
                var distance = Vec2.Distance(from, enemy.Position);
                if (distance < nearest) { nearest = distance; result = enemy; }
            }
            return result;
        }
        public bool HasNearbyThreat(Vec2 point, float radius)
        {
            if (!World) return false;
            foreach (var entry in enemyById.Values)
            {
                var enemy = entry.Port;
                if (!IsEnemyHostile(entry)) continue;
                if (Vec2.Distance(point, enemy.Position) < radius && World.HasLineOfSight(point, enemy.Position)) return true;
            }
            return false;
        }
        public bool IsEnemyHostile(EnemyPort2D enemy)
        {
            if (!enemy) return false;
            foreach (var entry in enemyById.Values) if (entry.Port == enemy) return IsEnemyHostile(entry);
            return false;
        }
        private bool IsEnemyHostile(EnemyEntry entry) => entry.Port && entry.Port.isActiveAndEnabled && entry.Port.AIEnabled &&
            entry.Port.Model != null && entry.Port.Model.State != EnemyState.Dead && !neutralized.Contains(entry.Data.id);
        public void OpenDoorsNear(Vec2 point)
        {
            foreach (var door in doors.Values)
                if (door.Port && door.Port.Model != null && !door.Port.Model.IsOpen && !door.Port.Locked && Vec2.Distance(point, door.Port.Position) < 34)
                    door.Port.Interact(point);
        }
        public void NotifyActorFreed(CampaignNpc actor) { Feedback?.Invoke("rescue", actor.Position); }

        private void UpdateCarrying()
        {
            var carrying = false;
            foreach (var actor in actors) if (actor && actor.Alive && actor.Carried) carrying = true;
            Player.Motor.MoveSpeed = carrying ? 70 : 120;
            // The carried hitbox lies within this footprint, so passing a wall
            // cannot leave the passenger body protruding through solid geometry.
            Player.GetComponent<CircleCollider2D>().radius = (carrying ? CarriedBodyRadiusPixels : 7) / ArkonCoordinates.PixelsPerUnit;
        }
        internal void RefreshCarrying() { if (Player) UpdateCarrying(); }

        private void UpdateEnemyActivation()
        {
            foreach (var entry in enemyById.Values)
                if (entry.Port && !neutralized.Contains(entry.Data.id)) entry.Port.AIEnabled =
                    string.IsNullOrEmpty(entry.Data.activeAfterObjective) || Progress.IsCompleted(entry.Data.activeAfterObjective);
        }

        private void StandDown(string group, string destination)
        {
            var goal = regions.TryGetValue(destination ?? "", out var region) ? new Vec2(region.x + region.w / 2, region.y + region.h / 2) : Player.Position;
            foreach (var entry in enemyById.Values)
            {
                if (entry.Data.group != group || !entry.Port || entry.Port.Model == null || entry.Port.Model.State == EnemyState.Dead) continue;
                neutralized.Add(entry.Data.id);
                var path = World.Navigation.FindPath(entry.Port.Position, goal);
                entry.Port.AIEnabled = true;
                entry.Port.Stage(path.Count > 0 ? path : new[] { goal }, goal);
                retreats[entry.Port] = goal;
            }
        }
        private void UpdateRetreats()
        {
            if (retreats.Count == 0) return;
            var completed = new List<EnemyPort2D>();
            foreach (var retreat in retreats)
            {
                if (retreat.Key) OpenDoorsNear(retreat.Key.Position);
                if (!retreat.Key || Vec2.Distance(retreat.Key.Position, retreat.Value) < 20)
                { if (retreat.Key) retreat.Key.gameObject.SetActive(false); completed.Add(retreat.Key); }
            }
            foreach (var enemy in completed) retreats.Remove(enemy);
        }

        public bool TrySelectTarget(EnemyPort2D enemy, out EnemyTarget2D target)
        {
            target = default;
            if (!Player || !World || !enemy) return false;
            targetCandidates.Clear(); targetHandles.Clear();
            var playerReceiver = Player.GetComponent<DamageReceiver2D>();
            var playerHandle = new EnemyTarget2D(Player.transform, playerReceiver);
            AddTarget(playerHandle);
            foreach (var actor in actors) if (actor && actor.CanBeTargeted) AddTarget(new EnemyTarget2D(actor.transform, actor.Receiver));
            var selected = EnemyTargetSelection.Select(enemy.Position, targetCandidates, World.HasLineOfSight, playerHandle.ActorId);
            return selected.HasValue && targetHandles.TryGetValue(selected.Value.ActorId, out target);
        }
        private void AddTarget(EnemyTarget2D target)
        {
            if (!target.Alive) return;
            targetCandidates.Add(new EnemyTargetCandidate(target.ActorId, target.Position)); targetHandles[target.ActorId] = target;
        }

        public bool HasInteracted(string id) => !string.IsNullOrEmpty(id) && interacted.Contains(id);
        public bool PlayerInRegion(string id) => Player && regions.TryGetValue(id ?? "", out var region) && region.Contains(Player.Position.X, Player.Position.Y);
        public bool IsGroupClear(string id)
        {
            if (Level == null || string.IsNullOrEmpty(id)) return false;
            var known = false;
            foreach (var entry in enemyById.Values)
                if (!string.IsNullOrEmpty(id) && entry.Data.group == id)
                {
                    known = true;
                    if (!neutralized.Contains(entry.Data.id) && entry.Port && (entry.Port.Model == null || entry.Port.Model.State != EnemyState.Dead)) return false;
                }
            if (!known) foreach (var enemy in Safe(Level.enemies)) if (enemy.group == id && !string.IsNullOrEmpty(id)) { known = true; break; }
            return known;
        }
        public bool IsActorAlive(string id) { var actor = FindActor(id); return actor && actor.Alive; }
        public bool IsActorInRegion(string actorId, string regionId)
        {
            var actor = FindActor(actorId);
            return actor && actor.Alive && regions.TryGetValue(regionId ?? "", out var region) && region.Contains(actor.Position.X, actor.Position.Y);
        }
        public bool IsActorRouteComplete(string id) { var actor = FindActor(id); return actor && actor.Alive && actor.RouteComplete; }

        private void TearDownMap()
        {
            if (World)
            {
                World.CombatFeedback -= OnFeedback;
                World.HasExternalAction = null; World.TryHandleExternalAction = null; World.EnemyTargetSelector = null;
            }
            if (Player)
            {
                Player.PlayerShotHit -= OnPlayerHit;
                Player.GetComponent<DamageReceiver2D>().Health.Death -= OnPlayerDeath;
                Player.GetComponent<DamageReceiver2D>().Health.Damaged -= OnPlayerDamaged;
                Player.Weapons.ShotConsumed -= OnPlayerWeaponChanged;
            }
            foreach (var actor in actors) if (actor) { actor.Died -= OnActorDeath; actor.Fired -= OnActorFired; }
            foreach (var enemy in enemies) if (enemy) { enemy.Killed -= OnEnemyKilled; enemy.Stunned -= OnEnemyStunned; }
            foreach (var item in pickups.Values) if (item.Port) item.Port.Collected -= OnPickup;
            foreach (var door in doors.Values) if (door.Port) { door.Port.Opened -= OnDoorOpened; door.Port.Slammed -= OnDoorSlammed; }
            if (mapRoot) { mapRoot.gameObject.SetActive(false); Destroy(mapRoot.gameObject); }
            mapRoot = null; World = null; Player = null; builder = null;
            entities.Clear(); actors.Clear(); enemies.Clear(); actorById.Clear(); enemyById.Clear(); doors.Clear(); pickups.Clear(); retreats.Clear();
        }

        private void SetState(CampaignGameState value) { State = value; StateChanged?.Invoke(); }
        private void RequireConfigured() { if (Campaign == null) throw new InvalidOperationException("Configure the campaign before starting it."); }
        private int IndexOf(string id)
        {
            if (Campaign != null) for (var i = 0; i < Campaign.levels.Length; i++) if (Campaign.levels[i].id == id) return i;
            return -1;
        }
        private static void Validate(CampaignData data)
        {
            if (data == null || data.schema != 1 || string.IsNullOrEmpty(data.id) || data.levels == null || data.levels.Length == 0)
                throw new InvalidDataException("Campaign data is incomplete.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var level in data.levels)
            {
                if (level == null || string.IsNullOrEmpty(level.id) || !ids.Add(level.id) || level.spawn == null || level.exit == null ||
                    !Finite(level.width) || !Finite(level.height) || level.width < 200 || level.height < 200)
                    throw new InvalidDataException("Campaign level geometry or identity is invalid.");
                new CampaignProgress(level);
            }
            if (!string.IsNullOrEmpty(data.firstLevel) && !ids.Contains(data.firstLevel)) throw new InvalidDataException("Campaign firstLevel is missing.");
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static void Position(GameObject value, float x, float y)
        {
            if (!Finite(x) || !Finite(y)) throw new InvalidDataException("Saved position is not finite.");
            var body = value.GetComponent<Rigidbody2D>();
            if (body) body.position = ArkonCoordinates.ToUnity(new Vec2(x, y));
            else CampaignMapBuilder.At(value, x, y);
        }
        private static Vec2 P(CampaignInteraction point) => new Vec2(point.x, point.y);
        private static T[] Safe<T>(T[] values) => values ?? Array.Empty<T>();
        private static string[] Copy(HashSet<string> values) { var result = new string[values.Count]; values.CopyTo(result); Array.Sort(result, StringComparer.Ordinal); return result; }
        private void OnApplicationFocus(bool focused) { if (!focused) Pause(true); }
        private void OnApplicationPause(bool paused) { if (paused) Pause(true); }
        private void OnDestroy() { generation++; Time.timeScale = 1; TearDownMap(); }
    }
}
