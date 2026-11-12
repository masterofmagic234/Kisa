extends Node2D
class_name ArkonTopdownWorld
## Host seam for selected v1.68 Level3Store methods. Map and story live outside.
const ProjectileScene = preload("res://addons/arkon_runtime/topdown/scenes/projectile.tscn")
const LAYOUT_SCALE: float = 1.5
const Feedback = preload("res://addons/arkon_runtime/topdown/fixtures/feedback.gd")
signal combat_feedback(kind: StringName, at: Vector2)
var player: ArkonLevel3Player
var pickups_root: Node2D
var _enemies: Array[ArkonLevel3Enemy] = []
var _doors: Array[ArkonLevel3Door] = []
var navigation := ArkonLevel3Navigation.new()
var _projectile_pool: ArkonObjectPool
var _player_dead := false
var _level_complete_started := false
var _camera_kick := Vector2.ZERO
var audio: Node = null
var feedback := Feedback.new()
var combat_paused := false
var _body_query := PhysicsShapeQueryParameters2D.new()

func initialize_combat() -> void:
    _projectile_pool = ArkonObjectPool.new(ProjectileScene, 24, self, 64)
    var shape := CircleShape2D.new()
    shape.radius = 7.8
    _body_query.shape = shape
    _body_query.collision_mask = 1
    _body_query.collide_with_areas = false
    player.fire_requested.connect(_on_player_fire_requested)
    player.throw_requested.connect(_on_throw_requested)
    player.died.connect(func(): _player_dead = true)
    get_node("/root/SignalBus").item_collected.connect(_on_bus_item_collected)
    for enemy in _enemies:
        enemy.shot_requested.connect(_on_enemy_shot_requested)

func is_combat_paused() -> bool:
    return combat_paused or _player_dead or _level_complete_started

func can_player_attack() -> bool:
    return player.combat_enabled and not is_combat_paused()

func _on_throw_requested(origin: Vector2, direction: Vector2) -> void:
    if not can_player_attack(): return
    _notify_noise(origin)
    origin = safe_muzzle(player.global_position, direction, 12.0)
    var target := _find_throw_target(origin, direction)
    var endpoint := target.global_position if target != null else _find_throw_endpoint(origin, direction)
    _spawn_bottle_projectile(origin, endpoint, target)

func _draw_shot_feedback(at: Vector2, _end: Vector2, _color: Color) -> void:
    combat_feedback.emit(&"shot", at)

func _spawn_muzzle_flash(at: Vector2, _angle: float, _cone_weapon := false) -> void:
    combat_feedback.emit(&"muzzle", at)

func _spawn_blood_feedback(at: Vector2, _direction: Vector2, _permanent: bool, _strength: float) -> void:
    combat_feedback.emit(&"hit", at)

func _spawn_bottle_impact(at: Vector2) -> void:
    combat_feedback.emit(&"glass", at)

func _set_hint(_message: String) -> void: pass
func _update_hud() -> void: pass

func _exit_tree() -> void:
    if _projectile_pool != null: _projectile_pool.release_all()
    _projectile_pool = null

func has_line_of_sight(from_position: Vector2, to_position: Vector2) -> bool:
    var query := PhysicsRayQueryParameters2D.create(from_position, to_position, 1)
    var result := get_world_2d().direct_space_state.intersect_ray(query)
    return result.is_empty()

func _trace_weapon_shot(
    origin: Vector2,
    direction: Vector2,
    shooter: CollisionObject2D,
    max_distance: float = 650.0,
    shot_hit_cache: Dictionary = {},
    spawn_muzzle_flash: bool = true,
    impact_damage: int = 100
) -> void:
    var shot_data: ArkonWeaponData=player.get_weapon_data() if shooter==player else null
    var end := origin + direction.normalized() * max_distance
    var query := PhysicsRayQueryParameters2D.create(origin, end, 1)
    query.exclude = [shooter.get_rid()]
    var result := get_world_2d().direct_space_state.intersect_ray(query)

    var visual_end := end
    if not result.is_empty():
        visual_end = result["position"]

    _draw_shot_feedback(
        origin,
        visual_end,
        Color(.72,.84,.75,.32) if shooter==player else Color(1.0,.80,.30,.60)
    )

    _spawn_projectile_visual(
        origin,
        visual_end,
        func(impact_position: Vector2, collider: Node) -> void:
            if not collider is ArkonHitboxComponent:
                return
            var actor := (collider as ArkonHitboxComponent).get_parent()
            if not actor is ArkonLevel3Enemy:
                return
            var enemy := actor as ArkonLevel3Enemy
            if enemy.state == enemy.State.DEAD:
                return
            var enemy_id := enemy.get_instance_id()
            if shot_hit_cache.has(enemy_id):
                return
            shot_hit_cache[enemy_id] = true
            _spawn_blood_feedback(
                impact_position,
                -direction,
                true,
                1.25
            )
            enemy.receive_hit(impact_damage, shooter)
            feedback.confirm_hit(),
        spawn_muzzle_flash,
        shot_data.projectile_speed if shot_data!=null else 650.0,
        shot_data.projectile_texture if shot_data!=null else "res://assets/fixtures/projectile.svg",
        shot_data.projectile_fps if shot_data!=null else 14.0,
        shot_data.projectile_scale if shot_data!=null else Vector2(1.1,1.1),
        true,
        1 | 4,
        shooter,
        0
    )

func _acquire_projectile() -> ArkonLevel3Projectile:
    if _projectile_pool == null:
        var fallback := ProjectileScene.instantiate() as ArkonLevel3Projectile
        if fallback == null:
            return null
        add_child(fallback)
        return fallback

    var bullet := _projectile_pool.acquire() as ArkonLevel3Projectile
    if bullet == null:
        return null

    bullet.set_recycle_callback(
        func() -> void:
            if _projectile_pool != null:
                _projectile_pool.release(bullet)
    )
    return bullet

func _spawn_projectile_visual(
    start: Vector2,
    end: Vector2,
    on_impact: Callable = Callable(),
    spawn_muzzle_flash: bool = true,
    projectile_speed: float = 650.0,
    projectile_texture: String = "res://assets/fixtures/projectile.svg",
    projectile_fps: float = 14.0,
    projectile_scale: Vector2 = Vector2(1.1, 1.1),
    collision_enabled: bool = false,
    collision_mask: int = 0,
    ignored_actor: Node = null,
    impact_damage: int = 0
) -> void:
    var bullet := _acquire_projectile()
    if bullet == null:
        return
    bullet.z_index = 34
    bullet.setup(
        start,
        end,
        projectile_speed,
        on_impact,
        projectile_texture,
        projectile_fps,
        projectile_scale,
        collision_enabled,
        collision_mask,
        ignored_actor,
        impact_damage
    )
    if spawn_muzzle_flash:
        _spawn_muzzle_flash(start, start.direction_to(end).angle(),ignored_actor==player)

func _on_player_fire_requested(
    origin: Vector2,
    direction: Vector2,
    weapon: StringName
) -> void:
    if not can_player_attack():
        return

    origin=safe_muzzle(player.global_position,direction,18.0)
    _notify_noise(origin)
    if audio!=null: audio.play_at(weapon,origin)
    _camera_kick=-direction*(3.0 if weapon==&"shotgun" else 1.3)

    if weapon == &"bat":
        _perform_bat_attack(origin, direction)
        return

    if weapon == &"shotgun":
        var shotgun_data := player.get_weapon_data()
        if shotgun_data == null:
            return
        _spawn_muzzle_flash(origin,direction.angle(),true)
        var shotgun_hit_cache: Dictionary = {}
        for spread in shotgun_data.spread_angles:
            _trace_weapon_shot(
                origin,
                direction.rotated(float(spread)),
                player,
                shotgun_data.max_distance,
                shotgun_hit_cache,
                false,
                shotgun_data.damage
            )
        return

    var weapon_data: ArkonWeaponData = player.get_weapon_data()
    var max_distance := weapon_data.max_distance if weapon_data != null else 650.0
    var damage := weapon_data.damage if weapon_data != null else 100
    _trace_weapon_shot(origin, direction, player, max_distance, {}, true, damage)

func _perform_bat_attack(origin: Vector2, direction: Vector2) -> void:
    var shape := CircleShape2D.new()
    shape.radius = 48.0 * LAYOUT_SCALE
    var params := PhysicsShapeQueryParameters2D.new()
    params.shape = shape
    params.transform = Transform2D(0.0, origin + direction * 14.0)
    params.collision_mask = 2
    params.exclude = [player.get_rid()]

    var hits := get_world_2d().direct_space_state.intersect_shape(params, 16)
    var nearest_enemy: ArkonLevel3Enemy = null
    var nearest_distance := INF

    for hit in hits:
        var collider := hit.get("collider") as Node
        if collider is ArkonLevel3Enemy:
            var enemy := collider as ArkonLevel3Enemy
            if enemy.state == enemy.State.DEAD:
                continue
            var to_enemy := player.global_position.direction_to(enemy.global_position)
            if absf(direction.angle_to(to_enemy)) > deg_to_rad(70.0):
                continue
            if not has_line_of_sight(player.global_position, enemy.global_position):
                continue
            var distance := player.global_position.distance_to(enemy.global_position)
            if distance < nearest_distance:
                nearest_distance = distance
                nearest_enemy = enemy

    if nearest_enemy != null:
        var bat_data := player.get_weapon_data()
        var bat_damage: int = bat_data.damage if bat_data != null else 100
        _spawn_blood_feedback(nearest_enemy.global_position, -direction, true, 1.05)
        nearest_enemy.receive_hit(bat_damage, player)
        feedback.confirm_hit()
        _notify_noise(player.global_position)

func _find_nearest_stunned_enemy() -> ArkonLevel3Enemy:
    if not player.combat_enabled: return null
    var best: ArkonLevel3Enemy = null
    var best_distance := player.get_action_range() * LAYOUT_SCALE
    var aim := player.get_aim_direction()
    for enemy in _enemies:
        if not is_instance_valid(enemy) or not enemy.is_stunned():
            continue
        var distance := player.global_position.distance_to(enemy.global_position)
        if distance > best_distance:
            continue
        var to_enemy := player.global_position.direction_to(enemy.global_position)
        if absf(aim.angle_to(to_enemy)) > deg_to_rad(75.0):
            continue
        if not has_line_of_sight(player.global_position, enemy.global_position):
            continue
        best = enemy
        best_distance = distance
    return best

func _find_nearest_closed_door() -> ArkonLevel3Door:
    var best: ArkonLevel3Door=null
    var distance_ := 41.0
    for door in _doors:
        if not is_instance_valid(door) or door.is_open or door.is_opening: continue
        var distance := player.global_position.distance_to(door.global_position)
        if distance>distance_: continue
        var query := PhysicsRayQueryParameters2D.create(player.global_position,door.global_position,1)
        query.exclude=[door.pivot.get_rid()]
        if not get_world_2d().direct_space_state.intersect_ray(query).is_empty(): continue
        best=door
        distance_=distance
    return best

func _find_throw_target(origin: Vector2, direction: Vector2) -> ArkonLevel3Enemy:
    var best: ArkonLevel3Enemy = null
    var best_distance := 190.0
    for enemy in _enemies:
        if not is_instance_valid(enemy) or enemy.state == enemy.State.DEAD:
            continue
        var offset := enemy.global_position - origin
        var distance := offset.length()
        if distance > best_distance:
            continue
        var normalized_offset := offset.normalized()
        if absf(direction.angle_to(normalized_offset)) > deg_to_rad(34.0):
            continue
        if not has_line_of_sight(origin, enemy.global_position):
            continue
        best = enemy
        best_distance = distance
    return best

func _find_throw_endpoint(origin: Vector2, direction: Vector2) -> Vector2:
    var max_distance := 190.0
    var end := origin + direction.normalized() * max_distance
    var query := PhysicsRayQueryParameters2D.create(origin, end, 1)
    var result := get_world_2d().direct_space_state.intersect_ray(query)
    if result.is_empty():
        return end
    return result["position"]

func _spawn_bottle_projectile(start: Vector2, end: Vector2, _target: ArkonLevel3Enemy) -> void:
    _spawn_projectile_visual(start,end,
        func(at: Vector2, collider: Node) -> void:
            if collider is ArkonHitboxComponent:
                var actor := (collider as ArkonHitboxComponent).get_parent()
                if actor is ArkonLevel3Enemy: actor.stun(3.8)
            _spawn_bottle_impact(at),
        false,360.0,"res://assets/fixtures/projectile.svg",8.0,Vector2(0.86,0.86),true,1|4,player,0)

func _notify_noise(noise_position: Vector2) -> void:
    for enemy in _enemies:
        if is_instance_valid(enemy):
            enemy.hear_noise(noise_position)

func _on_enemy_shot_requested(shooter: ArkonLevel3Enemy, origin: Vector2, direction: Vector2) -> void:
    if _player_dead or _level_complete_started:
        return
    if not is_instance_valid(shooter) or shooter.state == shooter.State.DEAD:
        return

    origin=safe_muzzle(shooter.global_position,direction,16.0)
    # Aim at the player's current position, but let the projectile continue
    # beyond that snapshot. A dodged shot must be able to pass the player.
    var shot_direction := direction.normalized()
    var max_distance := 900.0
    var far_end := origin + shot_direction * max_distance
    var query := PhysicsRayQueryParameters2D.create(origin, far_end, 1)
    query.exclude = [shooter.get_rid()]
    var wall_hit := get_world_2d().direct_space_state.intersect_ray(query)
    var end_position := far_end
    if not wall_hit.is_empty():
        end_position = wall_hit["position"]

    _spawn_enemy_projectile(origin, end_position, shooter)
    if audio!=null: audio.play_at(&"enemy_shot",origin)

func _spawn_enemy_projectile(start: Vector2, end: Vector2, shooter: ArkonLevel3Enemy = null) -> void:
    var bullet := _acquire_projectile()
    if bullet == null:
        return
    bullet.z_index = 34
    bullet.setup(
        start,
        end,
        380.0,
        func(_impact_position: Vector2, _collider: Node) -> void:
            # Damage is delivered by the projectile's authoritative swept query.
            pass,
        "res://assets/fixtures/projectile.svg",
        10.0,
        Vector2(1.1, 1.1),
        true,
        1 | 8,
        shooter,
        8
    )
    _spawn_muzzle_flash(start, start.direction_to(end).angle())

func is_body_path_clear(from: Vector2, to: Vector2) -> bool:
    _body_query.transform=Transform2D(0,from)
    _body_query.motion=to-from
    var cast := get_world_2d().direct_space_state.cast_motion(_body_query)
    return cast.size()==2 and cast[0]>0.999

func safe_muzzle(center: Vector2, direction: Vector2, distance_: float) -> Vector2:
    # An extended muzzle must not spawn beyond a thin wall/closed door when
    # the body is pressed against it. Start on the actor's side of that wall.
    var end := center+direction.normalized()*distance_
    var ray := PhysicsRayQueryParameters2D.create(center,end,1)
    var collision := get_world_2d().direct_space_state.intersect_ray(ray)
    return collision.position-direction.normalized() if not collision.is_empty() else end

func _on_bus_item_collected(
    item_kind: StringName,
    _item_id: StringName,
    amount: int,
    collector: Node
) -> void:
    if collector != player or amount <= 0:
        return
    _on_pickup_collected(item_kind)

func _on_pickup_collected(kind: StringName) -> void:
    match kind:
        &"pistol": player.equip_weapon(&"pistol",12)
        &"shotgun":
            player.equip_weapon(&"shotgun",6)
            _set_hint("Дробовик. TAB / СМЕНИТЬ — смена оружия.")
        &"bat":
            player.equip_weapon(&"bat")
            _set_hint("Бита. Патроны не нужны. TAB / СМЕНИТЬ — смена оружия.")
        &"bottle":
            player.give_throwable(&"bottle")
            _set_hint("Бутылка. Q / БРОСОК — оглушить, E / ДЕЙСТВИЕ — добить.")
        &"ammo":
            # Old authored pickups migrate to health kits. Ammunition is
            # unlimited and a supply never changes weapon/cooldown state.
            player.heal(35)
            _set_hint("Аптечка: +35 здоровья")
        &"medkit":
            player.heal(35)
            _set_hint("Аптечка: +35 здоровья")
    if audio!=null: audio.play_at(&"pickup",player.global_position)
    _update_hud()

func _find_nearest_pickup() -> ArkonLevel3Pickup:
    if not player.combat_enabled: return null
    var best: ArkonLevel3Pickup
    var distance_ := 32.0
    for child in pickups_root.get_children():
        var pickup := child as ArkonLevel3Pickup
        if pickup==null or pickup.consumed or pickup.is_automatic(): continue
        var distance := player.global_position.distance_to(pickup.global_position)
        if distance<=distance_ and has_line_of_sight(player.global_position,pickup.global_position):
            best=pickup
            distance_=distance
    return best
