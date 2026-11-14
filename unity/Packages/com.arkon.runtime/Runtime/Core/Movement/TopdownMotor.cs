using System;

namespace Arkon.Core
{
    public readonly struct MotorActions
    {
        public readonly bool FireRequested;
        public readonly bool ActionRequested;
        public readonly bool ThrowRequested;
        public MotorActions(bool fireRequested, bool actionRequested, bool throwRequested)
        {
            FireRequested = fireRequested;
            ActionRequested = actionRequested;
            ThrowRequested = throwRequested;
        }
    }

    /// <summary>
    /// Movement/input decisions from topdown/level3_player.gd. Values remain
    /// source pixels and radians. The engine adapter must perform the actual
    /// body sweep, then supply its collision-resolved velocity; no transform
    /// integration or simulated collision is hidden in this model.
    /// </summary>
    public sealed class TopdownMotor
    {
        public float MoveSpeed { get; set; } = 120;
        public float SprintMultiplier { get; set; } = 1.18f;
        public float Acceleration { get; set; } = 900;
        public float Friction { get; set; } = 1100;
        public bool ControlsEnabled { get; set; } = true;
        public bool CombatEnabled { get; set; } = true;
        private bool _dead;
        public bool Dead
        {
            get => _dead;
            set
            {
                _dead = value;
                if (value) ClearControls();
            }
        }
        public Vec2 Velocity { get; private set; }
        public Vec2 AimDirection { get; private set; } = Vec2.Right;
        public float FacingRadians { get; private set; }
        public float FlashbangRemaining { get; private set; }
        private bool _canFaceVelocity;
        private float _facingBeforeMove;

        public MotorActions Step(TopdownInput input, float delta)
        {
            if (!Scalar.IsFinite(delta) || delta <= 0) return default;
            _facingBeforeMove = FacingRadians;
            if (input.Aim.LengthSquared > .04f) AimDirection = input.Aim.Normalized;
            FlashbangRemaining = Math.Max(0, FlashbangRemaining - delta);
            _canFaceVelocity = ControlsEnabled && !Dead && FlashbangRemaining <= 0;
            if (!_canFaceVelocity)
            {
                Velocity = Velocity.MoveToward(Vec2.Zero, Friction * delta);
                return default;
            }
            var movement = input.Movement.LimitLength(1);
            var speed = MoveSpeed * (input.SprintHeld ? SprintMultiplier : 1);
            var response = movement.LengthSquared > .01f ? Acceleration : Friction;
            Velocity = Velocity.MoveToward(movement * speed, response * delta);
            UpdateFacing();
            return new MotorActions(CombatEnabled && input.FireHeld,
                input.ActionPressed, CombatEnabled && input.ThrowPressed);
        }

        /// <summary>
        /// Call after the real body move. This preserves source facing when a
        /// wall removes velocity, notably while the actor has combat disabled.
        /// </summary>
        public void SetResolvedVelocity(Vec2 velocity)
        {
            Velocity = velocity;
            if (_canFaceVelocity)
            {
                FacingRadians = _facingBeforeMove;
                UpdateFacing();
            }
        }

        private void UpdateFacing()
        {
            if (CombatEnabled && AimDirection.LengthSquared > .04f)
                FacingRadians = AimDirection.Angle;
            else if (Velocity.LengthSquared > 16)
                FacingRadians = Velocity.Angle;
        }

        public void ClearControls()
        {
            Velocity = Vec2.Zero;
            _canFaceVelocity = false;
        }

        public void ApplyFlashbang(float duration)
        {
            if (!Scalar.IsFinite(duration)) return;
            FlashbangRemaining = Math.Max(FlashbangRemaining, Scalar.Clamp(duration, 0, 1.7f));
            ClearControls();
        }
    }
}
