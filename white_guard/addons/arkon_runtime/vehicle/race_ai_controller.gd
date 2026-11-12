extends RefCounted
# Pursuit decisions feed the same steering, powertrain and solid body as the
# player. A passing car must physically earn its lead before it can brake.
const RaceMath=preload("res://addons/arkon_runtime/vehicle/race_math.gd")
const RaceLevelData=preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")
const ArkonRaceAiAssist=preload("res://addons/arkon_runtime/vehicle/race_ai_assist.gd")
var car
var skill: float=.8
var lane_bias: float=0
var track_pattern: Array
var track_x: PackedFloat32Array
var pursuit_target
var pursuit_allowed := false
var pursuit_role := 0
var attack_kind: StringName=&"ram"
var attack_phase: StringName=&"follow"
var attack_time := 0.0
var attack_cooldown := 1.6
var attack_side := 1.0
var evasion_lane := INF
var support_gap := -INF
var traffic_obstacles: Array[Vector2]=[]
var maneuver_serial := 0
var brake_serial := 0
var brake_lane := 0.0
var brake_checks := 0

func recover_from_ram() -> void:
    attack_phase=&"recover";attack_time=.42;attack_cooldown=1.25

func setup(car_ref,pattern: Array,tx: PackedFloat32Array,skill_: float,bias: float) -> void:
    car=car_ref;track_pattern=pattern;track_x=tx
    skill=clampf(skill_,0,1);lane_bias=clampf(bias,-1,1)

func busy() -> bool:
    return attack_phase not in [&"follow",&"recover"]

func brake_maneuver() -> bool:
    return attack_phase in [&"pass",&"merge",&"brake_warn",&"brake",&"release"]

func _open_road() -> bool:
    var z: float=car.world_z
    if z>RaceLevelData.TUNNEL_START-350 and z<RaceLevelData.TUNNEL_END+150: return false
    if z>RaceLevelData.route_length()-400 or absf(RaceLevelData.road_grade(z))>.035: return false
    for ahead in [0,80,160]:
        if RaceMath.safe_corner_speed((z+ahead)/40,track_x,RaceLevelData.PLAYER_MAX_SPEED,RaceLevelData.NFS_MAX_HEADING_YAW,RaceLevelData.CORNER_GRIP_ACCEL)<RaceLevelData.PLAYER_MAX_SPEED*.60: return false
    return true

func _decide(delta: float,dz: float) -> void:
    attack_cooldown=maxf(0,attack_cooldown-delta);attack_time=maxf(0,attack_time-delta)
    if not pursuit_allowed and busy(): recover_from_ram()
    var near_tunnel: bool=car.world_z>RaceLevelData.TUNNEL_START-200 and car.world_z<RaceLevelData.TUNNEL_END+100
    if brake_maneuver() and (is_finite(evasion_lane) or near_tunnel or pursuit_target.speed<14): recover_from_ram()
    if attack_phase in [&"brake_warn",&"brake"] and not _open_road(): recover_from_ram()
    if attack_phase==&"follow" and pursuit_allowed and attack_cooldown<=0 and car.speed>18:
        if attack_kind==&"brake_check" and dz> -25 and dz<15 and _open_road():
            brake_lane=clampf(pursuit_target.lateral_offset,-1.4,1.4)
            attack_side=1.0 if pursuit_target.lateral_offset<1.7 else -1.0
            attack_phase=&"pass";attack_time=18.0;maneuver_serial+=1
        elif attack_kind==&"ram" and absf(dz)<12:
            attack_side=signf(car.lateral_offset-pursuit_target.lateral_offset)
            if attack_side==0: attack_side=1.0 if pursuit_role%2==0 else -1.0
            attack_phase=&"warn";attack_time=.6;maneuver_serial+=1
    elif attack_phase==&"pass":
        if dz>13:
            attack_phase=&"merge";attack_time=3.0
        elif attack_time<=0: recover_from_ram()
    elif attack_phase==&"merge":
        if dz>10 and absf(car.lateral_offset-brake_lane)<.65 and _open_road():
            attack_phase=&"brake_warn";attack_time=1.15;brake_serial+=1
        elif attack_time<=0 or dz<5: recover_from_ram()
    elif attack_phase==&"brake_warn" and attack_time<=0:
        if dz>8 and dz<26:
            attack_phase=&"brake";attack_time=1.25;brake_checks+=1
        else: recover_from_ram()
    elif attack_phase==&"brake" and (attack_time<=0 or dz< -5):
        attack_phase=&"release";attack_time=1.4;attack_cooldown=9
    elif attack_phase==&"release" and attack_time<=0: recover_from_ram()
    elif attack_phase==&"warn" and attack_time<=0:
        attack_phase=&"ram";attack_time=2.1
    elif attack_phase==&"ram" and attack_time<=0: recover_from_ram()
    elif attack_phase==&"recover" and attack_time<=0: attack_phase=&"follow"

func tick(delta: float,player_progress: float=-INF) -> void:
    if car==null: return
    var n := track_pattern.size()
    var effective_skill := ArkonRaceAiAssist.effective_skill(skill,car.physical_progress(n),player_progress,n)
    var at: float=car.track_phase()
    var available_speed: float=RaceLevelData.PLAYER_MAX_SPEED*car.speed_limit_multiplier
    var ratio := clampf(float(car.speed)/available_speed,0,1)
    var tangent := atan2(RaceMath.track_center_slope(at+lerpf(.10,.42,ratio),track_x),40)
    var center := RaceMath.track_center_x(at,track_x)
    var lane := lane_bias*2.0
    var target_speed := RaceMath.safe_corner_speed(at,track_x,RaceLevelData.PLAYER_MAX_SPEED*.92,RaceLevelData.NFS_MAX_HEADING_YAW,RaceLevelData.CORNER_GRIP_ACCEL)*lerpf(.93,1,effective_skill)
    if pursuit_target!=null:
        var dz: float=car.world_z-pursuit_target.world_z
        _decide(delta,dz)
        lane=clampf(pursuit_target.lateral_offset+[-2.45,2.45,0.0][pursuit_role%3],-3.35,3.35)
        var desired_gap: float=[9.0,15.0,22.0][pursuit_role%3]
        if is_finite(support_gap): desired_gap=support_gap
        if attack_phase==&"follow" and absf(lane-pursuit_target.lateral_offset)<2.1 and absf(desired_gap)<4.8:
            desired_gap=8.0 if dz>0 else -8.0
        if attack_phase in [&"warn",&"ram"]:
            lane=clampf(pursuit_target.lateral_offset+(2.3*attack_side if attack_phase==&"warn" else -.22*attack_side),-3.35,3.35);desired_gap=0
        elif attack_phase==&"pass": lane=clampf(brake_lane+attack_side*2.8,-4.1,4.1);desired_gap=18
        elif attack_phase in [&"merge",&"brake_warn",&"brake"]: lane=brake_lane;desired_gap=15
        elif attack_phase in [&"recover",&"release"]: lane=attack_side*3.3;desired_gap=-5
        if is_finite(support_gap) and dz>3 and not busy() and absf(lane-pursuit_target.lateral_offset)<2.1:
            lane=2.9 if pursuit_target.lateral_offset<0 else -2.9
        if is_finite(evasion_lane):
            lane=evasion_lane
            # A car already ahead clears the rack first. Braking to get behind
            # the player in the SAME escape lane would create a rolling trap.
            desired_gap=24 if dz>3 else -12-pursuit_role*7
        for obstacle in traffic_obstacles:
            if obstacle.y< -8 or obstacle.y>maxf(170,car.speed*3.5):continue
            if absf(lane-obstacle.x)>2.15 and absf(car.lateral_offset-obstacle.x)>2.15:continue
            # Opposing traffic has no enemy immunity. Yield on the right or
            # pass the target on its clear shoulder instead of crashing into
            # a civilian and pinning the entire pursuit behind it.
            if attack_phase==&"pass" and brake_lane<1.7:
                attack_side=1;lane=clampf(brake_lane+2.8,-4.1,4.1)
            else:
                if busy():recover_from_ram()
                lane=clampf(obstacle.x+3.4,1.4,3.4)
                desired_gap=minf(desired_gap,-8-pursuit_role*7)
            break
        var chase_speed: float=clampf(pursuit_target.speed+(desired_gap-dz)*1.4,8,available_speed)
        var corner_limit := RaceMath.safe_corner_speed(at,track_x,available_speed,RaceLevelData.NFS_MAX_HEADING_YAW,RaceLevelData.CORNER_GRIP_ACCEL)
        # Account for the speed governor's throttle deadband. The passing car
        # commits harder through a gentle bend; chassis/grip/top speed stay the
        # same, and its real steering must still hold the lane.
        if attack_phase==&"pass": corner_limit=minf(available_speed,corner_limit+3.5)
        target_speed=minf(chase_speed,corner_limit)
    var target_yaw := tangent+clampf((center+lane-float(car.world_x))*.07,-.13,.13)
    var steer := clampf(RaceLevelData.steer_for_heading(target_yaw,ratio),-.97,.97)
    var brake := clampf((float(car.speed)-target_speed)/8,0,.65)
    var throttle := clampf((target_speed-float(car.speed))/4+.72,0,1)
    if attack_phase==&"brake": brake=.78;throttle=0
    car.set_inputs(steer,throttle,brake)
