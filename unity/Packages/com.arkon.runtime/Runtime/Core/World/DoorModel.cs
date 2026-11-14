using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public readonly struct DoorSlamHit
    {
        public readonly float StunSeconds;
        public readonly Vec2 Knockback;
        public DoorSlamHit(Vec2 knockback) { StunSeconds = 2.6f; Knockback = knockback; }
    }

    /// <summary>State policy from topdown/level3_door.gd; adapter owns overlap tests and deferred collider writes.</summary>
    public sealed class DoorModel
    {
        public const float OpenAngle = (float)Math.PI * .5f;
        public float OpenSpeed { get; set; } = 12;
        public float SlamSpeed { get; set; } = 30;
        public float CloseSpeed { get; set; } = 7.5f;
        public float AutoCloseDelay { get; set; } = 3;
        public Vec2 Position { get; set; }
        public float GlobalRotation { get; set; }
        public float Rotation { get; private set; }
        public bool Locked { get; set; }
        public bool IsOpen { get; private set; }
        public bool IsOpening { get; private set; }
        public bool IsSlammed { get; private set; }
        public bool ColliderEnabled { get; private set; } = true;
        public bool HitMonitoring { get; private set; }
        private readonly float _closedRotation;
        private float _targetRotation, _closeTimer = -1, _openDirection = 1;
        private readonly HashSet<int> _slamHits = new HashSet<int>();
        public event Action Opened;
        public event Action Slammed;
        public event Action Closed;

        public DoorModel(Vec2 position, bool locked = false, float globalRotation = 0, float closedPivotRotation = 0)
        {
            Position = position; Locked = locked; GlobalRotation = globalRotation;
            Rotation = _closedRotation = _targetRotation = closedPivotRotation;
        }

        public bool Interact(Vec2 interactorPosition, bool dynamicSlam = false)
        {
            if (Locked || IsOpening) return false;
            if (IsOpen) { _closeTimer = AutoCloseDelay; return true; }
            var normal = new Vec2(0, 1).Rotated(GlobalRotation).Normalized;
            var offset = interactorPosition - Position;
            if (offset.LengthSquared <= .001f) offset = normal;
            _openDirection = Vec2.Dot(offset.Normalized, normal) >= 0 ? -1 : 1;
            _targetRotation = _closedRotation + _openDirection * OpenAngle;
            ColliderEnabled = false; IsOpening = true; IsSlammed = dynamicSlam;
            _slamHits.Clear(); _closeTimer = -1;
            if (dynamicSlam) { HitMonitoring = true; Slammed?.Invoke(); }
            return true;
        }

        public void Tick(float delta, Func<bool> doorwayBlocked)
        {
            if (delta < 0 || !Scalar.IsFinite(delta)) throw new ArgumentOutOfRangeException(nameof(delta));
            if (IsOpening)
            {
                Rotation = RotateToward(Rotation, _targetRotation, (IsSlammed ? SlamSpeed : OpenSpeed) * delta);
                if (Approximately(Rotation, _targetRotation))
                {
                    Rotation = _targetRotation; IsOpening = false; IsOpen = true; IsSlammed = false;
                    _slamHits.Clear(); ColliderEnabled = false; HitMonitoring = false;
                    _closeTimer = AutoCloseDelay; Opened?.Invoke();
                }
            }
            else if (IsOpen)
            {
                _closeTimer -= delta;
                if (_closeTimer <= 0) BeginClose();
            }
            else if (!Approximately(Rotation, _closedRotation))
            {
                Rotation = RotateToward(Rotation, _closedRotation, CloseSpeed * delta);
                if (Approximately(Rotation, _closedRotation))
                {
                    Rotation = _closedRotation;
                    if (doorwayBlocked != null && doorwayBlocked())
                    {
                        _targetRotation = _closedRotation + _openDirection * OpenAngle;
                        IsOpening = true; IsOpen = false; _closeTimer = -1;
                        return;
                    }
                    ColliderEnabled = true; Closed?.Invoke();
                }
            }
        }

        public bool TrySlamHit(int enemyId, Vec2 enemyPosition, bool enemyDead, out DoorSlamHit hit)
        {
            hit = default;
            if (!IsSlammed || enemyDead || !_slamHits.Add(enemyId)) return false;
            hit = new DoorSlamHit((enemyPosition - Position).Normalized * 170);
            return true;
        }

        private void BeginClose()
        {
            IsOpening = IsOpen = IsSlammed = false; _slamHits.Clear();
            _targetRotation = _closedRotation; _closeTimer = -1;
            ColliderEnabled = false; HitMonitoring = false;
        }

        private static bool Approximately(float a, float b) => Math.Abs(a - b) < Math.Max(.00001f, .00001f * Math.Abs(a));
        private static float RotateToward(float from, float to, float step)
        {
            var difference = (to - from) % ((float)Math.PI * 2);
            if (difference > Math.PI) difference -= (float)Math.PI * 2;
            if (difference < -Math.PI) difference += (float)Math.PI * 2;
            return Math.Abs(difference) <= step ? to : from + Math.Sign(difference) * step;
        }
    }
}
