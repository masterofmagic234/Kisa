extends CharacterBody2D
class_name ArkonLevel3Player

const ArkonHealthComponent = preload("res://addons/arkon_runtime/combat/health_component.gd")
const ArkonHitboxComponent = preload("res://addons/arkon_runtime/combat/hitbox_component.gd")
const ArkonLevel3WeaponComponent = preload("res://addons/arkon_runtime/topdown/weapon_component.gd")
const ArkonWeaponData = preload("res://addons/arkon_runtime/combat/weapon_data.gd")
const HeroVisual = preload("res://addons/arkon_runtime/topdown/fixtures/hero_visual.gd")

signal fire_requested(origin: Vector2, direction: Vector2, weapon: StringName)
signal action_requested
signal throw_requested(origin: Vector2, direction: Vector2)
signal died
signal weapon_changed(weapon: StringName, ammo: int)
signal edge_inputs_consumed
var owned_weapons: Array[StringName] = [&"pistol"]

@export var move_speed: float = 120.0
@export var sprint_multiplier: float = 1.18
@export var acceleration: float = 900.0
@export var friction: float = 1100.0
@export var max_health: int = 100

var current_weapon: StringName = &"pistol"
var ammo: int = 12
var health: int = 100
var throwable: StringName = &"bottle"

var is_dead: bool = false
var controls_enabled: bool = true
var combat_enabled: bool = true

var _move_input: Vector2 = Vector2.ZERO
var _aim_input: Vector2 = Vector2.RIGHT
var _fire_held: bool = false
var _action_just_pressed: bool = false
var _throw_just_pressed: bool = false
var _sprint_held: bool = false

var _damage_cooldown: float = 0.0
var flashbang_time := 0.0
@onready var _visual: HeroVisual = %Visual
@onready var _health_component: ArkonHealthComponent = %Health
@onready var _weapon_component: ArkonLevel3WeaponComponent = %ArkonLevel3WeaponComponent
@onready var _hitbox_component: ArkonHitboxComponent = %Hitbox

func _ready() -> void:
    collision_layer = 2
    collision_mask = 1 | 2
    z_index = 20
    if _health_component != null:
        _health_component.health_changed.connect(_on_health_changed)
        _health_component.died.connect(_on_health_component_died)
        health = _health_component.current_health
        max_health = _health_component.max_health
    if _weapon_component != null:
        _weapon_component.weapon_changed.connect(_on_weapon_component_changed)
        current_weapon = _weapon_component.current_weapon
        ammo = _weapon_component.ammo
    texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
    _setup_visual()
    var listener := AudioListener2D.new()
    add_child(listener)
    listener.make_current()

func set_input(
    movement: Vector2,
    aim: Vector2,
    fire_held: bool,
    action_just_pressed: bool,
    throw_just_pressed: bool,
    sprint_held: bool
) -> void:
    _move_input = movement.limit_length(1.0)
    if aim.length_squared() > 0.04:
        _aim_input = aim.normalized()
    _fire_held = fire_held and combat_enabled
    _action_just_pressed = action_just_pressed
    _throw_just_pressed = throw_just_pressed and combat_enabled
    _sprint_held = sprint_held

func _physics_process(delta: float) -> void:
    if get_node("/root/SceneFlow").is_loading_gameplay(): return
    if _weapon_component != null:
        _weapon_component.tick(delta)
    var damage_was_active := _damage_cooldown > 0.0
    _damage_cooldown = maxf(0.0, _damage_cooldown - delta)
    if damage_was_active and _damage_cooldown <= 0.0 and not is_dead and _visual != null:
        _visual.modulate = Color.WHITE

    flashbang_time = maxf(0.0, flashbang_time-delta)
    if not controls_enabled or is_dead or flashbang_time > 0:
        velocity = velocity.move_toward(Vector2.ZERO, friction * delta)
        move_and_slide()
        _update_visual_motion(delta)
        _clear_edge_inputs()
        return

    var target_speed := move_speed
    if _sprint_held:
        target_speed *= sprint_multiplier

    var desired_velocity := _move_input * target_speed
    var response := acceleration if _move_input.length_squared() > 0.01 else friction
    velocity = velocity.move_toward(desired_velocity, response * delta)
    var impact_speed := velocity.length()
    move_and_slide()
    _check_door_slam(impact_speed)

    if combat_enabled and _aim_input.length_squared() > 0.04:
        rotation = _aim_input.angle()
    elif velocity.length_squared() > 16:
        rotation = velocity.angle()

    _update_visual_motion(delta)

    if combat_enabled and _fire_held and _weapon_component != null and _weapon_component.can_fire():
        _request_fire()

    if _action_just_pressed:
        action_requested.emit()

    if combat_enabled and _throw_just_pressed and throwable != &"":
        var origin := global_position + _aim_input * 12.0
        throwable = &""
        throw_requested.emit(origin, _aim_input)

    _clear_edge_inputs()

func _check_door_slam(impact_speed: float) -> void:
    if impact_speed < move_speed * 0.70:
        return

    for index in range(get_slide_collision_count()):
        var collision := get_slide_collision(index)
        var collider := collision.get_collider()
        if collider is AnimatableBody2D:
            var door_node: Node = collider.get_parent()
            if door_node is ArkonLevel3Door:
                (door_node as ArkonLevel3Door).interact(global_position, true)
                return

func _request_fire() -> void:
    if not combat_enabled or _weapon_component == null or not _weapon_component.consume_shot():
        return
    current_weapon = _weapon_component.current_weapon
    ammo = _weapon_component.ammo
    _play_fire_animation()
    var origin := global_position + _aim_input * 18.0
    fire_requested.emit(origin, _aim_input, current_weapon)

func _clear_edge_inputs() -> void:
    var consumed := _action_just_pressed or _throw_just_pressed
    _action_just_pressed = false
    _throw_just_pressed = false
    if consumed: edge_inputs_consumed.emit()

func equip_weapon(weapon: StringName, new_ammo: int = 0) -> void:
    if not combat_enabled or _weapon_component == null:
        return
    if not owned_weapons.has(weapon): owned_weapons.append(weapon)
    _weapon_component.equip(weapon, new_ammo)
    current_weapon = _weapon_component.current_weapon
    ammo = _weapon_component.ammo
    _refresh_visual()

func _setup_visual() -> void:
    if _visual == null: return
    _visual.configure()
    _visual.position = Vector2.ZERO
    _visual.scale = Vector2.ONE
    _visual.z_index = 1
    _visual.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
    _visual.set_weapon(current_weapon)
    _visual.set_weapon_drawn(combat_enabled)
    _update_visual_motion()

func _update_visual_motion(delta: float = 0.0) -> void:
    if _visual == null: return
    _visual.update_motion(delta,velocity.rotated(-rotation))

func _refresh_visual() -> void:
    if _visual == null: return
    _visual.set_weapon(current_weapon)
    _visual.set_weapon_drawn(combat_enabled)
    _update_visual_motion()

func _play_fire_animation() -> void:
    if _visual == null or not combat_enabled: return
    _visual.attack()

func give_throwable(throwable_kind: StringName) -> void:
    throwable = throwable_kind

func take_damage(amount: int = 100, source: Node = null) -> void:
    if _health_component == null or not _health_component.apply_damage(amount, source):
        return
    _damage_cooldown = 0.24
    health = _health_component.current_health
    if health > 0:
        if _visual != null:
            _visual.modulate = Color(1.0, 0.50, 0.50, 1.0)
        return
    _on_health_component_died()

func get_aim_direction() -> Vector2:
    return _aim_input

func get_action_range() -> float:
    var data := get_weapon_data()
    return data.action_range if data != null else 32.0

func get_weapon_data() -> ArkonWeaponData:
    if _weapon_component == null:
        return null
    return _weapon_component.current_data

func _on_health_changed(current: int, maximum: int) -> void:
    health = current
    max_health = maximum

func _on_health_component_died() -> void:
    if is_dead:
        return
    health = 0
    is_dead = true
    controls_enabled = false
    velocity = Vector2.ZERO
    died.emit()
    if _visual != null:
        _visual.die()
        _visual.modulate = Color(0.65, 0.20, 0.20, 1.0)

func _on_weapon_component_changed(weapon: StringName, current_ammo: int) -> void:
    current_weapon = weapon
    ammo = current_ammo
    weapon_changed.emit(current_weapon, ammo)

func switch_next_weapon() -> void:
    if not combat_enabled or is_dead or not controls_enabled or owned_weapons.size()<2: return
    var next := owned_weapons[(owned_weapons.find(current_weapon)+1)%owned_weapons.size()]
    equip_weapon(next)

func add_ammo(amount: int = 12) -> void:
    if _weapon_component.infinite_ammo:return
    var previous := current_weapon
    _weapon_component.equip(&"pistol",amount)
    if previous!=&"pistol": _weapon_component.equip(previous)
    current_weapon = _weapon_component.current_weapon
    ammo = _weapon_component.ammo
    _refresh_visual()

func heal(amount: int) -> void: _health_component.heal(amount)

func set_combat_enabled(enabled: bool) -> void:
    combat_enabled = enabled
    _fire_held = false
    _throw_just_pressed = false
    if _visual != null: _visual.set_weapon_drawn(enabled)

func clear_controls() -> void:
    set_input(Vector2.ZERO,_aim_input,false,false,false,false)
    velocity = Vector2.ZERO
    if _visual != null: _visual.clear_motion()

func _draw() -> void:
    draw_circle(Vector2(1,3),8,Color(0.01,0.015,0.025,0.30))

func apply_flashbang(duration: float) -> void:
    flashbang_time = maxf(flashbang_time, clampf(duration, 0, 1.7))
    clear_controls()
