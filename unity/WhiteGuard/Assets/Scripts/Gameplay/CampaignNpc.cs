using System;
using System.Collections.Generic;
using Arkon.Core;
using Arkon.Unity;
using UnityEngine;

namespace WhiteGuard.Gameplay
{
    /// <summary>
    /// Story actors use the same native body sweeps and projectile world as the player.
    /// Protected actors have physical layer11 hitboxes and real HealthModel damage.
    /// </summary>
    [DefaultExecutionOrder(80)]
    [RequireComponent(typeof(KinematicBodyPort2D), typeof(ActorIdentity2D), typeof(DamageReceiver2D))]
    public sealed class CampaignNpc : MonoBehaviour
    {
        public CampaignActor Definition;
        public CampaignGameLoop Owner;
        public Transform VisualPivot;
        public bool Freed { get; private set; }
        public bool Carried { get; private set; }
        public int RouteIndex { get; private set; }
        public Vec2 Position => body ? body.Position : ArkonCoordinates.ToSource(transform.position);
        public HealthModel Health => receiver.Health;
        public DamageReceiver2D Receiver => receiver;
        public ActorIdentity2D Identity => identity;
        public float FacingRadians { get; private set; }
        public bool Alive => this && isActiveAndEnabled && receiver && !receiver.Health.Dead &&
            (Definition.role != "passenger" || (Owner && Owner.FindActor(Definition.transport) && Owner.FindActor(Definition.transport).Alive));
        public bool CanBeTargeted => Alive && Definition != null && Definition.role != "civilian" && Definition.role != "passenger" && (Definition.role != "hostage" || Freed);
        public bool RouteComplete
        {
            get
            {
                if (!Freed) return false;
                var route = Definition.route ?? Array.Empty<CampaignPoint>();
                return Definition.role != "vehicle" || route.Length == 0 ||
                    (RouteIndex == route.Length - 1 && Vec2.Distance(Position, P(route[RouteIndex])) < 16);
            }
        }
        public event Action<CampaignNpc> Died;
        public event Action<CampaignNpc, Vec2> Fired;
        private KinematicBodyPort2D body;
        private ActorIdentity2D identity;
        private DamageReceiver2D receiver;
        private CircleCollider2D bodyShape, hitbox;
        private Vec2 velocity;
        private float pathTimer, shotCooldown, windup;
        private Vec2 lockedAim;
        private EnemyPort2D aimTarget;
        private IReadOnlyList<Vec2> path = Array.Empty<Vec2>();
        private int pathIndex;

        private void Awake()
        {
            body = GetComponent<KinematicBodyPort2D>(); identity = GetComponent<ActorIdentity2D>();
            receiver = GetComponent<DamageReceiver2D>(); bodyShape = GetComponent<CircleCollider2D>();
            if (Definition == null || !Owner) { enabled = false; return; }
            Freed = Definition.role != "hostage";
            var area = new GameObject("Protected actor hitbox"); area.SetActive(false);
            area.transform.SetParent(transform, false); area.layer = ArkonCoordinates.PlayerHitboxLayer;
            hitbox = area.AddComponent<CircleCollider2D>(); hitbox.isTrigger = true;
            hitbox.radius = (Definition.role == "vehicle" ? 18 : 7) / ArkonCoordinates.PixelsPerUnit;
            area.SetActive(true);
            if (Definition.role == "passenger") { bodyShape.enabled = false; hitbox.enabled = false; }
            receiver.Health.Death += OnDeath;
            shotCooldown = .55f;
        }

        public void Free()
        {
            if (!Alive || Freed) return;
            Freed = true; pathTimer = 0; Owner.NotifyActorFreed(this);
        }

        public void Carry()
        {
            if (!Alive || Definition.role == "vehicle" || Definition.role == "passenger") return;
            Free(); Carried = true; velocity = Vec2.Zero; windup = 0;
            if (bodyShape) bodyShape.enabled = false;
            Owner.RefreshCarrying();
            SynchronizeCarriedPose();
        }

        public void Restore(bool freed, int routeIndex, bool carried = false)
        {
            Freed = freed; Carried = carried; RouteIndex = Math.Max(0, Math.Min(routeIndex, Math.Max(0, (Definition.route?.Length ?? 0) - 1)));
            pathTimer = 0; path = Array.Empty<Vec2>(); velocity = Vec2.Zero;
            if (bodyShape) bodyShape.enabled = !Health.Dead && Definition.role != "passenger" && !Carried;
            if (hitbox) hitbox.enabled = !Health.Dead && Definition.role != "passenger";
            if (Carried) SynchronizeCarriedPose();
        }

        private void FixedUpdate()
        {
            if (!Alive || !Owner || !Owner.GameplayActive || !Owner.World || !Owner.World.NavigationReady) return;
            var dt = Time.fixedDeltaTime;
            if (Carried) { SynchronizeCarriedPose(); return; }
            if (Definition.role == "passenger")
            {
                var transport = Owner.FindActor(Definition.transport);
                if (transport) GetComponent<Rigidbody2D>().position = ArkonCoordinates.ToUnity(transport.Position);
                return;
            }
            shotCooldown = Math.Max(0, shotCooldown - dt);
            if (Definition.role == "hostage" && !Freed) return;
            if (Definition.role == "vehicle") MoveVehicle(dt);
            else if (Definition.role == "civilian") MoveCivilian(dt);
            else MoveFollower(dt);
            if (Definition.role == "ally") Fight(dt);
            var pivot = VisualPivot ? VisualPivot : transform;
            pivot.rotation = Quaternion.Euler(0, 0, ArkonCoordinates.ToUnityAngle(FacingRadians));
        }

        private void LateUpdate()
        {
            if (Carried && Alive && Owner && Owner.GameplayActive) SynchronizeCarriedPose();
        }

        private void SynchronizeCarriedPose()
        {
            if (!Owner || !Owner.Player || !Alive) return;
            // A carried person is constrained to the carrier, not a slow walker.
            // The independent solid circle is disabled; the real damage hitbox
            // remains active inside the carrier's enlarged native footprint.
            var facing = Owner.Player.Motor.FacingRadians;
            var forward = new Vec2((float)Math.Cos(facing), (float)Math.Sin(facing));
            var at = Owner.Player.Position - forward * CampaignActorDimensions.CarriedOffset;
            GetComponent<Rigidbody2D>().position = ArkonCoordinates.ToUnity(at);
            FacingRadians = forward.Angle + Mathf.PI * .5f;
            var pivot = VisualPivot ? VisualPivot : transform;
            pivot.rotation = Quaternion.Euler(0, 0, ArkonCoordinates.ToUnityAngle(FacingRadians));
            Physics2D.SyncTransforms();
        }

        private void MoveFollower(float dt)
        {
            if (!Owner.Player) return;
            var goal = Owner.FollowerPoint(this);
            var distance = Vec2.Distance(Position, goal);
            if (distance < 17) { velocity = velocity.MoveToward(Vec2.Zero, 600 * dt); body.Move(velocity, dt); return; }
            Follow(goal, Definition.speed > 0 ? Definition.speed : 100, dt);
        }

        private void MoveVehicle(float dt)
        {
            var route = Definition.route ?? Array.Empty<CampaignPoint>();
            if (route.Length == 0) return;
            if (!Owner.Player || Vec2.Distance(Position, Owner.Player.Position) > 240 || Owner.HasNearbyThreat(Position, 160))
            { velocity = Vec2.Zero; return; }
            while (RouteIndex < route.Length - 1 && Vec2.Distance(Position, P(route[RouteIndex])) < 16) RouteIndex++;
            var goal = P(route[RouteIndex]);
            if (RouteIndex == route.Length - 1 && Vec2.Distance(Position, goal) < 10) { velocity = Vec2.Zero; return; }
            // Authored road route stays wider than the vehicle's native radius.
            MoveToward(goal, Definition.speed > 0 ? Definition.speed : 64, dt);
        }

        private void MoveCivilian(float dt)
        {
            var threat = Owner.NearestEnemy(Position, 180);
            if (threat && Owner.World.HasLineOfSight(Position, threat.Position))
            {
                var away = (Position - threat.Position).Normalized;
                var goal = Owner.ClampPoint(Position + away * 110);
                Follow(goal, Definition.speed > 0 ? Definition.speed : 84, dt);
                return;
            }
            var route = Definition.route ?? Array.Empty<CampaignPoint>();
            if (route.Length == 0) { velocity = Vec2.Zero; return; }
            var at = P(route[RouteIndex]);
            if (Vec2.Distance(Position, at) < 14) RouteIndex = (RouteIndex + 1) % route.Length;
            Follow(P(route[RouteIndex]), Definition.speed > 0 ? Definition.speed : 42, dt);
        }

        private void Follow(Vec2 goal, float speed, float dt)
        {
            pathTimer -= dt;
            if (Owner.World.IsBodyPathClear(Position, goal)) { MoveToward(goal, speed, dt); return; }
            if (pathTimer <= 0)
            {
                path = Owner.World.Navigation.FindPath(Position, goal); pathIndex = 0; pathTimer = .3f;
            }
            while (pathIndex < path.Count && Vec2.Distance(Position, path[pathIndex]) < 7) pathIndex++;
            if (pathIndex < path.Count) MoveToward(path[pathIndex], speed, dt);
            else { velocity = Vec2.Zero; body.Move(velocity, dt); }
            Owner.OpenDoorsNear(Position);
        }

        private void MoveToward(Vec2 goal, float speed, float dt)
        {
            var difference = goal - Position;
            var wanted = difference.Normalized * Math.Min(speed, difference.Length / Math.Max(dt, .0001f));
            velocity = velocity.MoveToward(wanted, 750 * dt);
            velocity = body.Move(velocity, dt);
            if (velocity.LengthSquared > 10) FacingRadians = velocity.Angle;
        }

        private void Fight(float dt)
        {
            if (windup > 0)
            {
                windup -= dt; FacingRadians = lockedAim.Angle;
                if (windup <= 0 && aimTarget && aimTarget.Model != null && aimTarget.Model.State != EnemyState.Dead)
                {
                    var physics = Owner.World.GetComponent<Physics2DPort>();
                    var origin = WorldCombatQueries.SafeMuzzle(Position, lockedAim, 16, physics);
                    var end = origin + lockedAim * 420;
                    if (physics.Sweep(origin, end, SourceCollisionMasks.Walls, identity.Id, out var wall)) end = wall.Position;
                    Owner.World.GetComponent<ProjectilePool2D>().Spawn(origin, end, 650,
                        SourceCollisionMasks.Walls | SourceCollisionMasks.EnemyHitboxes, identity.Id, 100,
                        _ => { }, ProjectileAppearance.PlayerRound);
                    Owner.World.NotifyNoise(Position); Fired?.Invoke(this, lockedAim);
                }
                return;
            }
            if (shotCooldown > 0) return;
            var enemy = Owner.NearestEnemy(Position, 260);
            if (!enemy || !Owner.World.HasLineOfSight(Position, enemy.Position)) return;
            aimTarget = enemy; lockedAim = (enemy.Position - Position).Normalized;
            windup = .4f; shotCooldown = 1.35f;
        }

        private void OnDeath()
        {
            velocity = Vec2.Zero; windup = 0;
            if (bodyShape) bodyShape.enabled = false;
            if (hitbox) hitbox.enabled = false;
            Died?.Invoke(this);
        }

        private void OnDestroy() { if (receiver) receiver.Health.Death -= OnDeath; }
        private static Vec2 P(CampaignPoint point) => new Vec2(point.x, point.y);
    }
}
