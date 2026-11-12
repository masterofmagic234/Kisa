extends RefCounted
const Data = preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")

const MAX_SEGMENTS := 384
const LIFETIME := 22.0
const SAMPLE_DISTANCE := 0.40
const HALF_TRACK := 0.67
const HALF_TIRE_WIDTH := 0.085

var segments: Array = []
var clock: float = 0.0
var last_points: Array[Vector2] = []
var last_position := Vector2.ZERO
var last_strength: float = 0.0
var last_on_asphalt: Array[bool] = [false, false]

func update(car, track_size: int, segment_length: float, delta: float) -> void:
    clock += maxf(delta, 0.0)
    while not segments.is_empty() and clock - float(segments[0].time) > LIFETIME:
        segments.pop_front()
    var strength := float(car.tyre_skid_strength)
    if strength < 0.12:
        last_points.clear()
        return
    var z := float(car.progress(track_size)) * segment_length - float(car.grid_world_z_offset)
    var position := Vector2(float(car.world_x), z)
    var travelled := position.distance_to(last_position)
    if not last_points.is_empty() and travelled < SAMPLE_DISTANCE:
        return
    var yaw := float(car.heading_yaw)
    var forward := Vector2(sin(yaw), cos(yaw))
    var right := Vector2(cos(yaw), -sin(yaw))
    var points: Array[Vector2] = []
    var on_asphalt: Array[bool] = []
    var road_centre := float(car.world_x) - float(car.lateral_offset)
    for side in [-1.0, 1.0]:
        var centre: Vector2 = position + forward * 0.45 + right * HALF_TRACK * float(side)
        points.append(centre - right * HALF_TIRE_WIDTH)
        points.append(centre + right * HALF_TIRE_WIDTH)
        on_asphalt.append(absf(centre.x - road_centre) <= Data.ROAD_WIDTH * 0.5)
    if last_points.size() == 4 and travelled < 8.0:
        for tire in range(2):
            if not on_asphalt[tire] or not last_on_asphalt[tire]:
                continue
            var index := tire * 2
            segments.append({"points": PackedVector2Array([last_points[index], last_points[index + 1], points[index + 1], points[index]]), "strength": minf(strength, last_strength), "time": clock})
    while segments.size() > MAX_SEGMENTS:
        segments.pop_front()
    last_points = points
    last_position = position
    last_strength = strength
    last_on_asphalt = on_asphalt
