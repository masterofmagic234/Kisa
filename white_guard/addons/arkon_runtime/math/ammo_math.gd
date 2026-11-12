class_name ArkonAmmoMath
extends RefCounted

static func consume_one(current_ammo: int) -> int:
    return current_ammo - 1
