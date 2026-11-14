namespace Arkon.Core
{
    public readonly struct TopdownInput
    {
        public readonly Vec2 Movement;
        public readonly Vec2 Aim;
        public readonly bool FireHeld;
        public readonly bool ActionPressed;
        public readonly bool ThrowPressed;
        public readonly bool SprintHeld;

        public TopdownInput(Vec2 movement, Vec2 aim, bool fireHeld = false,
            bool actionPressed = false, bool throwPressed = false, bool sprintHeld = false)
        {
            Movement = movement;
            Aim = aim;
            FireHeld = fireHeld;
            ActionPressed = actionPressed;
            ThrowPressed = throwPressed;
            SprintHeld = sprintHeld;
        }
    }

    /// <summary>
    /// Update-to-physics adapter for level3_player.gd's set_input/clear-edge
    /// contract. Several input frames retain a press; several physics ticks
    /// consume it only once. Held movement/fire remain available each tick.
    /// </summary>
    public sealed class InputLatch
    {
        private Vec2 _movement;
        private Vec2 _aim = Vec2.Right;
        private bool _fireHeld;
        private bool _actionPending;
        private bool _throwPending;
        private bool _sprintHeld;

        public void Sample(Vec2 movement, Vec2 aim, bool fireHeld = false,
            bool actionPressed = false, bool throwPressed = false,
            bool sprintHeld = false, bool combatEnabled = true)
        {
            _movement = movement.LimitLength(1);
            if (aim.LengthSquared > .04f) _aim = aim.Normalized;
            _fireHeld = fireHeld && combatEnabled;
            _actionPending |= actionPressed;
            if (combatEnabled) _throwPending |= throwPressed;
            else _throwPending = false;
            _sprintHeld = sprintHeld;
        }

        public TopdownInput Consume()
        {
            var result = new TopdownInput(_movement, _aim, _fireHeld,
                _actionPending, _throwPending, _sprintHeld);
            _actionPending = false;
            _throwPending = false;
            return result;
        }

        public void Clear()
        {
            _movement = Vec2.Zero;
            _fireHeld = false;
            _actionPending = false;
            _throwPending = false;
            _sprintHeld = false;
            // Source clear_controls keeps the last useful aim direction.
        }
    }
}
