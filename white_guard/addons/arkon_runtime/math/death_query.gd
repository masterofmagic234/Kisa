class_name ArkonDeathQuery
extends RefCounted

# Pure health-state check. It never mutates HP.
static func is_dead(hp: int) -> bool:
    return hp <= 0
