class_name ArkonMissionProgressQuery
extends RefCounted

# Pure mission-progress checks. State mutation remains in game.gd.
static func is_complete(collected: int, goal_count: int) -> bool:
    return collected == goal_count
