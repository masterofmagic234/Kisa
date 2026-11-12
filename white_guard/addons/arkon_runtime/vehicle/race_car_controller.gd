extends RefCounted

# Состояние одной машины Level 2.

const RaceMath = preload("res://addons/arkon_runtime/vehicle/race_math.gd")
const RaceLevelData = preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")
const Powertrain = preload("res://addons/arkon_runtime/vehicle/race_powertrain.gd")
const Barriers = preload("res://addons/arkon_runtime/vehicle/race_barriers.gd")

var is_player: bool = false
var world_x: float = 0.0
var world_z: float = 0.0
var speed: float = 0.0
var steer_in: float = 0.0
var throttle: float = 0.0
var brake_in: float = 0.0
var grid_index: int = 0
# The start-grid offset is visual world-space separation only. Progress remains
# anchored at segment 0 so race ordering and renderer projection stay unchanged.
var grid_world_z_offset: float = 0.0
var segment_index: int = 0
var segment_progress: float = 0.0
var last_segment_index: int = -1
var lap: int = 0
var position: int = 1
var finish_time: float = -1.0
var sprite_yaw: float = 0.0
var lateral_offset: float = 0.0
var steer_applied: float = 0.0
var lateral_velocity: float = 0.0
var heading_yaw: float = 0.0
var travel_yaw: float = 0.0
var slip_angle: float = 0.0
var distance_travelled: float = 0.0
var longitudinal_acceleration: float = 0.0
var steering_angle: float = 0.0
var offroad_amount: float = 0.0
var _heading: float = 0.0
var _steering_lock := .52
var _track_size: int = 0
var _track_x := PackedFloat32Array()
var powertrain = Powertrain.new()
const REVERSE_HOLD_SECONDS := .65
var _reverse_hold := 0.0
var _reverse_active := false
var gear: int:
    get: return -1 if _reverse_active else powertrain.gear
var engine_rpm: float:
    get: return powertrain.engine_rpm
var engine_load: float:
    get: return powertrain.engine_load
var tyre_squeal: float = 0.0
var tyre_skid_strength: float = 0.0
var wall_contact: float = 0.0
var impact_velocity := 0.0
var puncture_remaining := 0.0
var speed_limit_multiplier := .92

func apply_side_impact(impulse: float, speed_loss: float) -> void:
    if speed<3: return
    impact_velocity=clampf(impact_velocity+impulse,-4.0,4.0)
    speed*=1.0-clampf(speed_loss,0,.12)

func hit_spikes() -> void:
    if puncture_remaining>0: return
    puncture_remaining=3.6
    speed*=.62

func resolve_contact(correction: Vector2,pattern: Array,track: PackedFloat32Array) -> void:
    var motion := Barriers.sweep(Vector2(world_x,world_z),Vector2(world_x,world_z)+correction,pattern,track,_heading)
    # Persist the corrected position in the canonical route state. A world-only
    # fix would be undone by the very next vehicle tick.
    var canonical := (motion.y+grid_world_z_offset)/RaceLevelData.SEGMENT_HEIGHT
    var count := pattern.size()
    lap=floori(canonical/count)
    segment_index=posmod(floori(canonical),count)
    segment_progress=canonical-floor(canonical)
    world_z=canonical*RaceLevelData.SEGMENT_HEIGHT-grid_world_z_offset
    world_x=motion.x
    lateral_offset=world_x-road_center_at(world_z)
    wall_contact=maxf(wall_contact,motion.z)

func setup(player_flag: bool) -> void:
    is_player = player_flag

func contact_velocity() -> Vector2:
    return Vector2(lateral_velocity+impact_velocity,signf(speed)*sqrt(maxf(0.0,speed*speed-lateral_velocity*lateral_velocity)))

func apply_contact_velocity(value: Vector2) -> void:
    # A contact cannot invent reverse while driving forward. Once the driver
    # has selected the crawl, signed velocity is retained and bounded to1km/h.
    lateral_velocity=value.x
    var z := value.y if _reverse_active else maxf(0.0,value.y)
    speed=Vector2(value.x,z).length()
    if z<0.0: speed=-minf(speed,Powertrain.REVERSE_MAX_SPEED)

func _step_drive(dt: float, enabled: bool, drive_limit: float) -> float:
    if not is_player:
        return powertrain.step(speed,throttle if enabled else 0.0,brake_in if enabled else .55,dt,drive_limit)
    var reverse_requested := enabled and brake_in>.5 and throttle<=.01
    if speed<-.00001:
        if reverse_requested:
            _reverse_active=true;_reverse_hold=REVERSE_HOLD_SECONDS
            var crawl_limit := Powertrain.REVERSE_MAX_SPEED*lerpf(1.0,.75,offroad_amount)
            if puncture_remaining>0.0: crawl_limit*=.64
            return powertrain.reverse_step(speed,dt,crawl_limit)
        _reverse_active=false;_reverse_hold=0.0
        powertrain.step(0.0,0.0,.55,dt,drive_limit)
        return move_toward(speed,0.0,Powertrain.REVERSE_MAX_SPEED*3.0*dt)
    _reverse_active=false
    if speed>.00001: _reverse_hold=0.0
    var next_speed: float=powertrain.step(speed,throttle if enabled else 0.0,brake_in if enabled else .55,dt,drive_limit)
    if reverse_requested and next_speed<=.00001:
        _reverse_hold+=dt
        if _reverse_hold>=REVERSE_HOLD_SECONDS:
            _reverse_active=true
            return powertrain.reverse_step(0.0,dt)
    else: _reverse_hold=0.0
    return next_speed

func place_on_grid(grid_slot: int, lane_x: float, track_x: PackedFloat32Array) -> void:
    grid_index = grid_slot
    segment_index = 0
    segment_progress = 0.0
    lateral_offset = lane_x
    _track_size=track_x.size()
    _track_x=track_x
    grid_world_z_offset = float(grid_slot) * RaceLevelData.GRID_SPACING
    world_x = RaceMath.track_center_x(-grid_world_z_offset/RaceLevelData.SEGMENT_HEIGHT,track_x)+lateral_offset
    world_z = -grid_world_z_offset

func set_inputs(steer: float, th: float, br: float) -> void:
    steer_in = clampf(steer, -1.0, 1.0)
    throttle = clampf(th, 0.0, 1.0)
    brake_in = clampf(br, 0.0, 1.0)
    if RaceLevelData.ACTIVE_HANDLING_PROFILE == RaceLevelData.HandlingProfile.NES_TRIBUTE:
        steer_applied = steer_in

func tick(delta: float, allow_control: bool, track_pattern: Array, track_x: PackedFloat32Array) -> void:
    _track_x=track_x
    var max_speed: float = RaceLevelData.PLAYER_MAX_SPEED
    if not is_player:
        max_speed *= speed_limit_multiplier

    var grip_demand := 0.0
    var previous_speed := speed
    var previous_center := RaceMath.track_center_x(track_phase(), track_x)
    var previous_offset := lateral_offset
    var previous_z := track_phase()*RaceLevelData.SEGMENT_HEIGHT
    var dt := maxf(delta, 0.0)
    puncture_remaining=maxf(0,puncture_remaining-dt)
    if allow_control:
        var drive_limit := max_speed
        if puncture_remaining>0: drive_limit*=.64
        var asphalt_edge := RaceLevelData.ROAD_WIDTH * 0.5 + RaceLevelData.OFFROAD_VEHICLE_HALF_WIDTH + RaceLevelData.OFFROAD_ASPHALT_MARGIN
        if absf(lateral_offset) > asphalt_edge:
            var shoulder := clampf((absf(lateral_offset) - asphalt_edge) / RaceLevelData.OFFROAD_SHOULDER, 0.0, 1.0)
            drive_limit *= lerpf(0.85, 0.55, shoulder)
        speed = _step_drive(dt,true,drive_limit)
        var speed_ratio := clampf(absf(speed) / max_speed, 0.0, 1.0)
        # One short, monotonic input filter. Chained steering/yaw springs made
        # the driver chase delayed corrections and weave even at low speed.
        var response := RaceLevelData.NFS_STEER_INPUT_RESPONSE
        if absf(steer_in)<absf(steer_applied): response*=1.35
        steer_applied=move_toward(steer_applied,steer_in,response*dt)
        _steering_lock=lerpf(_steering_lock,RaceLevelData.steering_lock(speed_ratio),1-exp(-6.0*dt))
        steering_angle = steer_applied * _steering_lock
        var moving := clampf((speed - RaceLevelData.NFS_LOW_SPEED_LATERAL_LOCK) / 3.0, 0.0, 1.0)
        var target_heading := steer_applied*RaceLevelData.NFS_MAX_HEADING_YAW*lerpf(1,.48,speed_ratio)
        var yaw_change := wrapf(target_heading-_heading,-PI,PI)*(1-exp(-RaceLevelData.NFS_HEADING_RESPONSE*moving*dt))
        var yaw_limit := RaceLevelData.CORNER_GRIP_ACCEL/maxf(speed,3.0)
        grip_demand=absf(yaw_change)/maxf(yaw_limit*dt,.00001)
        if speed<0.0:
            moving=clampf(absf(speed)/.16,0.0,1.0)
            _heading=clampf(_heading+(speed/2.55)*tan(steering_angle)*dt,-RaceLevelData.NFS_MAX_HEADING_YAW,RaceLevelData.NFS_MAX_HEADING_YAW)
        else: _heading+=clampf(yaw_change,-yaw_limit*dt,yaw_limit*dt)
        var desired_lateral := sin(_heading) * speed * moving
        var grip := RaceLevelData.NFS_LATERAL_GRIP_RESPONSE * lerpf(1.0, 0.90, speed_ratio)
        var previous_lateral_velocity := lateral_velocity
        lateral_velocity = lerpf(lateral_velocity, desired_lateral, 1.0 - exp(-grip * dt))
        if moving <= 0.0:
            # Wheels can steer at rest; the chassis cannot translate or yaw.
            lateral_velocity = 0.0
        if RaceLevelData.ACTIVE_HANDLING_PROFILE == RaceLevelData.HandlingProfile.NES_TRIBUTE and speed>=0.0:
            lateral_velocity = steer_in * speed * 0.28 * moving
            _heading = atan2(lateral_velocity, maxf(speed, 0.001))
        lateral_offset += (lateral_velocity+previous_lateral_velocity)*.5*dt if moving>0 else 0.0
    else:
        speed = _step_drive(dt,false,max_speed)
        lateral_velocity = 0.0
        steer_applied = move_toward(steer_applied, 0.0, 8.0 * dt)
        steering_angle = 0.0

    # Contacts add a short physical shove, never a new steering/camera filter.
    if allow_control and speed>3:
        lateral_offset+=impact_velocity*(1-exp(-3.2*dt))/3.2
    impact_velocity*=exp(-3.2*dt)
    if not allow_control or speed<=3: impact_velocity=0
    # speed is total speed; lateral travel cannot create extra forward speed.
    var forward_speed := signf(speed)*sqrt(maxf(speed * speed - lateral_velocity * lateral_velocity, 0.0))
    var motion := Barriers.sweep(Vector2(previous_center+previous_offset,previous_z),Vector2(previous_center+lateral_offset,previous_z+forward_speed*dt),track_pattern,track_x,_heading)
    wall_contact=motion.z
    if wall_contact>0.0:
        var minimum := RaceLevelData.OFFROAD_MIN_DRIVE_SPEED if allow_control and throttle>.01 and brake_in<.01 else 0.0
        speed=speed*.55 if speed<0.0 else maxf(minimum,speed*.55)
        lateral_velocity=(motion.x-previous_center-previous_offset)/maxf(dt,.0001)
    var advance: float = (motion.y-previous_z)/RaceLevelData.SEGMENT_HEIGHT
    segment_progress += advance
    while segment_progress >= 1.0:
        segment_progress -= 1.0
        last_segment_index = segment_index
        segment_index += 1
        if segment_index >= track_pattern.size():
            segment_index = 0
            lap += 1
    while segment_progress<0.0:
        segment_progress+=1.0
        last_segment_index=segment_index
        segment_index-=1
        if segment_index<0:
            segment_index=track_pattern.size()-1
            lap-=1

    var track_position := float(segment_index) + segment_progress
    var center: float = RaceMath.track_center_x(track_phase(), track_x)
    # Keep actual world-X motion independent of the road centre. A bend must
    # require steering rather than carrying the car around automatically.
    lateral_offset = motion.x-center
    var half: float = RaceLevelData.ROAD_WIDTH * 0.5
    var abs_lateral: float = absf(lateral_offset)
    # A car is not "off-road" merely because its center crossed the asphalt
    # edge. Allow for the vehicle half-width so speed loss starts when the
    # body actually reaches the shoulder.
    var road_edge_with_vehicle: float = (
        half
        + RaceLevelData.OFFROAD_VEHICLE_HALF_WIDTH
        + RaceLevelData.OFFROAD_ASPHALT_MARGIN
    )

    offroad_amount = clampf((abs_lateral - road_edge_with_vehicle) / RaceLevelData.OFFROAD_SHOULDER, 0.0, 1.0)
    if abs_lateral > road_edge_with_vehicle:
        var shoulder_progress: float = clampf(
            (abs_lateral - road_edge_with_vehicle) / maxf(RaceLevelData.OFFROAD_SHOULDER, 0.001),
            0.0,
            1.0
        )
        var penalty: float = lerpf(
            RaceLevelData.OFFROAD_SOFT_PENALTY,
            RaceLevelData.OFFROAD_HARD_PENALTY,
            shoulder_progress
        )
        var min_drive_speed := 0.0
        if allow_control and throttle > 0.01 and brake_in < 0.01:
            min_drive_speed = RaceLevelData.OFFROAD_MIN_DRIVE_SPEED
        # The existing penalty models high-speed shoulder loss. Reverse is
        # already a powered crawl, separately capped below1km/h on the verge.
        if speed>=0.0: speed = maxf(speed - penalty * dt, minf(speed, min_drive_speed))

    # The renderer and physics share the same continuous centerline. World X/Z
    # are presentation-space coordinates derived from that authoritative track
    # position rather than a discrete segment sample.
    world_x = center + lateral_offset

    # Preserve each car's physical start-grid offset as it accelerates out of
    # the line. Progress is still measured from the canonical race origin, so
    # this only prevents the render-space teleport from negative grid Z to 0.
    var canonical_world_z := progress(track_pattern.size()) * RaceLevelData.SEGMENT_HEIGHT
    world_z = canonical_world_z - grid_world_z_offset
    heading_yaw = _heading
    travel_yaw = atan2(lateral_velocity,forward_speed) if absf(speed)>.001 else heading_yaw
    var rolling_heading := wrapf(travel_yaw-PI,-PI,PI) if speed<0.0 else travel_yaw
    slip_angle = clampf(wrapf(heading_yaw - rolling_heading, -PI, PI), -0.20, 0.20)
    sprite_yaw = heading_yaw
    longitudinal_acceleration = (speed - previous_speed) / maxf(dt, 0.0001)
    distance_travelled += speed * dt
    # One tire-load signal drives both sound and rubber laid on asphalt.
    var speed_kmh := absf(speed) / RaceLevelData.SPEED_UNITS_PER_KMH
    var turn_squeal := maxf(smoothstep(.055,.12,absf(slip_angle)),smoothstep(1.5,3.0,grip_demand)*smoothstep(.65,.95,absf(steer_applied)))*smoothstep(110.0,170.0,speed_kmh)
    var brake_squeal := smoothstep(0.35, 0.95, brake_in) if allow_control else 0.0
    var tire_target := maxf(turn_squeal, brake_squeal) * smoothstep(8.0, 28.0, speed_kmh) * (1.0 - offroad_amount)
    tyre_squeal = lerpf(tyre_squeal, tire_target, 1.0 - exp(-14.0 * dt)) if speed_kmh > 1.0 else 0.0
    tyre_skid_strength = tyre_squeal if speed_kmh > 8.0 else 0.0

func progress(track_size: int) -> float:
    return float(lap) * float(track_size) + float(segment_index) + segment_progress

func physical_progress(track_size: int) -> float:
    return progress(track_size)-grid_world_z_offset/RaceLevelData.SEGMENT_HEIGHT

func track_phase() -> float:
    return progress(_track_size)-grid_world_z_offset/RaceLevelData.SEGMENT_HEIGHT

func road_center_at(z: float) -> float:
    return RaceMath.track_center_x(z/RaceLevelData.SEGMENT_HEIGHT,_track_x)
