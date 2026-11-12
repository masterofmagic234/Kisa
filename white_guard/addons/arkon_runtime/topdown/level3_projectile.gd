extends Area2D
class_name ArkonLevel3Projectile

const ArkonHitboxComponent = preload("res://addons/arkon_runtime/combat/hitbox_component.gd")

var target_position: Vector2
var direction: Vector2
var speed: float = 650.0
var lifetime: float = 1.0
var elapsed: float = 0.0
var on_impact: Callable = Callable()
var impact_position: Vector2
var collision_enabled: bool = false
var impact_damage: int = 0
var _ignored_actor: Node = null
var _impact_started: bool = false
var _collision_mask: int = 0
var _impact_collider: Node = null
var _recycle_callback: Callable = Callable()
var _visual: Node2D = null
var _visual_path: String = ""
var _visual_fps: float = 0.0
var _visual_scale: Vector2 = Vector2.ZERO

func setup(
    start: Vector2,
    target: Vector2,
    speed_: float,
    impact_callback: Callable,
    sprite_path: String,
    fps: float,
    sprite_scale: Vector2,
    collision_enabled_: bool = false,
    collision_mask_: int = 0,
    ignored_actor: Node = null,
    impact_damage_: int = 0
) -> void:
    global_position = start
    target_position = target
    direction = start.direction_to(target)
    speed = maxf(speed_, 1.0)
    lifetime = maxf(start.distance_to(target) / speed, 0.08) + 0.20
    elapsed = 0.0
    on_impact = impact_callback
    impact_position = target
    collision_enabled = collision_enabled_
    impact_damage = maxi(impact_damage_, 0)
    _ignored_actor = ignored_actor
    _impact_started = false
    _impact_collider = null
    _collision_mask = collision_mask_ if collision_enabled_ else 0

    if _visual == null or not is_instance_valid(_visual):
        _visual = _make_visual(sprite_path, fps, sprite_scale)
        if _visual != null:
            _visual.z_index = 1
            add_child(_visual)
        _visual_path = sprite_path
        _visual_fps = fps
        _visual_scale = sprite_scale
    elif (
        _visual_path != sprite_path
        or not _visual_scale.is_equal_approx(sprite_scale)
        or not is_equal_approx(_visual_fps, fps)
    ):
        if _visual is AnimatedSprite2D:
            (_visual as AnimatedSprite2D).sprite_frames = ArkonLevel3AssetVisual.sprite_frames_from_strip(sprite_path,fps,true)
        _visual.scale = sprite_scale
        _visual_path = sprite_path
        _visual_fps = fps
        _visual_scale = sprite_scale

    if _visual != null:
        _visual.position = Vector2.ZERO
        _visual.rotation = direction.angle()
        _visual.visible = true
        if _visual is AnimatedSprite2D:
            var sprite := _visual as AnimatedSprite2D
            sprite.frame = 0
            sprite.play(&"default")

    # Projectile collision is resolved by an authoritative swept physics query.
    # The Area2D remains only as the visual/projectile container; relying on
    # area_entered alone can miss fast-moving or newly spawned obstacles.
    collision_layer = 0
    collision_mask = 0
    monitoring = false
    monitorable = false

func _physics_process(delta: float) -> void:
    if _impact_started:
        return

    elapsed += delta
    var to_target := global_position.distance_to(target_position)
    if to_target <= 2.0 or elapsed >= lifetime:
        impact_position = global_position
        _impact_collider = null
        _impact_and_free()
        return

    var travel := minf(speed * delta, to_target)
    var next_position := global_position + direction * travel

    if collision_enabled and _collision_mask != 0:
        var query := PhysicsRayQueryParameters2D.create(
            global_position,
            next_position,
            _collision_mask
        )
        query.collide_with_bodies = true
        query.collide_with_areas = true
        query.exclude = _excluded_rids()

        var result := get_world_2d().direct_space_state.intersect_ray(query)
        if not result.is_empty():
            impact_position = result["position"]
            _impact_collider = result["collider"] as Node
            global_position = impact_position

            if _impact_collider is ArkonHitboxComponent and impact_damage > 0:
                (_impact_collider as ArkonHitboxComponent).receive_hit(
                    impact_damage,
                    _ignored_actor
                )

            _impact_and_free()
            return

    global_position = next_position


func _excluded_rids() -> Array[RID]:
    var result: Array[RID] = []
    if _ignored_actor is CollisionObject2D:
        result.append((_ignored_actor as CollisionObject2D).get_rid())

    if _ignored_actor != null:
        var ignored_hitbox := _ignored_actor.get_node_or_null("Hitbox") as CollisionObject2D
        if ignored_hitbox != null:
            result.append(ignored_hitbox.get_rid())

    return result

func prime_visual() -> void:
    _visual_path = "res://assets/fixtures/projectile.svg"
    _visual_fps = 14.0
    _visual_scale = Vector2(1.1,1.1)
    _visual = _make_visual(_visual_path,_visual_fps,_visual_scale)
    add_child(_visual)
    _visual.z_index = 1

func set_recycle_callback(callback: Callable) -> void:
    _recycle_callback = callback

func _impact_and_free() -> void:
    if _impact_started:
        return
    _impact_started = true
    if on_impact.is_valid():
        var callback := on_impact
        on_impact = Callable()
        callback.call(impact_position, _impact_collider)

    _ignored_actor = null
    _impact_collider = null
    var recycle := _recycle_callback
    _recycle_callback = Callable()
    if recycle.is_valid():
        recycle.call()
    else:
        queue_free()

func _make_visual(path: String, fps: float, sprite_scale: Vector2) -> Node2D:
    var helper_script = load("res://addons/arkon_runtime/topdown/fixtures/asset_visual.gd")
    if helper_script != null:
        var visual: Node2D = helper_script.animated_strip(path, fps, sprite_scale)
        if visual != null:
            return visual
    return null
