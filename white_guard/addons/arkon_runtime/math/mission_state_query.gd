class_name ArkonMissionStateQuery
extends RefCounted

# Pure mission-state checks. Runtime state remains owned by game.gd.
static func is_finished(mission_complete: bool, mission_failed: bool) -> bool:
    return mission_complete or mission_failed

static func is_active(mission_complete: bool, mission_failed: bool) -> bool:
    return not is_finished(mission_complete, mission_failed)
