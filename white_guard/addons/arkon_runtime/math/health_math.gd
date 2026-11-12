class_name ArkonHealthMath
extends RefCounted

static func apply_damage(current_hp: int, damage: int) -> int:
    return max(0, current_hp - damage)
