extends CharacterBody2D
class_name ArkonLevel3Enemy
const SquirrelAnimator = preload("res://addons/arkon_runtime/topdown/fixtures/actor_animator.gd")
enum State { IDLE, ALERT, STUNNED, DEAD }
signal shot_requested(shooter, origin: Vector2, direction: Vector2)
@export var move_speed := 82.0
@export var vision_range := 340.0
@export var preferred_distance := 170.0
@export var attack_range := 39.0
@export var patrol_radius := 48.0
@export var ranged := true
@export var enemy_kind: StringName = &"gunman"
var staging_path := PackedVector2Array()
var staging := false
var staging_face := Vector2.INF
var target: ArkonLevel3Player
var world: Node2D
var spawn_position: Vector2
var last_known_position: Vector2
var state: State = State.IDLE
var _stun_timer := 0.0
var _attack_cooldown := 0.4
var _windup := 0.0
var _locked_aim := Vector2.RIGHT
var _sees_target := false
var _sense_timer := 0.0
var _route_timer := 0.0
var _path := PackedVector2Array()
var _path_index := 0
var _last_goal := Vector2.INF
var _direct_clear := false
var _patrol_target := Vector2.ZERO
var _patrol_phase := 0.0
var _aim_direction := Vector2.RIGHT
var _animator := SquirrelAnimator.new()
var raid_role: StringName = &""
var raid_side: StringName = &""
var duty_room: StringName = &""
var duty_region := Rect2()
var duty_patrol := PackedVector2Array()
var duty_approach := PackedVector2Array()
var duty_arrived := false
var _duty_patrol_index := 0
@onready var _visual: AnimatedSprite2D = %Visual
@onready var _weapon_visual: Sprite2D = %WeaponVisual
@onready var _health_component: ArkonHealthComponent = %Health
@onready var _hitbox_component: ArkonHitboxComponent = %Hitbox
@onready var _collision_shape: CollisionShape2D = %CollisionShape2D

func _ready() -> void:
    world = get_parent().get_parent() as Node2D
    target = world.get_node("Player") as ArkonLevel3Player
    spawn_position = global_position
    last_known_position = global_position
    _patrol_target = global_position
    collision_layer = 2
    collision_mask = 1|2
    z_index = 14
    ranged = enemy_kind==&"gunman"
    move_speed = 77.0 if ranged else (100.0 if enemy_kind==&"butcher" else 93.0)
    _sense_timer = float(posmod(hash(name),10))*0.01
    _patrol_phase = float(posmod(hash(name),100))*0.0628
    _health_component.died.connect(_on_health_died)
    _animator.configure(_visual,enemy_kind,_patrol_phase)
    _weapon_visual.visible = false

func _physics_process(delta: float) -> void:
    if get_node("/root/SceneFlow").is_loading_gameplay(): return
    if state==State.DEAD:
        if not world.is_combat_paused(): _update_visual(delta)
        return
    if staging:
        _stage_tick(delta)
        return
    if world.is_combat_paused() or not world.navigation.ready:
        velocity = Vector2.ZERO
        _visual.pause()
        return
    _attack_cooldown = maxf(0,_attack_cooldown-delta)
    _route_timer -= delta
    if state==State.STUNNED:
        _stun_timer -= delta
        velocity = velocity.move_toward(Vector2.ZERO,800*delta)
        move_and_slide()
        _visual.modulate = Color(1,0.9,0.65)
        if _stun_timer<=0:
            state = State.ALERT
            _visual.modulate = Color.WHITE
        _update_visual(delta)
        queue_redraw()
        return
    if target==null or target.is_dead: return
    if not duty_approach.is_empty():
        _duty_approach_tick(delta)
        return
    _sense_timer -= delta
    if _sense_timer<=0:
        _sense_timer = 0.1
        var local_target := _target_in_duty_area(target.global_position)
        _sees_target = local_target and global_position.distance_to(target.global_position)<=vision_range and world.has_line_of_sight(global_position,target.global_position)
        if _sees_target:
            state = State.ALERT
            last_known_position = target.global_position
        elif raid_role!=&"" and state==State.ALERT and (not local_target or not _target_in_duty_area(global_position)):
            _return_to_duty()
    if _windup>0:
        _windup -= delta
        velocity = velocity.move_toward(Vector2.ZERO,1100*delta)
        rotation = _locked_aim.angle()
        if _windup<=0: _resolve_attack()
    elif state==State.IDLE:
        if global_position.distance_to(_patrol_target)<8.0:
            if not duty_patrol.is_empty():
                _duty_patrol_index=(_duty_patrol_index+1)%duty_patrol.size()
                _patrol_target=duty_patrol[_duty_patrol_index]
            else:
                _patrol_phase += 1.45
                _patrol_target = spawn_position+Vector2.from_angle(_patrol_phase)*patrol_radius*0.65
        _follow(_patrol_target,move_speed*0.38,delta)
    else:
        var distance := global_position.distance_to(last_known_position)
        _aim_direction = global_position.direction_to(last_known_position)
        if _sees_target:
            rotation = _aim_direction.angle()
            if ranged:
                if distance>preferred_distance+35:
                    _follow(last_known_position,move_speed,delta)
                elif distance<preferred_distance-55:
                    var retreat := global_position-_aim_direction*35
                    if world.is_body_path_clear(global_position,retreat):
                        velocity = velocity.move_toward(-_aim_direction*move_speed*0.55,750*delta)
                    else: velocity = velocity.move_toward(Vector2.ZERO,950*delta)
                else: velocity = velocity.move_toward(Vector2.ZERO,950*delta)
            else: _follow(last_known_position,move_speed,delta)
            if _attack_cooldown<=0 and (ranged or distance<=attack_range):
                _locked_aim = _aim_direction
                _windup = 0.48 if ranged else 0.34
                _attack_cooldown = 1.35 if ranged else 1.10
        elif distance<14:
            state = State.IDLE
            _path.clear()
            _patrol_target = global_position
            velocity = velocity.move_toward(Vector2.ZERO,950*delta)
        else: _follow(last_known_position,move_speed*0.80,delta)
    _separate_room_units(delta)
    move_and_slide()
    if state==State.IDLE and velocity.length_squared()>10: rotation = velocity.angle()
    _update_visual(delta)
    queue_redraw()

func _update_visual(delta: float) -> void:
    _animator.update(delta,velocity,rotation,_windup,.48 if ranged else .34,state==State.STUNNED,state==State.DEAD)

func _follow(goal: Vector2, speed_: float, delta: float) -> void:
    # The collider radius is respected by the sampled route. A visible ray alone
    # is insufficient around the edge of a chair or a wall corner.
    if _route_timer<=0:
        _direct_clear=world.is_body_path_clear(global_position,goal)
        _route_timer=0.24
        if not _direct_clear and (goal.distance_to(_last_goal)>12 or _path_index>=_path.size()):
            _path=world.navigation.path(global_position,goal)
            _path_index=0
            _last_goal=goal
    if _direct_clear:
        velocity=velocity.move_toward(global_position.direction_to(goal)*speed_,850*delta)
        return
    while _path_index<_path.size() and global_position.distance_to(_path[_path_index])<2.5: _path_index+=1
    if raid_role!=&"":
        # Twenty bodies must not all pin themselves to the same 2.5px grid
        # sample. Advance through visible corridor points using the real body
        # sweep; this keeps corners/walls solid and lets a queued pair move on.
        while _path_index+1<_path.size() and world.is_body_path_clear(global_position,_path[_path_index+1]):
            _path_index+=1
    if _path_index>=_path.size():
        velocity = velocity.move_toward(Vector2.ZERO,900*delta)
        return
    var waypoint := _path[_path_index]
    for door in world._doors:
        if not door.is_open and not door.is_opening and global_position.distance_to(door.global_position)<34:
            door.interact(global_position)
    velocity = velocity.move_toward(global_position.direction_to(waypoint)*speed_,850*delta)

func _resolve_attack() -> void:
    if state!=State.ALERT or target.is_dead: return
    _animator.attack()
    if ranged:
        shot_requested.emit(self,global_position+_locked_aim*16,_locked_aim)
    else:
        var offset := target.global_position-global_position
        if offset.length()<=attack_range+4 and offset.normalized().dot(_locked_aim)>0.55 and world.has_line_of_sight(global_position,target.global_position): target.take_damage(22,self)

func hear_noise(at: Vector2) -> void:
    if state==State.DEAD or state==State.STUNNED: return
    # A shot in the kitchen must not summon every room patrol. They react to
    # an intruder/noise in their own assigned area and then return to their beat.
    if not duty_approach.is_empty() or not _target_in_duty_area(at): return
    if global_position.distance_to(at)<=250:
        state = State.ALERT
        last_known_position = at
        _route_timer = 0

func stun(duration: float = 3.2, knockback_velocity: Vector2 = Vector2.ZERO) -> void:
    if state==State.DEAD: return
    state = State.STUNNED
    _stun_timer = duration
    _windup = 0
    velocity = knockback_velocity
    _animator.interrupt()
    _update_visual(0)
    var bus := get_node_or_null("/root/SignalBus")
    if bus!=null: bus.entity_stunned.emit(self,duration)

func is_stunned() -> bool: return state==State.STUNNED
func receive_hit(amount: int = 100, source: Node = null) -> void:
    if state!=State.DEAD: _hitbox_component.receive_hit(amount,source)
func take_damage(amount: int = 100, source: Node = null) -> void:
    if state!=State.DEAD: _health_component.apply_damage(amount,source)
func kill() -> void:
    if state==State.DEAD: return
    state = State.DEAD
    if not _health_component.is_dead: _health_component.force_kill()
    velocity = Vector2.ZERO
    _windup = 0
    _collision_shape.set_deferred("disabled",true)
    _hitbox_component.set_deferred("collision_layer",0)
    _hitbox_component.set_deferred("monitorable",false)
    collision_layer = 0
    _animator.fall()
    _update_visual(0)
    _visual.modulate = Color(0.75,0.70,0.72)
    z_index = -2
    var bus := get_node_or_null("/root/SignalBus")
    if bus!=null: bus.enemy_defeated.emit(self)
    queue_redraw()
func _on_health_died() -> void: kill()

func _draw() -> void:
    if state==State.DEAD: return
    draw_circle(Vector2(1,3),8,Color(0.01,0.015,0.025,0.30))
    if state==State.STUNNED:
        draw_arc(Vector2.ZERO,15,0,TAU,24,Color(1,0.78,0.3,0.80),1.3,true)
    elif _windup>0:
        var mat_color := Color(1.0,0.37,0.32,0.8) if ranged else Color(1.0,0.76,0.25,0.8)
        var aim := _locked_aim.rotated(-rotation)
        draw_line(aim*13,aim*(74 if ranged else 37),mat_color,1.0,true)
        draw_arc(Vector2.ZERO,15,0,TAU*(1.0-_windup/(0.48 if ranged else 0.34)),24,mat_color,1.4,true)

func stage(points: PackedVector2Array, face_at: Vector2 = Vector2.INF) -> void:
    staging = true
    staging_path = points
    staging_face = face_at
    state = State.IDLE
    _windup = 0

func _stage_tick(delta: float) -> void:
    while not staging_path.is_empty() and global_position.distance_to(staging_path[0]) < 4:
        staging_path.remove_at(0)
    if staging_path.is_empty():
        velocity = Vector2.ZERO
        var hold_at := staging_face if staging_face.is_finite() else target.global_position
        rotation = global_position.direction_to(hold_at).angle()
        _update_visual(delta)
        return
    var direction := global_position.direction_to(staging_path[0])
    velocity = direction * 195
    rotation = direction.angle()
    move_and_slide()
    _update_visual(delta)

func begin_assault(at: Vector2) -> void:
    staging = false
    staging_face = Vector2.INF
    staging_path.clear()
    state = State.ALERT
    last_known_position = at
    _route_timer = 0
    _sense_timer = 0
    if raid_role!=&"":
        state=State.IDLE
        last_known_position=spawn_position

func configure_raid_duty(description: Dictionary) -> void:
    raid_role=description.role
    raid_side=description.side
    duty_room=description.room
    duty_region=description.region
    duty_approach=_snap_duty_points(description.approach)
    duty_patrol=_snap_duty_points(description.patrol)
    _duty_patrol_index=posmod(hash(name),maxi(1,duty_patrol.size()))
    if not duty_patrol.is_empty():
        spawn_position=duty_patrol[_duty_patrol_index]
        _patrol_target=spawn_position
    _path.clear()
    _path_index=0
    _last_goal=Vector2.INF
    duty_arrived=false

func _snap_duty_points(points: PackedVector2Array) -> PackedVector2Array:
    var result:=PackedVector2Array()
    for at in points:
        var id: Vector2i=world.navigation.nearest_id(at)
        var snapped: Vector2=world.navigation.grid.get_point_position(id)
        if result.is_empty() or result[result.size()-1].distance_to(snapped)>1:
            result.append(snapped)
    return result

func _target_in_duty_area(at: Vector2) -> bool:
    return raid_role==&"" or duty_region.grow(72 if raid_role==&"kitchen" else 42).has_point(at)

func _return_to_duty() -> void:
    state=State.IDLE
    _sees_target=false
    _windup=0
    _path.clear()
    _path_index=0
    _route_timer=0
    _last_goal=Vector2.INF
    _patrol_target=spawn_position if duty_patrol.is_empty() else duty_patrol[_duty_patrol_index]

func _duty_approach_tick(delta: float) -> void:
    while not duty_approach.is_empty() and global_position.distance_to(duty_approach[0])<13:
        duty_approach.remove_at(0)
        _route_timer=0
        _path.clear()
        _path_index=0
    if duty_approach.is_empty():
        duty_arrived=true
        _return_to_duty()
        velocity=Vector2.ZERO
    else:
        _follow(duty_approach[0],move_speed,delta)
        _separate_room_units(delta)
        if velocity.length_squared()>4: rotation=velocity.angle()
        move_and_slide()
    _update_visual(delta)

func _separate_room_units(delta: float) -> void:
    if raid_role==&"" or velocity.length_squared()<4: return
    var away:=Vector2.ZERO
    for other in world._enemies:
        if other==self or not is_instance_valid(other) or other.state==State.DEAD: continue
        var offset: Vector2=global_position-other.global_position
        var distance:=offset.length()
        if distance>.1 and distance<23:
            away+=offset/distance*(23-distance)/23
    if away.length_squared()<.001: return
    var desired:=velocity+away*move_speed*.8
    if world.is_body_path_clear(global_position,global_position+desired.normalized()*14):
        velocity=velocity.move_toward(desired,480*delta)
