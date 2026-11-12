extends RefCounted
class_name ArkonRaceCameraState

const RaceLevelData = preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")
const BEHIND := 6.0
const FOLLOW_RESPONSE := 7.5

# +Z is forward, +X is right. This is the ONLY gameplay camera owner.
# The renderer and model viewport consume the same yaw, roll and zoom.
var lateral_offset: float = 0.0
var target_lateral_offset: float = 0.0
var behind_distance: float = BEHIND
var steer: float = 0.0
var speed_ratio: float = 0.0
var yaw_offset: float = 0.0
var roll: float = 0.0
var zoom: float = 1.0
var horizon_offset: float = 0.0
var road_height: float = 0.0
var road_grade: float = 0.0
var _boom_world_x := 0.0
var _boom_initialized := false

func reset(race_car) -> void:
    lateral_offset = 0.0
    target_lateral_offset = 0.0
    behind_distance = BEHIND
    steer = 0.0
    speed_ratio = 0.0
    yaw_offset = 0.0
    roll = 0.0
    zoom = 1.0
    horizon_offset = 0.0
    _boom_initialized=false
    if race_car != null:
        update_from_race_car(race_car, 0.0)
        lateral_offset = target_lateral_offset

func update_from_race_car(race_car, delta: float) -> void:
    if race_car == null:
        return
    steer = clampf(float(race_car.steer_applied), -1.0, 1.0)
    speed_ratio = clampf(float(race_car.speed) / RaceLevelData.PLAYER_MAX_SPEED, 0.0, 1.0)
    road_height = RaceLevelData.road_height(float(race_car.world_z))
    road_grade = RaceLevelData.road_grade(float(race_car.world_z))
    # Direction is bound to the nose. Only the camera boom position has inertia.
    yaw_offset = float(race_car.heading_yaw)
    behind_distance = BEHIND * cos(yaw_offset)
    target_lateral_offset = float(race_car.lateral_offset) - BEHIND * sin(yaw_offset)
    var blend := 1.0 - exp(-FOLLOW_RESPONSE * maxf(delta, 0.0))
    # Smooth the boom in world coordinates. Smoothing a road-relative offset
    # and adding the *new* centerline drags the camera into every bend even
    # while the driver and chassis hold a straight, neutral-input trajectory.
    var center: float=float(race_car.world_x)-float(race_car.lateral_offset)
    var target_world_x: float=center+target_lateral_offset
    if not _boom_initialized:
        _boom_world_x=target_world_x;_boom_initialized=true
    else:_boom_world_x=lerpf(_boom_world_x,target_world_x,blend)
    lateral_offset=_boom_world_x-center
    var camera_z: float=float(race_car.world_z)-behind_distance
    if race_car._track_size==RaceLevelData.TRACK_DATA.total_segments() and camera_z>=RaceLevelData.TUNNEL_START and camera_z<RaceLevelData.TUNNEL_END:
        var center_shift: float=race_car.road_center_at(camera_z)-race_car.road_center_at(float(race_car.world_z))
        var clearance := RaceLevelData.TUNNEL_RADIUS-.25
        target_lateral_offset=clampf(target_lateral_offset,center_shift-clearance,center_shift+clearance)
        lateral_offset=clampf(lateral_offset,center_shift-clearance,center_shift+clearance)
        _boom_world_x=center+lateral_offset
    roll = lerpf(roll, -steer * speed_ratio * deg_to_rad(1.0), blend)
    zoom = lerpf(zoom, lerpf(1.0, 0.94, speed_ratio), blend)
    horizon_offset = lerpf(horizon_offset, clampf(float(race_car.longitudinal_acceleration) / 40.0, -1.0, 1.0) * 0.008, blend)
