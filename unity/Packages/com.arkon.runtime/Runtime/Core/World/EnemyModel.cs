using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public enum EnemyState { Idle = 0, Alert = 1, Stunned = 2, Dead = 3 }
    public enum EnemyKind { Gunman, Butcher, Melee }

    public readonly struct DutyRegion
    {
        public readonly float X, Y, Width, Height;
        public DutyRegion(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public bool Contains(Vec2 at, float padding)
        {
            return at.X >= X - padding && at.Y >= Y - padding && at.X < X + Width + padding && at.Y < Y + Height + padding;
        }
    }

    public sealed class RaidDuty
    {
        public string Role = "", Side = "", Room = "";
        public DutyRegion Region;
        public IReadOnlyList<Vec2> Approach = Array.Empty<Vec2>();
        public IReadOnlyList<Vec2> Patrol = Array.Empty<Vec2>();
    }

    public readonly struct EnemyNeighbour
    {
        public readonly int Id;
        public readonly Vec2 Position;
        public readonly EnemyState State;
        public EnemyNeighbour(int id, Vec2 position, EnemyState state) { Id = id; Position = position; State = state; }
    }

    public interface IEnemyWorld
    {
        bool Transitioning { get; }
        bool CombatPaused { get; }
        bool NavigationReady { get; }
        bool TargetAlive { get; }
        Vec2 TargetPosition { get; }
        bool HasLineOfSight(Vec2 from, Vec2 to);
        bool IsBodyPathClear(Vec2 from, Vec2 to);
        IReadOnlyList<Vec2> FindPath(Vec2 from, Vec2 to);
        Vec2 NearestNavigationPoint(Vec2 point);
        /// <summary>Interact with closed, non-opening doors within the source-space radius.</summary>
        void OpenNearbyDoors(Vec2 actorPosition, float radius);
        IReadOnlyList<EnemyNeighbour> Neighbours { get; }
    }

    /// <summary>
    /// Decision/state port of topdown/level3_enemy.gd. Set Position from the real body
    /// before Tick; if Tick returns true, the adapter applies Velocity through its
    /// collision controller and reports the resulting Position on the next tick.
    /// Rendering, actual sweeps, animation and global time/pause are engine-owned.
    /// Sense/patrol slots must be exported from Godot name hashes for exact phase parity.
    /// </summary>
    public sealed class EnemyModel
    {
        public int Id { get; }
        public EnemyKind Kind { get; }
        public bool Ranged => Kind == EnemyKind.Gunman;
        public float MoveSpeed => Ranged ? 77 : Kind == EnemyKind.Butcher ? 100 : 93;
        public float VisionRange { get; set; } = 340;
        public float PreferredDistance { get; set; } = 170;
        public float AttackRange { get; set; } = 39;
        public float PatrolRadius { get; set; } = 48;
        public Vec2 Position { get; set; }
        public Vec2 Velocity { get; private set; }
        public Vec2 SpawnPosition { get; private set; }
        public Vec2 LastKnownPosition { get; private set; }
        public float RotationRadians { get; private set; }
        public EnemyState State { get; private set; } = EnemyState.Idle;
        public float AttackCooldown => _attackCooldown;
        public float Windup => _windup;
        public float StunRemaining => _stunTimer;
        public bool SeesTarget => _seesTarget;
        public bool Staging { get; private set; }
        public bool DutyArrived { get; private set; }
        public string RaidRole { get; private set; } = "";
        public string RaidSide { get; private set; } = "";
        public string DutyRoom { get; private set; } = "";
        public DutyRegion Region { get; private set; }
        public event Action<Vec2, Vec2> ShotRequested;
        public event Action<int> MeleeDamageRequested;
        public event Action<float> Stunned;
        public event Action Killed;
        private float _stunTimer, _attackCooldown = .4f, _windup, _senseTimer, _routeTimer, _patrolPhase;
        private bool _seesTarget, _directClear, _hasLastGoal;
        private Vec2 _lockedAim = Vec2.Right, _lastGoal, _patrolTarget;
        private IReadOnlyList<Vec2> _path = Array.Empty<Vec2>();
        private int _pathIndex, _dutyPatrolIndex;
        private readonly int _dutyPatrolSeed;
        private readonly List<Vec2> _stagingPath = new List<Vec2>();
        private readonly List<Vec2> _dutyApproach = new List<Vec2>();
        private readonly List<Vec2> _dutyPatrol = new List<Vec2>();
        private Vec2? _stagingFace;

        public EnemyModel(int id, Vec2 position, EnemyKind kind = EnemyKind.Gunman,
            int sensePhaseSlot = 0, int patrolPhaseSlot = 0, int dutyPatrolSeed = 0)
        {
            Id = id; Kind = kind; Position = SpawnPosition = LastKnownPosition = _patrolTarget = position;
            _senseTimer = PositiveMod(sensePhaseSlot, 10) * .01f;
            _patrolPhase = PositiveMod(patrolPhaseSlot, 100) * .0628f;
            _dutyPatrolSeed = dutyPatrolSeed;
        }

        public bool Tick(float delta, IEnemyWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (delta < 0 || !Scalar.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            if (world.Transitioning || State == EnemyState.Dead) return false;
            // Source staging deliberately precedes the combat gate; the global scene pause still owns time.
            if (Staging) return StageTick(world);
            if (world.CombatPaused || !world.NavigationReady) { Velocity = Vec2.Zero; return false; }
            _attackCooldown = Math.Max(0, _attackCooldown - delta); _routeTimer -= delta;
            if (State == EnemyState.Stunned)
            {
                _stunTimer -= delta; Velocity = Velocity.MoveToward(Vec2.Zero, 800 * delta);
                if (_stunTimer <= 0) State = EnemyState.Alert;
                return true;
            }
            if (!world.TargetAlive) return false;
            if (_dutyApproach.Count > 0) return DutyApproachTick(delta, world);
            _senseTimer -= delta;
            if (_senseTimer <= 0)
            {
                _senseTimer = .1f;
                var localTarget = TargetInDutyArea(world.TargetPosition);
                _seesTarget = localTarget && Vec2.Distance(Position, world.TargetPosition) <= VisionRange && world.HasLineOfSight(Position, world.TargetPosition);
                if (_seesTarget) { State = EnemyState.Alert; LastKnownPosition = world.TargetPosition; }
                else if (RaidRole.Length > 0 && State == EnemyState.Alert && (!localTarget || !TargetInDutyArea(Position))) ReturnToDuty();
            }
            if (_windup > 0)
            {
                _windup -= delta; Velocity = Velocity.MoveToward(Vec2.Zero, 1100 * delta);
                RotationRadians = _lockedAim.Angle;
                if (_windup <= 0) ResolveAttack(world);
            }
            else if (State == EnemyState.Idle)
            {
                if (Vec2.Distance(Position, _patrolTarget) < 8)
                {
                    if (_dutyPatrol.Count > 0)
                    {
                        _dutyPatrolIndex = (_dutyPatrolIndex + 1) % _dutyPatrol.Count;
                        _patrolTarget = _dutyPatrol[_dutyPatrolIndex];
                    }
                    else
                    {
                        _patrolPhase += 1.45f;
                        _patrolTarget = SpawnPosition + new Vec2((float)Math.Cos(_patrolPhase), (float)Math.Sin(_patrolPhase)) * PatrolRadius * .65f;
                    }
                }
                Follow(_patrolTarget, MoveSpeed * .38f, delta, world);
            }
            else
            {
                var distance = Vec2.Distance(Position, LastKnownPosition);
                var aim = (LastKnownPosition - Position).Normalized;
                if (_seesTarget)
                {
                    RotationRadians = aim.Angle;
                    if (Ranged)
                    {
                        if (distance > PreferredDistance + 35) Follow(LastKnownPosition, MoveSpeed, delta, world);
                        else if (distance < PreferredDistance - 55)
                        {
                            if (world.IsBodyPathClear(Position, Position - aim * 35)) Velocity = Velocity.MoveToward(-aim * MoveSpeed * .55f, 750 * delta);
                            else Velocity = Velocity.MoveToward(Vec2.Zero, 950 * delta);
                        }
                        else Velocity = Velocity.MoveToward(Vec2.Zero, 950 * delta);
                    }
                    else Follow(LastKnownPosition, MoveSpeed, delta, world);
                    if (_attackCooldown <= 0 && (Ranged || distance <= AttackRange))
                    {
                        _lockedAim = aim; _windup = Ranged ? .48f : .34f;
                        _attackCooldown = Ranged ? 1.35f : 1.10f;
                    }
                }
                else if (distance < 14)
                {
                    State = EnemyState.Idle; _path = Array.Empty<Vec2>(); _patrolTarget = Position;
                    Velocity = Velocity.MoveToward(Vec2.Zero, 950 * delta);
                }
                else Follow(LastKnownPosition, MoveSpeed * .80f, delta, world);
            }
            SeparateRoomUnits(delta, world);
            if (State == EnemyState.Idle && Velocity.LengthSquared > 10) RotationRadians = Velocity.Angle;
            return true;
        }

        public void HearNoise(Vec2 at)
        {
            if (State == EnemyState.Dead || State == EnemyState.Stunned || _dutyApproach.Count > 0 || !TargetInDutyArea(at)) return;
            if (Vec2.Distance(Position, at) <= 250) { State = EnemyState.Alert; LastKnownPosition = at; _routeTimer = 0; }
        }

        public void Stun(float duration = 3.2f, Vec2 knockback = default)
        {
            if (State == EnemyState.Dead) return;
            State = EnemyState.Stunned; _stunTimer = duration; _windup = 0; Velocity = knockback;
            Stunned?.Invoke(duration);
        }

        public void Kill(Action forceKillHealth = null)
        {
            if (State == EnemyState.Dead) return;
            // Set state before touching health: a health death callback may call Kill again.
            State = EnemyState.Dead; Velocity = Vec2.Zero; _windup = 0;
            forceKillHealth?.Invoke(); Killed?.Invoke();
        }

        public void Stage(IReadOnlyList<Vec2> points, Vec2? faceAt = null)
        {
            _stagingPath.Clear();
            if (points != null) foreach (var point in points) _stagingPath.Add(point);
            _stagingFace = faceAt; Staging = true; State = EnemyState.Idle; _windup = 0;
        }

        public void BeginAssault(Vec2 at)
        {
            Staging = false; _stagingFace = null; _stagingPath.Clear();
            State = EnemyState.Alert; LastKnownPosition = at; _routeTimer = _senseTimer = 0;
            if (RaidRole.Length > 0) { State = EnemyState.Idle; LastKnownPosition = SpawnPosition; }
        }

        public void ConfigureRaidDuty(RaidDuty duty, IEnemyWorld world)
        {
            if (duty == null) throw new ArgumentNullException(nameof(duty));
            if (world == null) throw new ArgumentNullException(nameof(world));
            RaidRole = duty.Role ?? ""; RaidSide = duty.Side ?? ""; DutyRoom = duty.Room ?? ""; Region = duty.Region;
            SnapDutyPoints(duty.Approach, _dutyApproach, world); SnapDutyPoints(duty.Patrol, _dutyPatrol, world);
            _dutyPatrolIndex = PositiveMod(_dutyPatrolSeed, Math.Max(1, _dutyPatrol.Count));
            if (_dutyPatrol.Count > 0) SpawnPosition = _patrolTarget = _dutyPatrol[_dutyPatrolIndex];
            _path = Array.Empty<Vec2>(); _pathIndex = 0; _hasLastGoal = false; DutyArrived = false;
        }

        private void Follow(Vec2 goal, float speed, float delta, IEnemyWorld world)
        {
            if (_routeTimer <= 0)
            {
                _directClear = world.IsBodyPathClear(Position, goal); _routeTimer = .24f;
                if (!_directClear && (!_hasLastGoal || Vec2.Distance(goal, _lastGoal) > 12 || _pathIndex >= _path.Count))
                {
                    _path = world.FindPath(Position, goal) ?? Array.Empty<Vec2>();
                    _pathIndex = 0; _lastGoal = goal; _hasLastGoal = true;
                }
            }
            if (_directClear) { Velocity = Velocity.MoveToward((goal - Position).Normalized * speed, 850 * delta); return; }
            while (_pathIndex < _path.Count && Vec2.Distance(Position, _path[_pathIndex]) < 2.5f) _pathIndex++;
            if (RaidRole.Length > 0)
                while (_pathIndex + 1 < _path.Count && world.IsBodyPathClear(Position, _path[_pathIndex + 1])) _pathIndex++;
            if (_pathIndex >= _path.Count) { Velocity = Velocity.MoveToward(Vec2.Zero, 900 * delta); return; }
            world.OpenNearbyDoors(Position, 34);
            Velocity = Velocity.MoveToward((_path[_pathIndex] - Position).Normalized * speed, 850 * delta);
        }

        private void ResolveAttack(IEnemyWorld world)
        {
            if (State != EnemyState.Alert || !world.TargetAlive) return;
            if (Ranged) ShotRequested?.Invoke(Position + _lockedAim * 16, _lockedAim);
            else
            {
                var offset = world.TargetPosition - Position;
                if (offset.Length <= AttackRange + 4 && Vec2.Dot(offset.Normalized, _lockedAim) > .55f && world.HasLineOfSight(Position, world.TargetPosition))
                    MeleeDamageRequested?.Invoke(22);
            }
        }

        private bool StageTick(IEnemyWorld world)
        {
            while (_stagingPath.Count > 0 && Vec2.Distance(Position, _stagingPath[0]) < 4) _stagingPath.RemoveAt(0);
            if (_stagingPath.Count == 0)
            {
                Velocity = Vec2.Zero; RotationRadians = ((_stagingFace ?? world.TargetPosition) - Position).Angle;
                return false;
            }
            var direction = (_stagingPath[0] - Position).Normalized;
            Velocity = direction * 195; RotationRadians = direction.Angle; return true;
        }

        private bool DutyApproachTick(float delta, IEnemyWorld world)
        {
            while (_dutyApproach.Count > 0 && Vec2.Distance(Position, _dutyApproach[0]) < 13)
            {
                _dutyApproach.RemoveAt(0); _routeTimer = 0; _path = Array.Empty<Vec2>(); _pathIndex = 0;
            }
            if (_dutyApproach.Count == 0) { DutyArrived = true; ReturnToDuty(); Velocity = Vec2.Zero; return false; }
            Follow(_dutyApproach[0], MoveSpeed, delta, world); SeparateRoomUnits(delta, world);
            if (Velocity.LengthSquared > 4) RotationRadians = Velocity.Angle;
            return true;
        }

        private bool TargetInDutyArea(Vec2 at) => RaidRole.Length == 0 || Region.Contains(at, RaidRole == "kitchen" ? 72 : 42);
        private void ReturnToDuty()
        {
            State = EnemyState.Idle; _seesTarget = false; _windup = 0; _path = Array.Empty<Vec2>();
            _pathIndex = 0; _routeTimer = 0; _hasLastGoal = false;
            _patrolTarget = _dutyPatrol.Count == 0 ? SpawnPosition : _dutyPatrol[_dutyPatrolIndex];
        }

        private static void SnapDutyPoints(IReadOnlyList<Vec2> input, List<Vec2> output, IEnemyWorld world)
        {
            output.Clear(); if (input == null) return;
            foreach (var point in input)
            {
                var snapped = world.NearestNavigationPoint(point);
                if (output.Count == 0 || Vec2.Distance(output[output.Count - 1], snapped) > 1) output.Add(snapped);
            }
        }

        private void SeparateRoomUnits(float delta, IEnemyWorld world)
        {
            if (RaidRole.Length == 0 || Velocity.LengthSquared < 4) return;
            var away = Vec2.Zero;
            if (world.Neighbours != null)
                foreach (var other in world.Neighbours)
                {
                    if (other.Id == Id || other.State == EnemyState.Dead) continue;
                    var offset = Position - other.Position; var distance = offset.Length;
                    if (distance > .1f && distance < 23) away += offset / distance * (23 - distance) / 23;
                }
            if (away.LengthSquared < .001f) return;
            var desired = Velocity + away * MoveSpeed * .8f;
            if (world.IsBodyPathClear(Position, Position + desired.Normalized * 14)) Velocity = Velocity.MoveToward(desired, 480 * delta);
        }

        private static int PositiveMod(int value, int divisor) => (value % divisor + divisor) % divisor;
    }
}
