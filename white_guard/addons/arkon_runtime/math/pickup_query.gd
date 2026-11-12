class_name ArkonPickupQuery
extends RefCounted

# Pure pickup-distance queries. It does not change pickup state or scene nodes.
static func is_in_range(player_position: Vector3, pickup_position: Vector3, pickup_radius: float) -> bool:
    return player_position.distance_to(pickup_position) <= pickup_radius
