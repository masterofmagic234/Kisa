extends Node
class_name ArkonWeaponComponent
## Arkon's per-weapon ammunition/cooldown ownership with an injected catalog.
signal weapon_changed(id: StringName, ammo: int)
const Data = preload("res://addons/arkon_runtime/combat/weapon_data.gd")
var weapons: Dictionary = {}
var ammo_by_weapon: Dictionary = {}
var current_weapon: StringName = &""
var current_data: ArkonWeaponData
var ammo := 0
var cooldown := 0.0
var infinite_ammo := false

func configure(catalog: Dictionary) -> void:
    weapons = catalog.duplicate()
    ammo_by_weapon.clear()
    for id in weapons: ammo_by_weapon[id] = 0
    current_weapon = &""
    current_data = null

func equip(id: StringName, additional_ammo: int = 0) -> bool:
    if not weapons.get(id) is Data: return false
    current_weapon = id
    current_data = weapons[id]
    ammo = maxi(int(ammo_by_weapon.get(id, 0)) + maxi(additional_ammo, 0), 0) if current_data.consumes_ammo else 0
    ammo_by_weapon[id] = ammo
    cooldown = 0.0
    weapon_changed.emit(id, ammo)
    return true

func tick(delta: float) -> void:
    cooldown = maxf(0.0, cooldown - delta)

func can_fire() -> bool:
    return current_data != null and cooldown <= 0 and (not current_data.consumes_ammo or infinite_ammo or ammo > 0)

func consume_shot() -> bool:
    if not can_fire(): return false
    if current_data.consumes_ammo and not infinite_ammo:
        ammo = maxi(ammo - 1, 0)
        ammo_by_weapon[current_weapon] = ammo
    cooldown = maxf(current_data.fire_interval, 0.01)
    weapon_changed.emit(current_weapon, ammo)
    return true
