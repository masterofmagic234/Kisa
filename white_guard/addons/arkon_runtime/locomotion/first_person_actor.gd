extends CharacterBody3D
## Adapted from release Level1Player: real body movement and stair sweeps.
## The squirrel mission, dash, heist, portrait and its UI are not dependencies.
const Motion = preload("res://addons/arkon_runtime/math/movement_math.gd")
const StepMotion = preload("res://addons/arkon_runtime/locomotion/grounded_step_motion.gd")
const Health = preload("res://addons/arkon_runtime/combat/health_component.gd")
const Weapon = preload("res://addons/arkon_runtime/combat/weapon_component.gd")
@export var walk_speed := 4.0
@export var look_sensitivity := 0.0028
@export var hit_mask := 3
var input_enabled := true
var camera: Camera3D
var health: ArkonHealthComponent
var weapon: ArkonWeaponComponent
var _pitch := 0.0

func _ready() -> void:
    camera = $Camera3D
    health = $Health
    weapon = $Weapon
    floor_snap_length = 0.4
    floor_max_angle = deg_to_rad(52)

func _unhandled_input(event: InputEvent) -> void:
    if not input_enabled or health.is_dead: return
    if event is InputEventMouseMotion and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
        rotate_y(-event.relative.x * look_sensitivity)
        _pitch = clampf(_pitch - event.relative.y * look_sensitivity, -1.4, 1.4)
        camera.rotation.x = _pitch
    if event.is_action_pressed("capture_pointer"):
        Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
    if event.is_action_pressed("fire") and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
        fire()

func _physics_process(delta: float) -> void:
    weapon.tick(delta)
    if SceneFlow.is_loading_gameplay() or not input_enabled or health.is_dead: return
    var axis := Input.get_vector("move_left", "move_right", "move_forward", "move_back")
    var vertical := velocity.y
    velocity = Motion.velocity_for_input(global_basis, axis, walk_speed)
    velocity.y = -0.5 if is_on_floor() else vertical - 18.0 * delta
    StepMotion.move(self, delta)

func fire() -> bool:
    if not weapon.consume_shot(): return false
    var query := PhysicsRayQueryParameters3D.create(camera.global_position, camera.global_position - camera.global_basis.z * 60.0, hit_mask)
    query.exclude = [get_rid()]
    query.collide_with_areas = true
    var hit := get_world_3d().direct_space_state.intersect_ray(query)
    if not hit.is_empty() and hit.collider.has_method("receive_hit"):
        hit.collider.receive_hit(weapon.current_data.damage, self)
    SignalBus.emit_audio_event(&"shoot", global_position)
    return true

func take_damage(amount: int, source: Node = null) -> bool:
    return health.apply_damage(amount, source)
