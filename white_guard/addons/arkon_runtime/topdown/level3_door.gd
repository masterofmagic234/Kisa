extends Node2D
class_name ArkonLevel3Door

const AssetVisual = preload("res://addons/arkon_runtime/topdown/fixtures/asset_visual.gd")

signal opened(door: ArkonLevel3Door)
signal slammed(door: ArkonLevel3Door)
signal closed(door: ArkonLevel3Door)

@export var open_speed: float = 12.0
@export var slam_speed: float = 30.0
@export var close_speed: float = 7.5
@export var auto_close_delay: float = 3.0

var is_open: bool = false
var is_opening: bool = false
var is_slammed: bool = false
var locked: bool = false

var _target_rotation: float = 0.0
var _closed_rotation: float = 0.0
const OPEN_ANGLE: float = PI * 0.5
var _close_timer: float = -1.0
var _open_direction: float = 1.0
var _slam_hit_enemies: Dictionary = {}

@onready var pivot: AnimatableBody2D = $Pivot
@onready var body_shape: CollisionShape2D = $Pivot/CollisionShape2D
@onready var hit_area: Area2D = $Pivot/HitArea
@onready var door_sprite: Sprite2D = $Pivot/Sprite2D
@onready var frame_sprite: Sprite2D = $FrameSprite

func setup(world_position: Vector2, locked_state: bool = false, initial_rotation: float = 0.0, texture_path: String = "res://assets/fixtures/door.svg") -> void:
    global_position = world_position
    locked = locked_state
    rotation = initial_rotation
    _closed_rotation = pivot.rotation
    _target_rotation = pivot.rotation
    var custom_texture := AssetVisual.first_frame_texture(texture_path)
    if custom_texture != null:
        door_sprite.texture = custom_texture

func _ready() -> void:
    _closed_rotation = pivot.rotation
    _target_rotation = pivot.rotation
    hit_area.monitoring = false
    hit_area.body_entered.connect(_on_hit_area_body_entered)
    (body_shape.shape as RectangleShape2D).size = Vector2(32, 5)
    body_shape.disabled = false
    door_sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
    door_sprite.scale = Vector2(32.0, 6.0) / door_sprite.texture.get_size()
    door_sprite.visible = true
    z_index = 2
    frame_sprite.scale = Vector2(0.55, 0.55)
    frame_sprite.z_index = 0
    frame_sprite.modulate = Color(1.0, 0.92, 0.82, 1.0)

func _physics_process(delta: float) -> void:
    if is_opening:
        var speed := slam_speed if is_slammed else open_speed
        pivot.rotation = rotate_toward(pivot.rotation, _target_rotation, speed * delta)

        if is_equal_approx(pivot.rotation, _target_rotation):
            pivot.rotation = _target_rotation
            is_opening = false
            is_open = true
            is_slammed = false
            _slam_hit_enemies.clear()
            body_shape.disabled = true
            hit_area.monitoring = false
            _close_timer = auto_close_delay
            opened.emit(self)
            _emit_interaction(&"opened")

    elif is_open:
        _close_timer -= delta
        if _close_timer <= 0.0:
            _begin_close()

    else:
        if not is_equal_approx(pivot.rotation, _closed_rotation):
            pivot.rotation = rotate_toward(pivot.rotation, _closed_rotation, close_speed * delta)
            if is_equal_approx(pivot.rotation, _closed_rotation):
                pivot.rotation = _closed_rotation
                if _doorway_blocked():
                    # Never reactivate a solid door underneath a player/enemy.
                    # Reopen instead and try again once the doorway is clear.
                    _target_rotation = _closed_rotation + _open_direction * OPEN_ANGLE
                    is_opening = true
                    is_open = false
                    _close_timer = -1.0
                    return
                body_shape.disabled = false
                closed.emit(self)
                _emit_interaction(&"closed")

func _emit_interaction(state: StringName) -> void:
    var bus := get_node_or_null("/root/SignalBus")
    if bus != null and bus.has_signal("object_interacted"):
        bus.object_interacted.emit(StringName(name), state)

func get_interaction_position() -> Vector2:
    return global_position


func interact(interactor_position: Vector2, dynamic_slam: bool = false) -> bool:
    if locked or is_opening:
        return false

    if is_open:
        _close_timer = auto_close_delay
        return true

    var to_interactor := interactor_position - pivot.global_position
    if to_interactor.length_squared() <= 0.001:
        to_interactor = Vector2.DOWN.rotated(global_rotation)

    var door_normal := Vector2.DOWN.rotated(global_rotation).normalized()
    var side := to_interactor.normalized().dot(door_normal)

    # Push the door away from the player. The sign chooses the side of the hinge.
    var open_direction := -1.0 if side >= 0.0 else 1.0
    _open_direction = open_direction
    _target_rotation = _closed_rotation + open_direction * OPEN_ANGLE

    body_shape.set_deferred("disabled", true)
    is_opening = true
    is_slammed = dynamic_slam
    _slam_hit_enemies.clear()
    _close_timer = -1.0

    if is_slammed:
        hit_area.set_deferred("monitoring", true)
        slammed.emit(self)
        _emit_interaction(&"slammed")

    return true

func _doorway_blocked() -> bool:
    if body_shape == null or body_shape.shape == null:
        return false
    var params := PhysicsShapeQueryParameters2D.new()
    params.shape = body_shape.shape
    params.transform = body_shape.global_transform
    params.collision_mask = 2
    params.collide_with_bodies = true
    params.collide_with_areas = false
    params.exclude = [pivot.get_rid()]
    var hits := get_world_2d().direct_space_state.intersect_shape(params, 8)
    for hit in hits:
        var collider := hit.get("collider") as Node
        if collider is ArkonLevel3Player or collider is ArkonLevel3Enemy:
            return true
    return false

func _begin_close() -> void:
    is_opening = false
    is_open = false
    is_slammed = false
    _slam_hit_enemies.clear()
    _target_rotation = _closed_rotation
    _close_timer = -1.0
    # Keep the door collider disabled while the leaf travels back to the frame.
    # Collision is restored only after the visual rotation reaches the closed pose.
    body_shape.disabled = true
    hit_area.monitoring = false

func _on_hit_area_body_entered(hit_body: Node2D) -> void:
    if not is_slammed:
        return

    if hit_body is ArkonLevel3Enemy:
        var enemy := hit_body as ArkonLevel3Enemy
        # A single slam can hit every enemy that enters the doorway, but each
        # enemy is resolved at most once during that slam.
        if enemy.state == enemy.State.DEAD:
            return
        var enemy_id := enemy.get_instance_id()
        if _slam_hit_enemies.has(enemy_id):
            return
        _slam_hit_enemies[enemy_id] = true
        var push_dir := (enemy.global_position - global_position).normalized()
        enemy.stun(2.6, push_dir * 170.0)
