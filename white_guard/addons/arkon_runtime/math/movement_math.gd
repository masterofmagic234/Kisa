class_name ArkonMovementMath
extends RefCounted

# Pure player movement math. It does not move the player or store runtime state.
static func velocity_for_input(player_basis: Basis, move_axis: Vector2, walk_speed: float) -> Vector3:
    var forward := -move_axis.y
    var right := move_axis.x
    var velocity := (
        -player_basis.z * forward
        + player_basis.x * right
    ) * walk_speed
    velocity.y = 0.0
    return velocity
