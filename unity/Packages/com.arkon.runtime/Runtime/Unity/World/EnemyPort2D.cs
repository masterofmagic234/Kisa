using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>Level3 body/hitbox/decision adapter. Sprite animation remains an authored prefab concern.</summary>
    [RequireComponent(typeof(KinematicBodyPort2D), typeof(ActorIdentity2D))]
    public sealed class EnemyPort2D : MonoBehaviour
    {
        public TopdownWorld2D AssignedWorld;
        public EnemyKind Kind = EnemyKind.Gunman;
        public bool AIEnabled = true;
        public float VisionRangePixels = 340, PreferredDistancePixels = 170, AttackRangePixels = 39, PatrolRadiusPixels = 48;
        public int SourceSenseSlot, SourcePatrolSlot, SourceDutySeed;
        public Transform VisualPivot;
        public DamageReceiver2D HitboxReceiver;
        public CircleCollider2D HitboxCollider;
        public EnemyModel Model { get; private set; }
        public Vec2 Position => body ? body.Position : ArkonCoordinates.ToSource(transform.position);
        public int ActorId => identity.Id;
        public event Action<EnemyPort2D> Killed;
        public event Action<EnemyPort2D, float> Stunned;
        private TopdownWorld2D world;
        private KinematicBodyPort2D body;
        private ActorIdentity2D identity;
        private CircleCollider2D bodyCollider;
        private HealthModel health;
        private IEnemyWorld binding;
        private bool started;
        private RaidDuty pendingDuty;
        private IReadOnlyList<Vec2> pendingStage;
        private Vec2? pendingFace;

        private void Awake()
        {
            body = GetComponent<KinematicBodyPort2D>(); identity = GetComponent<ActorIdentity2D>(); bodyCollider = GetComponent<CircleCollider2D>();
            if (!HitboxReceiver)
            {
                var hitbox = new GameObject("EnemyHitbox"); hitbox.SetActive(false);
                hitbox.transform.SetParent(transform, false); hitbox.layer = ArkonCoordinates.EnemyHitboxLayer;
                HitboxCollider = hitbox.AddComponent<CircleCollider2D>(); HitboxCollider.isTrigger = true;
                HitboxCollider.radius = 7 / ArkonCoordinates.PixelsPerUnit;
                HitboxReceiver = hitbox.AddComponent<DamageReceiver2D>();
                // Configure before activation/Awake; the released enemy scene has 1HP and no invulnerability.
                HitboxReceiver.Maximum = 1; HitboxReceiver.Invulnerability = 0;
                hitbox.SetActive(true);
            }
            if (!HitboxCollider) HitboxCollider = HitboxReceiver.GetComponent<CircleCollider2D>();
            health = HitboxReceiver.Health;
            if (HitboxReceiver.gameObject.scene != gameObject.scene || HitboxReceiver.transform == transform || !HitboxReceiver.transform.IsChildOf(transform)
                || health.Maximum != 1 || health.InvulnerabilityDuration != 0 || !HitboxCollider)
            {
                Debug.LogError("Enemy hitbox must be this actor's child with source 1HP/0 invulnerability and a circle collider.", this);
                enabled = false; return;
            }
            HitboxReceiver.gameObject.layer = ArkonCoordinates.EnemyHitboxLayer;
            HitboxCollider.gameObject.layer = ArkonCoordinates.EnemyHitboxLayer;
            HitboxCollider.isTrigger = true; HitboxCollider.radius = 7 / ArkonCoordinates.PixelsPerUnit;
            health.Death += OnHealthDeath;
        }

        private void Start() { started = true; Initialize(); }
        private void OnEnable() { if (started) Initialize(); }

        private void Initialize()
        {
            var candidate = AssignedWorld ? AssignedWorld : SceneObjects.Find<TopdownWorld2D>(this);
            if (!candidate || !candidate.isActiveAndEnabled || candidate.gameObject.scene != gameObject.scene)
            { Debug.LogError("EnemyPort2D needs an active TopdownWorld2D in the same scene.", this); return; }
            world = candidate; binding = world.EnemyWorld(this);
            if (Model == null)
            {
                Model = new EnemyModel(ActorId, Position, Kind, SourceSenseSlot, SourcePatrolSlot, SourceDutySeed);
                Model.VisionRange = VisionRangePixels; Model.PreferredDistance = PreferredDistancePixels;
                Model.AttackRange = AttackRangePixels; Model.PatrolRadius = PatrolRadiusPixels;
                Model.ShotRequested += OnShot; Model.MeleeDamageRequested += OnMelee;
                Model.Stunned += OnStunned; Model.Killed += OnKilled;
            }
            bodyCollider.enabled = Model.State != EnemyState.Dead; HitboxCollider.enabled = Model.State != EnemyState.Dead;
            world.Register(this);
            if (pendingDuty != null && world.NavigationReady) { Model.ConfigureRaidDuty(pendingDuty, binding); pendingDuty = null; }
            if (pendingStage != null) { Model.Stage(pendingStage, pendingFace); pendingStage = null; }
            if (health.Dead && Model.State != EnemyState.Dead) Model.Kill();
        }

        private void FixedUpdate()
        {
            if (!AIEnabled || Model == null || !world || !world.isActiveAndEnabled || Time.timeScale <= 0) return;
            if (pendingDuty != null)
            {
                if (!world.NavigationReady) return;
                Model.ConfigureRaidDuty(pendingDuty, binding); pendingDuty = null;
            }
            Model.Position = Position;
            if (Model.Tick(Time.fixedDeltaTime, binding))
            {
                Model.SetResolvedVelocity(body.Move(Model.Velocity, Time.fixedDeltaTime));
                Model.Position = Position;
            }
            var pivot = VisualPivot ? VisualPivot : transform;
            if (pivot == transform) pivot.rotation = Quaternion.Euler(0, 0, ArkonCoordinates.ToUnityAngle(Model.RotationRadians));
            else pivot.localRotation = Quaternion.Euler(0, 0, ArkonCoordinates.ToUnityAngle(Model.RotationRadians));
        }

        public void ConfigureRaidDuty(RaidDuty duty)
        {
            pendingDuty = duty ?? throw new ArgumentNullException(nameof(duty));
            if (Model != null && world && world.NavigationReady) { Model.ConfigureRaidDuty(duty, binding); pendingDuty = null; }
        }

        public void Stage(IReadOnlyList<Vec2> points, Vec2? faceAt = null)
        {
            pendingStage = points ?? Array.Empty<Vec2>(); pendingFace = faceAt;
            if (Model != null) { Model.Stage(pendingStage, pendingFace); pendingStage = null; }
        }

        public void BeginAssault(Vec2 at) { if (Model != null) Model.BeginAssault(at); }
        public void ReceiveHit(int amount, int sourceId = 0) { if (Model != null && Model.State != EnemyState.Dead) health.ApplyDamage(amount, sourceId); }
        public void Stun(float duration, Vec2 knockback = default) { if (Model != null) Model.Stun(duration, knockback); }
        public void Kill() { if (Model != null) Model.Kill(() => health.ForceKill()); }
        private void OnShot(Vec2 origin, Vec2 direction) { if (world) world.FireEnemy(this, direction); }
        private void OnMelee(int damage) { if (world) world.MeleeTarget(this, damage); }
        private void OnStunned(float duration) { Stunned?.Invoke(this, duration); }
        private void OnHealthDeath() { if (Model != null) Model.Kill(); }
        private void OnKilled()
        {
            if (bodyCollider) bodyCollider.enabled = false;
            if (HitboxCollider) HitboxCollider.enabled = false;
            Killed?.Invoke(this);
        }
        private void OnDisable()
        {
            if (world) world.Unregister(this);
            if (bodyCollider) bodyCollider.enabled = false;
            if (HitboxCollider) HitboxCollider.enabled = false;
        }
        private void OnDestroy()
        {
            if (health != null) health.Death -= OnHealthDeath;
            if (Model != null)
            {
                Model.ShotRequested -= OnShot; Model.MeleeDamageRequested -= OnMelee;
                Model.Stunned -= OnStunned; Model.Killed -= OnKilled;
            }
        }
    }
}
