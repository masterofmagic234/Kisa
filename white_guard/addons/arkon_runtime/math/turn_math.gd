class_name ArkonTurnMath
extends RefCounted

# Pure player turn calculation. The game loop remains responsible for applying the rotation.
static func turn_amount(move_axis_x: float, turn_speed: float, delta: float) -> float:
    return -move_axis_x * turn_speed * delta
