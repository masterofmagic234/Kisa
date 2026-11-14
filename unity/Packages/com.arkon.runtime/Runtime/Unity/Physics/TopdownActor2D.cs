using System;
using Arkon.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arkon.Unity
{
    [RequireComponent(typeof(KinematicBodyPort2D), typeof(ActorIdentity2D), typeof(DamageReceiver2D))]
    public sealed class TopdownActor2D : MonoBehaviour
    {
        public bool ReadKeyboardMouse = true;
        public bool InfiniteAmmo = true;
        public bool CombatEnabled = true;
        public Transform VisualPivot;
        public Camera AimCamera;
        public Physics2DPort AssignedWorld;
        private readonly InputLatch input = new InputLatch();
        private readonly TopdownMotor motor = new TopdownMotor();
        private KinematicBodyPort2D body;
        private ActorIdentity2D identity;
        private DamageReceiver2D receiver;
        private Physics2DPort world;
        private ProjectilePool2D projectiles;
        public WeaponModel Weapons { get; private set; }
        public TopdownMotor Motor => motor;
        public Vec2 Position => body.Position;
        // Level ownership remains explicit: pickups, bottles, melee targets and
        // mission facts belong to the world/level port, not a fake global state.
        public event Action<TopdownActor2D> ActionRequested;
        public event Action<TopdownActor2D> ThrowRequested;
        public event Action<TopdownActor2D, WeaponDefinition> MeleeRequested;

        private void Awake()
        {
            body = GetComponent<KinematicBodyPort2D>(); identity = GetComponent<ActorIdentity2D>();
            receiver = GetComponent<DamageReceiver2D>();
            Weapons = new WeaponModel(InfiniteAmmo);
            world = AssignedWorld ? AssignedWorld : SceneObjects.Find<Physics2DPort>(this);
            if (!world)
            {
                var sceneWorld = new GameObject("Scene projectile world");
                SceneManager.MoveGameObjectToScene(sceneWorld, gameObject.scene);
                world = sceneWorld.AddComponent<Physics2DPort>();
            }
            if (!AimCamera) AimCamera = SceneObjects.Find<Camera>(this);
            projectiles = world.GetComponent<ProjectilePool2D>();
            if (!projectiles) projectiles = world.gameObject.AddComponent<ProjectilePool2D>();
            receiver.Health.Death += OnDeath;
        }
        private void OnDestroy() { if (receiver) receiver.Health.Death -= OnDeath; }
        private void OnDeath() { motor.Dead = true; input.Clear(); }
        private void Update()
        {
            if (Time.timeScale <= 0) { input.Clear(); motor.ClearControls(); return; }
            if (!ReadKeyboardMouse) return;
            var movement = new Vec2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.S) ? 1 : 0) - (Input.GetKey(KeyCode.W) ? 1 : 0));
            var aim = motor.AimDirection;
            if (AimCamera)
            {
                var at = AimCamera.ScreenToWorldPoint(Input.mousePosition);
                aim = (ArkonCoordinates.ToSource(at) - Position).Normalized;
            }
            FeedInput(movement, aim, Input.GetMouseButton(0), Input.GetKeyDown(KeyCode.E),
                Input.GetKeyDown(KeyCode.Q), Input.GetKey(KeyCode.LeftShift));
        }
        public void FeedInput(Vec2 movement, Vec2 aim, bool fire, bool action, bool throwing, bool sprint)
        {
            if (Time.timeScale <= 0) { input.Clear(); motor.ClearControls(); return; }
            input.Sample(movement, aim, fire, action, throwing, sprint, CombatEnabled);
        }
        private void FixedUpdate()
        {
            var delta = Time.fixedDeltaTime; motor.CombatEnabled = CombatEnabled;
            Weapons.Tick(delta); var commands = motor.Step(input.Consume(), delta);
            motor.SetResolvedVelocity(body.Move(motor.Velocity, delta));
            if (VisualPivot) VisualPivot.localRotation = Quaternion.Euler(0, 0, ArkonCoordinates.ToUnityAngle(motor.FacingRadians));
            if (commands.FireRequested) Fire();
            if (commands.ActionRequested) ActionRequested?.Invoke(this);
            if (commands.ThrowRequested) ThrowRequested?.Invoke(this);
        }
        private void Fire()
        {
            if (!Weapons.ConsumeShot()) return;
            var data = Weapons.CurrentDefinition;
            if (data.Melee) { MeleeRequested?.Invoke(this, data); return; }
            var origin = WorldCombatQueries.SafeMuzzle(Position, motor.AimDirection, 18, world);
            var volley = new ShotHitCache();
            foreach (var angle in data.SpreadAngles)
            {
                var direction = motor.AimDirection.Rotated(angle);
                var endpoint = origin + direction * data.MaxDistance;
                if (world.Sweep(origin, endpoint, SourceCollisionMasks.Walls, identity.Id, out var wallHit)) endpoint = wallHit.Position;
                projectiles.Spawn(origin, endpoint, data.ProjectileSpeed,
                    SourceCollisionMasks.Walls | SourceCollisionMasks.EnemyHitboxes, identity.Id, 0, hit =>
                    {
                        // Player damage is callback-owned. Direct projectile damage MUST stay zero.
                        if (hit.HasCollider && hit.Hit.DamageableHitbox && volley.TryHit(hit.Hit.ActorId))
                            world.DamageHitbox(hit.Hit, data.Damage, identity.Id);
                    });
            }
        }
        private void OnDisable() { input.Clear(); motor.ClearControls(); }
        private void OnApplicationFocus(bool focused) { if (!focused) { input.Clear(); motor.ClearControls(); } }
    }
}
