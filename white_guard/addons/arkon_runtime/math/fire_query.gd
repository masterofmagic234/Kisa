class_name ArkonFireQuery
extends RefCounted

# Pure fire eligibility check. It never consumes ammo or changes cooldowns.
static func can_fire(mission_complete: bool, mission_failed: bool, cooldown: float, ammo: int) -> bool:
    if mission_complete or mission_failed:
        return false
    if cooldown > 0.0:
        return false
    return ammo > 0
