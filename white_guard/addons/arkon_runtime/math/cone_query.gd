class_name ArkonConeQuery
extends RefCounted

static func is_in_range(player_position: Vector3, cone_position: Vector3, radius: float = 1.2) -> bool:
    return player_position.distance_to(cone_position) <= radius
