using System;
using System.Collections;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>
    /// Scene-local physics/navigation host for the released Level3 policies.
    /// An authored level owner supplies geometry, bounds, target and mission gates.
    /// This component does not create a map or advance campaign/story state.
    /// </summary>
    [RequireComponent(typeof(Physics2DPort), typeof(ProjectilePool2D))]
    public sealed class TopdownWorld2D : MonoBehaviour, INavigationSampler
    {
        public TopdownActor2D Target;
        public Vector2 WorldSizePixels;
        public bool BuildNavigationOnStart;
        public int RowsPerSlice = 8;
        public bool CombatPaused;
        public bool Transitioning;
        public bool LevelComplete;
        public bool HasBottle = true;
        public NavigationGrid Navigation { get; } = new NavigationGrid();
        public bool NavigationReady => isActiveAndEnabled && navigationCommitted && Navigation.Ready;
        public bool TargetAlive => Target && Target.isActiveAndEnabled && targetHealth != null && !targetHealth.Dead && !Target.Motor.Dead;
        public bool IsCombatPaused => CombatPaused || !TargetAlive || LevelComplete;
        public event Action<PickupPort2D, TopdownActor2D> PickupApplied;
        public event Action<string, Vec2> CombatFeedback;
        private Physics2DPort projectileWorld;
        private ProjectilePool2D projectiles;
        private HealthModel targetHealth;
        private WeaponModel targetWeapons;
        private TopdownActor2D boundTarget;
        private KinematicBodyPort2D boundBody;
        private TargetPickupActor pickupActor;
        private readonly List<EnemyPort2D> enemies = new List<EnemyPort2D>();
        private readonly List<DoorPort2D> doors = new List<DoorPort2D>();
        private readonly List<PickupPort2D> pickups = new List<PickupPort2D>();
        private readonly List<EnemyNeighbour> neighbours = new List<EnemyNeighbour>();
        private readonly List<RaycastHit2D> rayHits = new List<RaycastHit2D>(16);
        private readonly List<RaycastHit2D> bodyHits = new List<RaycastHit2D>(16);
        private readonly List<Collider2D> overlapHits = new List<Collider2D>(16);
        private Coroutine buildRoutine;
        private int buildGeneration;
        private bool navigationCommitted;
        private bool started;
        private bool navigationRequested;

        private void Awake()
        {
            projectileWorld = GetComponent<Physics2DPort>();
            projectiles = GetComponent<ProjectilePool2D>();
        }

        private void Start()
        {
            started = true;
            var candidate = Target ? Target : SceneObjects.Find<TopdownActor2D>(this);
            BindTarget(candidate);
            if (BuildNavigationOnStart) BuildNavigation(WorldSizePixels);
        }

        private void OnEnable()
        {
            if (projectiles) projectiles.enabled = true;
            if (started)
            {
                BindTarget(Target ? Target : SceneObjects.Find<TopdownActor2D>(this));
                if (navigationRequested || BuildNavigationOnStart) BuildNavigation(WorldSizePixels);
            }
        }

        private void OnDisable()
        {
            buildGeneration++; navigationCommitted = false;
            if (buildRoutine != null) { StopCoroutine(buildRoutine); buildRoutine = null; }
            UnbindTarget();
            if (projectiles) projectiles.enabled = false;
        }

        public void BindTarget(TopdownActor2D actor)
        {
            UnbindTarget(); Target = null;
            if (!actor) return;
            if (actor.gameObject.scene != gameObject.scene || !actor.isActiveAndEnabled)
            { Debug.LogError("TopdownWorld2D target must be active in the same scene.", this); return; }
            Target = actor;
            var receiver = actor.GetComponent<DamageReceiver2D>();
            targetHealth = receiver.Health; targetWeapons = actor.Weapons;
            if (targetWeapons == null)
            { Debug.LogError("Bind target after the actor Awake has completed.", this); Target = null; targetHealth = null; return; }
            EnsurePlayerHitbox(actor);
            pickupActor = new TargetPickupActor(actor, receiver);
            boundTarget = actor; boundBody = actor.GetComponent<KinematicBodyPort2D>();
            actor.TransitionBlockedQuery = () => !this || !isActiveAndEnabled || Transitioning || Target != actor;
            actor.CanSpawnPlayerShotQuery = () => this && isActiveAndEnabled && Target == actor && CanAttack;
            actor.ActionRequested += OnAction;
            actor.ThrowRequested += OnThrow;
            actor.MeleeRequested += OnMelee;
            targetWeapons.Changed += OnWeaponChanged;
            if (boundBody) boundBody.BlockingCollisions += OnBlockingCollisions;
        }

        private void UnbindTarget()
        {
            if (boundTarget)
            {
                boundTarget.ActionRequested -= OnAction; boundTarget.ThrowRequested -= OnThrow; boundTarget.MeleeRequested -= OnMelee;
            }
            if (boundBody) boundBody.BlockingCollisions -= OnBlockingCollisions;
            if (targetWeapons != null) targetWeapons.Changed -= OnWeaponChanged;
            targetWeapons = null; targetHealth = null; pickupActor = null; boundTarget = null; boundBody = null;
        }

        private static void EnsurePlayerHitbox(TopdownActor2D actor)
        {
            foreach (var collider in actor.GetComponentsInChildren<Collider2D>(true))
                if (collider.gameObject.layer == ArkonCoordinates.PlayerHitboxLayer) return;
            var hitbox = new GameObject("PlayerHitbox"); hitbox.SetActive(false);
            hitbox.transform.SetParent(actor.transform, false); hitbox.layer = ArkonCoordinates.PlayerHitboxLayer;
            var shape = hitbox.AddComponent<CircleCollider2D>();
            shape.radius = 7 / ArkonCoordinates.PixelsPerUnit; shape.isTrigger = true;
            hitbox.SetActive(true);
        }

        public void BuildNavigation(Vector2 sizePixels)
        {
            if (!isActiveAndEnabled) return;
            if (sizePixels.x <= 0 || sizePixels.y <= 0)
            { Debug.LogError("Supply the authored positive map bounds before building navigation.", this); return; }
            WorldSizePixels = sizePixels; buildGeneration++; navigationCommitted = false;
            navigationRequested = true;
            if (buildRoutine != null) StopCoroutine(buildRoutine);
            var ownerGeneration = buildGeneration;
            var gridGeneration = Navigation.BeginBuild(new Vec2(sizePixels.x, sizePixels.y));
            buildRoutine = StartCoroutine(BuildNavigationSliced(ownerGeneration, gridGeneration));
        }

        private IEnumerator BuildNavigationSliced(int ownerGeneration, int gridGeneration)
        {
            // Synchronize authored colliders, then preserve the source render+physics yield.
            yield return null;
            yield return new WaitForFixedUpdate();
            while (isActiveAndEnabled && ownerGeneration == buildGeneration)
            {
                while (Time.timeScale <= 0)
                {
                    yield return null;
                    if (!isActiveAndEnabled || ownerGeneration != buildGeneration) yield break;
                }
                Physics2D.SyncTransforms();
                if (Navigation.SampleRows(gridGeneration, Math.Max(1, RowsPerSlice), this))
                { navigationCommitted = true; buildRoutine = null; yield break; }
                yield return null;
                yield return new WaitForFixedUpdate();
            }
        }

        public bool IsBlocked(Vec2 center, float radius)
        {
            overlapHits.Clear();
            Physics2D.OverlapCircle(ArkonCoordinates.ToUnity(center), radius / ArkonCoordinates.PixelsPerUnit, WallFilter(), overlapHits);
            foreach (var hit in overlapHits)
                if (Local(hit) && !hit.GetComponentInParent<DoorPort2D>()) return true;
            return false;
        }

        public bool HasLineOfSight(Vec2 from, Vec2 to) => !WallRay(from, to, null, out _);

        internal bool WallRay(Vec2 from, Vec2 to, Collider2D ignore, out Vec2 position)
        {
            position = to; var origin = ArkonCoordinates.ToUnity(from); var motion = ArkonCoordinates.ToUnity(to - from);
            if (motion.sqrMagnitude <= 1e-12f) return false;
            rayHits.Clear(); Physics2D.Raycast(origin, motion.normalized, WallFilter(), rayHits, motion.magnitude);
            var nearest = float.PositiveInfinity; var found = false;
            foreach (var hit in rayHits)
            {
                if (!Local(hit.collider) || hit.collider == ignore || hit.distance >= nearest) continue;
                found = true; nearest = hit.distance; position = ArkonCoordinates.ToSource(hit.point);
            }
            return found;
        }

        public bool IsBodyPathClear(Vec2 from, Vec2 to)
        {
            var origin = ArkonCoordinates.ToUnity(from); var motion = ArkonCoordinates.ToUnity(to - from);
            if (motion.sqrMagnitude <= 1e-12f)
            {
                overlapHits.Clear(); Physics2D.OverlapCircle(origin, 7.8f / ArkonCoordinates.PixelsPerUnit, WallFilter(), overlapHits);
                foreach (var collider in overlapHits) if (Local(collider)) return false;
                return true;
            }
            bodyHits.Clear();
            Physics2D.CircleCast(origin, 7.8f / ArkonCoordinates.PixelsPerUnit, motion.normalized, WallFilter(), bodyHits, motion.magnitude);
            foreach (var hit in bodyHits)
                if (Local(hit.collider) && hit.distance / motion.magnitude <= .999f) return false;
            return true;
        }

        internal void Register(EnemyPort2D enemy) { if (enemy && enemy.gameObject.scene == gameObject.scene && !enemies.Contains(enemy)) enemies.Add(enemy); }
        internal void Unregister(EnemyPort2D enemy) { enemies.Remove(enemy); }
        internal void Register(DoorPort2D door) { if (door && door.gameObject.scene == gameObject.scene && !doors.Contains(door)) doors.Add(door); }
        internal void Unregister(DoorPort2D door) { doors.Remove(door); }
        internal void Register(PickupPort2D pickup) { if (pickup && pickup.gameObject.scene == gameObject.scene && !pickups.Contains(pickup)) pickups.Add(pickup); }
        internal void Unregister(PickupPort2D pickup) { pickups.Remove(pickup); }

        internal IEnemyWorld EnemyWorld(EnemyPort2D owner) => new EnemyBinding(this, owner);
        internal IPickupActor PickupActor(TopdownActor2D actor) => actor && actor == Target && pickupActor != null ? pickupActor : null;
        private bool CanSimulateCombat => isActiveAndEnabled && Time.timeScale > 0 && !Transitioning && !IsCombatPaused;
        internal bool CanAttack => CanSimulateCombat && Target.CombatEnabled;

        internal void FireEnemy(EnemyPort2D enemy, Vec2 direction)
        {
            if (!CanSimulateCombat || !enemy || enemy.Model == null || enemy.Model.State == EnemyState.Dead || !projectiles) return;
            var origin = SafeMuzzle(enemy.Position, direction, 16); var endpoint = origin + direction.Normalized * WorldCombatQueries.EnemyBulletRange;
            if (WallRay(origin, endpoint, null, out var wall)) endpoint = wall;
            projectiles.Spawn(origin, endpoint, WorldCombatQueries.EnemyBulletSpeed,
                SourceCollisionMasks.Walls | SourceCollisionMasks.PlayerHitboxes, enemy.ActorId,
                WorldCombatQueries.EnemyBulletDamage, _ => { });
            CombatFeedback?.Invoke("enemy_shot", origin);
        }

        internal void MeleeTarget(EnemyPort2D enemy, int damage)
        {
            if (CanSimulateCombat && enemy && enemy.Model != null && enemy.Model.State != EnemyState.Dead) targetHealth.ApplyDamage(damage, enemy.ActorId);
        }

        internal void ApplyPickup(PickupPort2D pickup, TopdownActor2D actor)
        {
            if (!actor || actor != Target || targetHealth == null) return;
            var position = pickup.Position;
            switch (pickup.Model.Kind)
            {
                case "pistol": actor.Weapons.Equip("pistol", 12); break;
                case "shotgun": actor.Weapons.Equip("shotgun", 6); break;
                case "bat": actor.Weapons.Equip("bat"); break;
                case "bottle": HasBottle = true; break;
                case "medkit": targetHealth.Heal(WorldCombatQueries.MedkitHealing); break;
            }
            PickupApplied?.Invoke(pickup, actor); CombatFeedback?.Invoke("pickup", position);
        }

        public void NotifyNoise(Vec2 at)
        {
            foreach (var enemy in enemies.ToArray()) if (enemy && enemy.Model != null) enemy.Model.HearNoise(at);
        }

        private void OnWeaponChanged(string id, int ammo)
        {
            // Equip resets cooldown to zero; ConsumeShot is the only Changed event with a positive cooldown.
            if (CanAttack && targetWeapons.Cooldown > 0) NotifyNoise(SafeMuzzle(Target.Position, Target.Motor.AimDirection, 18));
        }

        private void OnBlockingCollisions(IReadOnlyList<Collider2D> contacts, float impactSpeed)
        {
            var actor = boundTarget;
            // Source _check_door_slam runs after the body move, outside the world combat gate.
            if (!actor || actor != Target || !isActiveAndEnabled || Transitioning || Time.timeScale <= 0
                || !actor.Motor.ControlsEnabled || actor.Motor.Dead || actor.Motor.FlashbangRemaining > 0
                || impactSpeed < actor.Motor.MoveSpeed * .70f) return;
            foreach (var collider in contacts)
            {
                if (!Local(collider)) continue;
                var door = collider.GetComponentInParent<DoorPort2D>();
                if (!door) continue;
                door.Interact(actor.Position, true);
                return; // Only the first collided door is slammed, as in the source.
            }
        }

        private List<CombatTarget> Targets()
        {
            var result = new List<CombatTarget>(enemies.Count);
            foreach (var enemy in enemies) if (enemy && enemy.Model != null && enemy.isActiveAndEnabled)
                result.Add(new CombatTarget(enemy.ActorId, enemy.Position, enemy.Model.State));
            return result;
        }

        private EnemyPort2D EnemyById(int id)
        {
            foreach (var enemy in enemies) if (enemy && enemy.ActorId == id) return enemy;
            return null;
        }

        private void OnAction(TopdownActor2D actor)
        {
            if (actor != Target || !CanSimulateCombat) return;
            // Source disables combat interactions while unarmed, but ordinary doors remain usable.
            if (actor.CombatEnabled)
            {
                var execution = WorldCombatQueries.SelectExecutionTarget(actor.Position, actor.Motor.AimDirection,
                    actor.Weapons.CurrentDefinition.ActionRange, Targets(), HasLineOfSight);
                if (execution.HasValue) { EnemyById(execution.Value.Id)?.Kill(); return; }
                PickupPort2D nearestPickup = null; var pickupDistance = 32f;
                foreach (var pickup in pickups)
                {
                    if (!pickup || pickup.Model == null || pickup.Model.Consumed || pickup.Model.Automatic) continue;
                    var distance = Vec2.Distance(actor.Position, pickup.Position);
                    if (distance <= pickupDistance && HasLineOfSight(actor.Position, pickup.Position)) { nearestPickup = pickup; pickupDistance = distance; }
                }
                if (nearestPickup) { nearestPickup.TryCollect(actor); return; }
            }
            DoorPort2D nearestDoor = null; var doorDistance = 41f;
            foreach (var door in doors)
            {
                if (!door || door.Model == null || door.Model.IsOpen || door.Model.IsOpening) continue;
                var distance = Vec2.Distance(actor.Position, door.Position);
                if (distance <= doorDistance && !WallRay(actor.Position, door.Position, door.SolidCollider, out _)) { nearestDoor = door; doorDistance = distance; }
            }
            if (nearestDoor) nearestDoor.Interact(actor.Position);
        }

        private void OnMelee(TopdownActor2D actor, WeaponDefinition weapon)
        {
            if (actor != Target || !CanAttack) return;
            var direction = actor.Motor.AimDirection; var origin = SafeMuzzle(actor.Position, direction, 18);
            overlapHits.Clear();
            var filter = new ContactFilter2D { useLayerMask = true, useTriggers = false, layerMask = 1 << ArkonCoordinates.BodiesLayer };
            Physics2D.OverlapCircle(ArkonCoordinates.ToUnity(origin + direction * 14), 72 / ArkonCoordinates.PixelsPerUnit, filter, overlapHits);
            var candidates = new List<CombatTarget>(16); var count = 0;
            foreach (var collider in overlapHits)
            {
                if (!Local(collider) || collider.GetComponentInParent<TopdownActor2D>() == actor) continue;
                if (++count > 16) break;
                var enemy = collider.GetComponentInParent<EnemyPort2D>();
                if (enemy && enemy.Model != null) candidates.Add(new CombatTarget(enemy.ActorId, enemy.Position, enemy.Model.State));
            }
            var target = WorldCombatQueries.SelectBatTarget(actor.Position, direction, candidates, HasLineOfSight);
            if (target.HasValue)
            {
                EnemyById(target.Value.Id)?.ReceiveHit(weapon.Damage, actor.GetComponent<ActorIdentity2D>().Id);
                CombatFeedback?.Invoke("hit", target.Value.Position); NotifyNoise(actor.Position);
            }
        }

        private void OnThrow(TopdownActor2D actor)
        {
            if (actor != Target || !CanAttack || !HasBottle) return;
            HasBottle = false; var direction = actor.Motor.AimDirection;
            var origin = SafeMuzzle(actor.Position, direction, 12); NotifyNoise(origin);
            var selected = WorldCombatQueries.SelectThrowTarget(origin, direction, Targets(), HasLineOfSight);
            var endpoint = selected.HasValue ? selected.Value.Position : origin + direction * WorldCombatQueries.BottleRange;
            if (!selected.HasValue && WallRay(origin, endpoint, null, out var wall)) endpoint = wall;
            projectiles.Spawn(origin, endpoint, WorldCombatQueries.BottleSpeed,
                SourceCollisionMasks.Walls | SourceCollisionMasks.EnemyHitboxes, actor.GetComponent<ActorIdentity2D>().Id, 0,
                impact =>
                {
                    if (!this || !isActiveAndEnabled) return;
                    if (impact.HasCollider && impact.Hit.DamageableHitbox) EnemyById(impact.Hit.ActorId)?.Stun(WorldCombatQueries.BottleStun);
                    CombatFeedback?.Invoke("glass", impact.Position);
                });
        }

        private Vec2 SafeMuzzle(Vec2 center, Vec2 direction, float distance)
        {
            var normal = direction.Normalized; var end = center + normal * distance;
            return WallRay(center, end, null, out var wall) ? wall - normal : end;
        }

        private bool Local(Collider2D collider) => collider && collider.gameObject.scene == gameObject.scene;
        private static ContactFilter2D WallFilter() => new ContactFilter2D { useLayerMask = true, useTriggers = false, layerMask = 1 << ArkonCoordinates.WallsLayer };

        private sealed class TargetPickupActor : IPickupActor
        {
            private readonly TopdownActor2D actor; private readonly DamageReceiver2D receiver;
            public TargetPickupActor(TopdownActor2D actor, DamageReceiver2D receiver) { this.actor = actor; this.receiver = receiver; }
            public int Id => receiver.ActorId;
            public bool Dead => !actor || receiver.Health.Dead || actor.Motor.Dead;
            public bool CombatEnabled => actor && actor.CombatEnabled;
            public int Health => receiver.Health.Current;
            public int MaximumHealth => receiver.Health.Maximum;
        }

        private sealed class EnemyBinding : IEnemyWorld
        {
            private readonly TopdownWorld2D world;
            private readonly EnemyPort2D owner;
            public EnemyBinding(TopdownWorld2D world, EnemyPort2D owner) { this.world = world; this.owner = owner; }
            public bool Transitioning => !world || !world.isActiveAndEnabled || world.Transitioning;
            public bool CombatPaused => !world || world.IsCombatPaused;
            public bool NavigationReady => world && world.NavigationReady;
            public bool TargetAlive => world && world.TargetAlive;
            public Vec2 TargetPosition => TargetAlive ? world.Target.Position : owner.Position;
            public bool HasLineOfSight(Vec2 from, Vec2 to) => world.HasLineOfSight(from, to);
            public bool IsBodyPathClear(Vec2 from, Vec2 to) => world.IsBodyPathClear(from, to);
            public IReadOnlyList<Vec2> FindPath(Vec2 from, Vec2 to) => world.Navigation.FindPath(from, to);
            public Vec2 NearestNavigationPoint(Vec2 point) => world.Navigation.CellCenter(world.Navigation.NearestCell(point));
            public void OpenNearbyDoors(Vec2 at, float radius)
            {
                foreach (var door in world.doors)
                    if (door && door.Model != null && !door.Model.IsOpen && !door.Model.IsOpening && Vec2.Distance(at, door.Position) < radius) door.Interact(at);
            }
            public IReadOnlyList<EnemyNeighbour> Neighbours
            {
                get
                {
                    world.neighbours.Clear();
                    foreach (var enemy in world.enemies)
                        if (enemy && enemy.Model != null && enemy.isActiveAndEnabled) world.neighbours.Add(new EnemyNeighbour(enemy.ActorId, enemy.Position, enemy.Model.State));
                    return world.neighbours;
                }
            }
        }
    }
}
