using System;
using System.Collections.Generic;
using Arkon.Core;

namespace Arkon.Parity
{
    /// <summary>Behavioural cases for source-world policies; these do not validate Unity physics.</summary>
    public static class WorldCases
    {
        public static void Run(Action<bool, string> check)
        {
            NavigationCases(check); DoorCases(check); PoolCases(check); ProjectileCases(check);
            CombatQueryCases(check); PickupCases(check); EnemyCases(check);
        }

        private static bool Near(float a, float b, float tolerance = .0001f) => Math.Abs(a - b) <= tolerance;
        private static bool Near(Vec2 a, Vec2 b) => Vec2.Distance(a, b) < .0001f;

        private sealed class Sampler : INavigationSampler
        {
            public Func<Vec2, bool> Blocked = _ => false;
            public bool WrongRadius;
            public bool IsBlocked(Vec2 point, float radius) { WrongRadius |= radius != 8; return Blocked(point); }
        }

        private static void NavigationCases(Action<bool, string> check)
        {
            var grid = new NavigationGrid(); var sampler = new Sampler { Blocked = p => p.X == 18 && p.Y == 18 };
            var generation = grid.BeginBuild(0, 0, 3, 3);
            check(!grid.Ready && grid.FindPath(new Vec2(6, 18), new Vec2(30, 18)).Count == 0, "Unfinished navigation cannot route actors");
            check(!grid.SampleRows(generation, 1, sampler) && grid.Samples == 3, "Sliced navigation samples one requested row");
            check(grid.SampleRows(generation, 8, sampler) && grid.Walkable == 8 && !sampler.WrongRadius, "Navigation samples source radius8 and finishes only after all rows");
            var path = grid.FindPath(new Vec2(6, 18), new Vec2(30, 18));
            check(path.Count == 5 && Near(path[0], new Vec2(6, 18)) && Near(path[path.Count - 1], new Vec2(30, 18)), "A* routes around a blocked centre, retaining endpoints");
            var cardinal = true; var avoidsWall = true;
            for (var i = 0; i < path.Count; i++)
            {
                avoidsWall &= !grid.IsSolid(new GridCell((int)Math.Floor(path[i].X / 12), (int)Math.Floor(path[i].Y / 12)));
                if (i > 0) cardinal &= Near(Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y), 12);
            }
            check(cardinal && avoidsWall, "A* never cuts diagonally through wall corners");
            var oldGeneration = grid.BeginBuild(0, 0, 3, 3); var next = grid.BeginBuild(0, 0, 1, 1);
            check(!grid.SampleRows(oldGeneration, 3, sampler) && grid.Samples == 0, "Obsolete navigation generation cannot populate new grid");
            grid.SampleRows(next, 1, new Sampler());
            check(grid.Ready && grid.FindPath(new Vec2(1, 1), new Vec2(2, 2)).Count == 1, "A* same-cell route has one source cell centre");
            grid.BeginBuild(-7, -7, 15, 15); grid.SampleRows(grid.Generation, 15, new Sampler { Blocked = p => !(p.X == -66 && p.Y == -66) });
            check(grid.NearestCell(Vec2.Zero).Equals(new GridCell(-6, -6)), "Nearest free search reaches source six-cell square boundary");
            grid.BeginBuild(-7, -7, 15, 15); grid.SampleRows(grid.Generation, 15, new Sampler { Blocked = p => !(p.X == 90 && p.Y == 90) });
            check(grid.IsSolid(grid.NearestCell(Vec2.Zero)), "Nearest free search does not expand beyond six cells");
            grid.BeginBuild(-1, -1, 3, 3); grid.SampleRows(grid.Generation, 3, new Sampler());
            check(grid.NearestCell(new Vec2(-.01f, -.01f)).Equals(new GridCell(-1, -1)), "Navigation floors negative source coordinates");
            grid.BeginBuild(new Vec2(24, 36));
            check(grid.MinX == -12 && grid.MinY == -12 && grid.Width == 26 && grid.Height == 35, "World-grid margins retain the source24/32 cells");
        }

        private static void DoorCases(Action<bool, string> check)
        {
            var door = new DoorModel(Vec2.Zero); var opened = 0; var closed = 0; var slammed = 0;
            door.Opened += () => opened++; door.Closed += () => closed++; door.Slammed += () => slammed++;
            check(door.ColliderEnabled && !door.IsOpen, "Door begins solid in its authored closed pose");
            check(door.Interact(new Vec2(0, 10), true) && !door.ColliderEnabled && door.HitMonitoring && slammed == 1, "Slam disables solidity and starts the hit window");
            check(door.TrySlamHit(7, new Vec2(10, 0), false, out var hit) && Near(hit.StunSeconds, 2.6f) && Near(hit.Knockback, new Vec2(170, 0)), "Slam applies source stun2.6 and knockback170");
            check(!door.TrySlamHit(7, new Vec2(10, 0), false, out _) && !door.TrySlamHit(8, Vec2.Zero, true, out _), "One slam does not hit the same or dead enemy twice");
            door.Tick(.1f, () => false);
            check(door.IsOpen && !door.IsOpening && !door.IsSlammed && !door.HitMonitoring && Near(door.Rotation, -(float)Math.PI / 2) && opened == 1, "Door opens away from the actor to exactly90 degrees");
            door.Tick(3, () => false);
            check(!door.IsOpen && !door.ColliderEnabled, "Auto-close keeps the moving leaf non-solid");
            door.Tick(.3f, () => true);
            check(door.IsOpening && !door.ColliderEnabled && closed == 0, "A blocked doorway reopens instead of enabling collision over an actor");
            door.Tick(.2f, () => true); door.Tick(3, () => false); door.Tick(.3f, () => false);
            check(door.ColliderEnabled && Near(door.Rotation, 0) && opened == 2 && closed == 1, "Clear doorway eventually restores solidity exactly at closed pose");
            door.Locked = true;
            check(!door.Interact(Vec2.Zero) && door.ColliderEnabled, "Locked door cannot be opened");
        }

        private sealed class PooledItem { public bool Active; public Vec2 Position; }
        private static void PoolCases(Action<bool, string> check)
        {
            var made = 0;
            var pool = new BoundedPool<PooledItem>(() => { made++; return new PooledItem(); },
                activate: item => item.Active = true, deactivate: item => { item.Active = false; item.Position = Vec2.Zero; });
            check(made == 24 && pool.Capacity == 24 && pool.ActiveCount == 0, "Scene pool warms24 disabled instances");
            var held = new List<PooledItem>();
            for (var i = 0; i < 64; i++) held.Add(pool.Acquire());
            check(made == 64 && pool.Capacity == 64 && pool.ActiveCount == 64 && pool.Acquire() == null, "Pool stops at64 without allocating an overflow projectile");
            held[0].Position = new Vec2(20, 20);
            check(pool.Release(held[0]) && !pool.Release(held[0]) && !pool.Release(new PooledItem()), "Pool rejects duplicate and foreign release");
            check(ReferenceEquals(pool.Acquire(), held[0]) && Near(held[0].Position, Vec2.Zero), "Released instances are reset and reused in LIFO order");
            pool.ReleaseAll();
            check(pool.ActiveCount == 0 && pool.AvailableCount == 64 && held.TrueForAll(x => !x.Active), "Scene cleanup releases every active object");
        }

        private sealed class BulletWorld : IProjectileWorld
        {
            public int Sweeps, Damage, LastMask, LastIgnore;
            public bool Hitbox = true;
            public float WallAt = 50;
            public bool Sweep(Vec2 from, Vec2 to, int sourceMask, int ignoredActorId, out ProjectileHit hit)
            {
                Sweeps++; LastMask = sourceMask; LastIgnore = ignoredActorId;
                hit = new ProjectileHit(10, 7, new Vec2(WallAt, 0), Hitbox);
                return from.X <= WallAt && to.X >= WallAt;
            }
            public void DamageHitbox(ProjectileHit hit, int damage, int sourceActorId) { Damage += damage; }
        }

        private static void ProjectileCases(Action<bool, string> check)
        {
            var world = new BulletWorld(); var bullet = new ProjectileModel(); var impacts = 0; var recycled = 0; var position = Vec2.Zero;
            bullet.Setup(Vec2.Zero, new Vec2(650, 0), 650, impact => { impacts++; position = impact.Position; }, true, 9, 123, 8, _ => recycled++);
            bullet.Tick(.2f, world); bullet.Tick(.2f, world);
            check(world.Sweeps == 1 && world.LastMask == 9 && world.LastIgnore == 123 && Near(position, new Vec2(50, 0)), "Fast projectile resolves the entire swept segment and excludes its actor");
            check(world.Damage == 8 && impacts == 1 && recycled == 1 && !bullet.Active, "Enemy hit damage, impact and recycle happen once");
            var playerWorld = new BulletWorld(); var cache = new ShotHitCache(); var volleyDamage = 0;
            for (var pellet = 0; pellet < 5; pellet++)
            {
                var shot = new ProjectileModel();
                shot.Setup(Vec2.Zero, new Vec2(650, 0), 650, impact => { if (impact.HasCollider && cache.TryHit(impact.Hit.ActorId)) volleyDamage += 100; }, true, 5, 3, 0);
                shot.Tick(.2f, playerWorld);
            }
            check(playerWorld.Damage == 0 && volleyDamage == 100 && cache.Count == 1, "Player volley owns callback damage and one shared cache, never five direct hits");
            var wallWorld = new BulletWorld { Hitbox = false }; var wallBullet = new ProjectileModel();
            wallBullet.Setup(Vec2.Zero, new Vec2(650, 0), 650, _ => { }, true, 1, 2, 8); wallBullet.Tick(.2f, wallWorld);
            check(wallWorld.Damage == 0, "A solid wall never receives actor hitbox damage");
            var expired = new ProjectileModel(); var noCollider = false;
            expired.Setup(Vec2.Zero, new Vec2(650, 0), 650, impact => noCollider = !impact.HasCollider, true, 5);
            var before = world.Sweeps; expired.Tick(2, world);
            check(noCollider && Near(expired.Position, Vec2.Zero) && world.Sweeps == before, "Lifetime expiry resolves current position before a new sweep");
            var callbackFailure = new ProjectileModel(); var failureRecycles = 0;
            callbackFailure.Setup(Vec2.Zero, Vec2.Zero, 0, _ => throw new InvalidOperationException("deliberate test"), recycle: _ => failureRecycles++);
            try { callbackFailure.Tick(.01f, world); } catch (InvalidOperationException) { }
            check(failureRecycles == 1 && callbackFailure.ImpactStarted && Near(callbackFailure.Lifetime, .28f), "Failed impact callback cannot leak an active pool lease");
        }

        private static void CombatQueryCases(Action<bool, string> check)
        {
            var world = new BulletWorld { WallAt = 5, Hitbox = false };
            check(Near(WorldCombatQueries.SafeMuzzle(Vec2.Zero, Vec2.Right, 18, world), new Vec2(4, 0)), "Muzzle stays one source pixel on the shooter's side of a thin wall");
            var targets = new[] { new CombatTarget(1, new Vec2(20, 0), EnemyState.Alert), new CombatTarget(2, new Vec2(10, 0), EnemyState.Stunned), new CombatTarget(3, new Vec2(-1, 0), EnemyState.Stunned) };
            check(WorldCombatQueries.SelectExecutionTarget(Vec2.Zero, Vec2.Right, 32, targets, (_, __) => true)?.Id == 2, "Execution selects a visible stunned target in the forward sector");
            check(WorldCombatQueries.SelectExecutionTarget(Vec2.Zero, Vec2.Right, 32, targets, (_, __) => false) == null, "Execution cannot cross a wall");
            check(WorldCombatQueries.SelectThrowTarget(Vec2.Zero, Vec2.Right, targets, (_, __) => true)?.Id == 2, "Bottle chooses nearest living target inside34 degrees");
            check(WorldCombatQueries.SelectBatTarget(Vec2.Zero, Vec2.Right, targets, (_, __) => true)?.Id == 2, "Bat chooses nearest physics-overlap candidate within70 degrees");
        }

        private sealed class PickupActor : IPickupActor
        {
            public int Id => 42;
            public bool Dead { get; set; }
            public bool CombatEnabled { get; set; } = true;
            public int Health { get; set; } = 100;
            public int MaximumHealth => 100;
        }

        private static void PickupCases(Action<bool, string> check)
        {
            var actor = new PickupActor(); var pickup = new PickupModel("ammo"); var events = 0;
            pickup.Collected += (kind, id) => { events++; actor.Health = Math.Min(100, actor.Health + WorldCombatQueries.MedkitHealing); };
            var overlap = new IPickupActor[] { actor };
            check(pickup.Automatic && pickup.Kind == "medkit" && !pickup.Tick(.01f, overlap, _ => true) && !pickup.Consumed, "Legacy ammo becomes a medkit and full health does not consume it");
            actor.Health = 50;
            check(!pickup.Tick(.05f, overlap, _ => true), "Medkit retry respects the .12 second schedule");
            check(pickup.Tick(.08f, overlap, _ => true) && actor.Health == 85 && events == 1, "A player wounded while overlapping can collect the medkit without re-entering");
            check(!pickup.Collect(actor, true) && events == 1, "A medkit cannot emit collection twice");
            var manual = new PickupModel("pistol");
            check(!manual.Automatic && !manual.Tick(1, overlap, _ => true) && !manual.Collect(actor, false), "Weapon pickup remains manual and obeys line of sight");
        }

        private sealed class EnemyWorld : IEnemyWorld
        {
            public bool Transitioning { get; set; }
            public bool CombatPaused { get; set; }
            public bool NavigationReady { get; set; } = true;
            public bool TargetAlive { get; set; } = true;
            public Vec2 TargetPosition { get; set; } = new Vec2(170, 0);
            public bool Visible = true, Clear = true;
            public int DoorChecks, PathQueries;
            public IReadOnlyList<EnemyNeighbour> Neighbours { get; set; } = Array.Empty<EnemyNeighbour>();
            public bool HasLineOfSight(Vec2 from, Vec2 to) => Visible;
            public bool IsBodyPathClear(Vec2 from, Vec2 to) => Clear;
            public IReadOnlyList<Vec2> FindPath(Vec2 from, Vec2 to) { PathQueries++; return new[] { from, to }; }
            public Vec2 NearestNavigationPoint(Vec2 point) => point;
            public void OpenNearbyDoors(Vec2 actorPosition, float radius) { DoorChecks++; }
        }

        private static void EnemyCases(Action<bool, string> check)
        {
            var world = new EnemyWorld(); var gunman = new EnemyModel(1, Vec2.Zero); var shots = 0; var shotDirection = Vec2.Zero; var shotOrigin = Vec2.Zero;
            gunman.ShotRequested += (origin, direction) => { shots++; shotDirection = direction; shotOrigin = origin; };
            gunman.Tick(.4f, world);
            check(gunman.State == EnemyState.Alert && Near(gunman.Windup, .48f) && Near(gunman.MoveSpeed, 77), "Gunman senses target and starts source .48 second windup after initial cooldown");
            world.TargetPosition = new Vec2(0, 170); gunman.Tick(.25f, world); gunman.Tick(.24f, world);
            check(shots == 1 && Near(shotDirection, Vec2.Right) && Near(shotOrigin, new Vec2(16, 0)), "Ranged attack uses locked aim through windup, even after target moves");
            gunman.Stun(1, new Vec2(170, 0)); gunman.HearNoise(new Vec2(10, 10)); gunman.Tick(.1f, world);
            check(gunman.State == EnemyState.Stunned && Near(gunman.Velocity.Length, 90) && gunman.Windup == 0, "Stun cancels windup, ignores noise and damps knockback at800px/s2");
            gunman.Tick(.91f, world);
            check(gunman.State == EnemyState.Alert, "Stun expiry returns an enemy to alert state");
            var deaths = 0; gunman.Killed += () => deaths++; gunman.Kill(() => gunman.Kill()); gunman.Kill();
            check(gunman.State == EnemyState.Dead && deaths == 1 && Near(gunman.Velocity, Vec2.Zero), "Enemy marks DEAD before a re-entrant health callback");
            var melee = new EnemyModel(2, Vec2.Zero, EnemyKind.Butcher); var meleeDamage = 0;
            melee.MeleeDamageRequested += amount => meleeDamage += amount;
            world.TargetPosition = new Vec2(38, 0); melee.Tick(.4f, world); world.TargetPosition = new Vec2(40, 0); melee.Tick(.35f, world);
            check(meleeDamage == 22 && Near(melee.MoveSpeed, 100), "Butcher melee windup uses source22 damage and +4 resolution margin");
            var missed = new EnemyModel(3, Vec2.Zero, EnemyKind.Melee); var missedDamage = 0; missed.MeleeDamageRequested += amount => missedDamage += amount;
            world.TargetPosition = new Vec2(38, 0); missed.Tick(.4f, world); world.TargetPosition = new Vec2(-38, 0); missed.Tick(.35f, world);
            check(missedDamage == 0 && Near(missed.MoveSpeed, 93), "A target behind locked melee aim is not hit");
            var paused = new EnemyModel(4, Vec2.Zero); world.NavigationReady = false;
            check(!paused.Tick(.5f, world) && Near(paused.Velocity, Vec2.Zero), "Combat AI cannot move before navigation is ready");
            paused.Stage(new[] { Vec2.Zero, new Vec2(20, 0) }); world.CombatPaused = true;
            check(paused.Tick(.01f, world) && Near(paused.Velocity, new Vec2(195, 0)), "Staging follows source195 speed before the combat gate");
            paused.Position = new Vec2(20, 0);
            check(!paused.Tick(.01f, world) && Near(paused.Velocity, Vec2.Zero), "Completed staging holds instead of advancing into combat");
            world.NavigationReady = true; world.CombatPaused = false; world.TargetPosition = new Vec2(900, 900);
            var duty = new EnemyModel(5, Vec2.Zero);
            duty.ConfigureRaidDuty(new RaidDuty { Role = "guard", Region = new DutyRegion(0, 0, 50, 50), Patrol = new[] { Vec2.Zero } }, world);
            duty.HearNoise(new Vec2(93, 0));
            check(duty.State == EnemyState.Idle, "Noise outside a room's42px margin does not summon its patrol");
            duty.HearNoise(new Vec2(90, 0));
            check(duty.State == EnemyState.Alert, "Noise inside the room margin alerts its patrol");
            duty.Tick(.1f, world);
            check(duty.State == EnemyState.Idle, "Room patrol returns to duty when target is outside its area");
            var pathEnemy = new EnemyModel(6, Vec2.Zero); world.Clear = false; world.Visible = false;
            pathEnemy.HearNoise(new Vec2(200, 0)); pathEnemy.Tick(.01f, world); pathEnemy.Tick(.1f, world);
            check(world.PathQueries == 1 && world.DoorChecks >= 1, "Blocked-body pursuit uses navigation and interacts with nearby closed doors without per-tick path rebuild");
        }
    }
}
