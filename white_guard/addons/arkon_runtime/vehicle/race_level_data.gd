extends RefCounted

# Release vehicle calibration fixture; NOT the White Guard campaign. Race-prefixed owners retain the proven vehicle,
# camera and road math; reaching the city is an escape, not a ranking objective.

const SEGMENT_HEIGHT := 40.0
const ROAD_WIDTH := 9.0
const FIELD_FENCE_X := 7.8
const BRIDGE_RAIL_X := 5.5
const CURVE_RAIL_X := 8.0
const TUNNEL_RADIUS := 6.1
# A body is taller than its ground footprint: leave clearance under the vault.
const TUNNEL_BODY_CLEAR_X := 5.9
const LANE_OFFSETS := [-2.7, -0.9, 0.9, 2.7]

enum Seg { STRAIGHT, CURVE_L, CURVE_R, HAIRPIN_L, HAIRPIN_R, CHICANE }
enum HandlingProfile { NES_TRIBUTE, NFS_UNDERGROUND2 }

# Active Level2 feel. Switch to NES_TRIBUTE to keep the original tribute response
# while retaining the same pseudo-3D renderer and camera-state architecture.
const ACTIVE_HANDLING_PROFILE: int = HandlingProfile.NFS_UNDERGROUND2

const TRACK_DATA = preload("res://addons/arkon_runtime/vehicle/fixtures/race_track_default.tres")

static func get_track_pattern() -> Array:
    return TRACK_DATA.build_pattern()

const IS_SPRINT := true
# Nominal production-input drive; finishing remains physical, never timed.
const TARGET_RACE_SECONDS := 210.0
const TOTAL_LAPS := 1
const GRID_SPACING := 7.0
const PLAYER_GRID_SLOT := 3
# Boundaries are world metres. Objects choose their own sector, so the next
# location appears in the distance before the car actually enters it.
const BRIDGE_START := 3120.0
const BRIDGE_END := 4080.0
const TUNNEL_START := 5120.0
const TUNNEL_END := 6400.0
const OUTSKIRTS_START := TUNNEL_END
const CITY_START := 7440.0
const SECTOR_STARTS := [0.0, 1520.0, BRIDGE_START, BRIDGE_END, TUNNEL_START, OUTSKIRTS_START, CITY_START]
const SECTOR_NAMES := ["FIXTURE 0", "FIXTURE 1", "FIXTURE 2", "FIXTURE 3", "FIXTURE 4", "FIXTURE 5", "FIXTURE 6"]
# Metres, shared by the main camera, road, vehicles and roadside architecture.
const ELEVATION_Z := [0.0,2680.0,3120.0,3520.0,4080.0,4560.0,4820.0,5120.0,6400.0,6760.0,8880.0]
const ELEVATION_Y := [0.0,0.0,18.0,20.0,18.0,0.0,0.0,-10.0,-10.0,0.0,0.0]
static func road_height(world_z: float) -> float:
    for i in range(1,ELEVATION_Z.size()):
        if world_z<=ELEVATION_Z[i]:
            if is_equal_approx(ELEVATION_Y[i-1],ELEVATION_Y[i]): return ELEVATION_Y[i]
            var t := clampf((world_z-ELEVATION_Z[i-1])/(ELEVATION_Z[i]-ELEVATION_Z[i-1]),0.0,1.0)
            return lerpf(ELEVATION_Y[i-1],ELEVATION_Y[i],(1.0-cos(PI*t))*0.5)
    return ELEVATION_Y[-1]
static func terrain_height(world_z: float) -> float:
    return 0.0 if world_z>=BRIDGE_END else road_height(world_z)

const VERGE_EDGE := ROAD_WIDTH*.5+.32
const CUT_WALL_X := 6.6
const EMBANKMENT_X := 24.0

static func ground_height(offset: float, world_z: float) -> float:
    var road := road_height(world_z)
    var terrain := terrain_height(world_z)
    return ground_height_cached(offset,world_z,road,terrain)

static func ground_height_cached(offset: float,world_z: float,road: float,terrain: float) -> float:
    var x := absf(offset)
    if world_z>=BRIDGE_START and world_z<BRIDGE_END:
        return road if x<=BRIDGE_RAIL_X else 0.0
    if road<terrain-.001:
        return road if x<CUT_WALL_X else terrain
    return lerpf(road,terrain,clampf((x-VERGE_EDGE)/(EMBANKMENT_X-VERGE_EDGE),0,1))

static func verge_profile(world_z: float, outer: float) -> Array[Vector2]:
    var road := road_height(world_z)
    return verge_profile_cached(world_z,outer,road,0.0 if world_z>=BRIDGE_END else road)

static func verge_profile_cached(world_z: float,outer: float,road: float,terrain: float) -> Array[Vector2]:
    if world_z>=BRIDGE_START and world_z<BRIDGE_END:
        return [Vector2(VERGE_EDGE,road),Vector2(BRIDGE_RAIL_X,road),Vector2(BRIDGE_RAIL_X,0),Vector2(outer,0)]
    if road<terrain-.001:
        return [Vector2(VERGE_EDGE,road),Vector2(CUT_WALL_X,road),Vector2(CUT_WALL_X,terrain),Vector2(outer,terrain)]
    return [Vector2(VERGE_EDGE,road),Vector2(VERGE_EDGE+(EMBANKMENT_X-VERGE_EDGE)*.5,lerpf(road,terrain,.5)),Vector2(EMBANKMENT_X,terrain),Vector2(outer,terrain)]

static func road_grade(world_z: float) -> float:
    for i in range(1,ELEVATION_Z.size()):
        if world_z<=ELEVATION_Z[i]:
            var span: float=ELEVATION_Z[i]-ELEVATION_Z[i-1]
            var t := clampf((world_z-ELEVATION_Z[i-1])/span,0.0,1.0)
            return (ELEVATION_Y[i]-ELEVATION_Y[i-1])*PI*sin(PI*t)/(2.0*span)
    return 0.0
static func sector_at(world_z: float) -> int:
    for i in range(SECTOR_STARTS.size()-1,-1,-1):
        if world_z>=SECTOR_STARTS[i]: return i
    return 0
static func route_length() -> float:
    return TRACK_DATA.total_segments()*SEGMENT_HEIGHT
const RACER_COUNT := 4

const PLAYER_MAX_SPEED_KMH := 219.0
# The previous 32.0 simulation units represented 100 km/h.
const SPEED_UNITS_PER_KMH := 32.0 / 100.0
const PLAYER_MAX_SPEED := PLAYER_MAX_SPEED_KMH * SPEED_UNITS_PER_KMH
const PLAYER_ACCEL := 16.0
const PLAYER_BRAKE := 40.0
const PLAYER_DRAG := 0.55
const PLAYER_STEER_RATE := 6.4
const NFS_STEER_INPUT_RESPONSE := 8.0
const NFS_LATERAL_GRIP_RESPONSE := 15.0
const NFS_LOW_SPEED_LATERAL_LOCK := 0.8
const NFS_HEADING_RESPONSE := 7.0
# Heading is measured clockwise from +Z, like movement and projection.
const NFS_MAX_HEADING_YAW := 0.84
const CORNER_GRIP_ACCEL := 12.0

static func steering_lock(speed_ratio: float) -> float:
    return lerpf(.52,.22,clampf(speed_ratio,0,1))

static func steer_for_heading(heading: float, speed_ratio: float) -> float:
    return clampf(heading/(NFS_MAX_HEADING_YAW*lerpf(1,.48,clampf(speed_ratio,0,1))),-1,1)

# Fixed simulation in _physics_process; one interpolated pose for all rendering.
const SIMULATION_HZ := 60.0
const SIMULATION_STEP := 1.0 / SIMULATION_HZ
const MAX_FRAME_DELTA := 0.25

# Relative-lateral arcade handling.
# Positive curve bends right; the car is pushed outward to the left.
const CENTRIFUGAL_FORCE := 0.022

const PLAYER_STEER_RESPONSE := 9.0

const OFFROAD_VEHICLE_HALF_WIDTH := 0.85
const OFFROAD_ASPHALT_MARGIN := 0.25
const OFFROAD_SHOULDER := 2.2
const OFFROAD_SOFT_PENALTY := 4.0
const OFFROAD_HARD_PENALTY := 8.0
const OFFROAD_MIN_DRIVE_SPEED := 6.0

const AI_SKILLS := [0.86, 0.78, 0.70]
const AI_LOOKAHEAD := 8

const ACORN_PICKUPS := [
    [12, 0], [22, 3], [40, 1], [58, 2], [72, 0], [90, 3], [110, 1],
]
