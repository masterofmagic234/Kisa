extends RefCounted

# A tuned 1990s four-cylinder with a four-speed torque-converter automatic.
# SI units are used here; renderer/handling coordinates retain their old scale.
const Data = preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")
const RATIOS := [2.785, 1.545, 1.000, 0.694]
const FINAL_DRIVE := 4.083
const TIRE_RADIUS := 0.30
const MASS_KG := 1280.0
const IDLE_RPM := 850.0
const REDLINE_RPM := 6500.0
const SIM_UNITS_PER_MPS := Data.SPEED_UNITS_PER_KMH * 3.6
const TORQUE_CURVE := [[850.0, 135.0], [2000.0, 185.0], [3500.0, 225.0], [4500.0, 230.0], [5500.0, 205.0], [6500.0, 170.0]]
const REVERSE_MAX_SPEED := Data.SPEED_UNITS_PER_KMH # Exactly1 km/h in race units.
const REVERSE_ACCELERATION := REVERSE_MAX_SPEED * 1.8

# Default is the unchanged player engine. Pursuit vehicles may configure a
# modest engine tune; torque, gearbox and drag still earn every metre.
var top_speed_limit: float = Data.PLAYER_MAX_SPEED
var torque_multiplier: float = 1.0
var gear: int = 1
var engine_rpm: float = IDLE_RPM
var engine_load: float = 0.0
var shift_remaining: float = 0.0
var shift_duration: float = 0.32
var shift_serial: int = 0
var shift_up: bool = true
var _shift_start_rpm: float = IDLE_RPM
var _shift_cooldown: float = 0.0

func road_rpm(mps: float, in_gear: int) -> float:
    return mps / TIRE_RADIUS * float(RATIOS[in_gear - 1]) * FINAL_DRIVE * 60.0 / TAU

func torque_at(rpm: float) -> float:
    for i in range(1, TORQUE_CURVE.size()):
        if rpm <= float(TORQUE_CURVE[i][0]):
            var a: Array = TORQUE_CURVE[i - 1]
            var b: Array = TORQUE_CURVE[i]
            return lerpf(float(a[1]), float(b[1]), clampf((rpm - float(a[0])) / (float(b[0]) - float(a[0])), 0.0, 1.0))
    return float(TORQUE_CURVE[-1][1])

func _shift(to_gear: int) -> void:
    shift_up = to_gear > gear
    _shift_start_rpm = engine_rpm
    gear = to_gear
    shift_duration = 0.32 if shift_up else 0.24
    shift_remaining = shift_duration
    _shift_cooldown = 0.70
    shift_serial += 1

func step(speed: float, throttle: float, brake: float, dt: float, drive_limit: float) -> float:
    if dt <= 0.0:
        return speed
    var mps := maxf(speed / SIM_UNITS_PER_MPS, 0.0)
    var pedal := clampf(throttle, 0.0, 1.0) if brake <= 0.01 else 0.0
    _shift_cooldown = maxf(_shift_cooldown - dt, 0.0)
    var coupled_rpm := road_rpm(mps, gear)
    if mps < 0.15 and pedal < 0.01:
        gear = 1
        shift_remaining = 0.0
        coupled_rpm = 0.0
    elif shift_remaining <= 0.0 and _shift_cooldown <= 0.0:
        var up_rpm := lerpf(2700.0, 6100.0, pedal)
        if gear < RATIOS.size() and coupled_rpm > up_rpm:
            _shift(gear + 1)
        elif gear > 1:
            var lower_rpm := road_rpm(mps, gear - 1)
            if coupled_rpm < 1400.0 or (pedal > 0.85 and coupled_rpm < 2900.0 and lower_rpm < 5600.0):
                _shift(gear - 1)
    coupled_rpm = road_rpm(mps, gear)
    var converter_slip := 1600.0 * pedal * (1.0 - smoothstep(2.0, 18.0, mps))
    var target_rpm := clampf(maxf(IDLE_RPM, coupled_rpm + converter_slip), IDLE_RPM, REDLINE_RPM)
    var torque_transfer := 1.0
    if shift_remaining > 0.0:
        shift_remaining = maxf(shift_remaining - dt, 0.0)
        var progress := 1.0 - shift_remaining / shift_duration
        engine_rpm = lerpf(_shift_start_rpm, target_rpm, smoothstep(0.0, 1.0, progress))
        torque_transfer = lerpf(0.22, 1.0, smoothstep(0.35, 1.0, progress))
    else:
        engine_rpm = lerpf(engine_rpm, target_rpm, 1.0 - exp(-10.0 * dt))
    engine_load = pedal * torque_transfer
    var force := torque_at(engine_rpm) * float(RATIOS[gear - 1]) * FINAL_DRIVE * 0.86 / TIRE_RADIUS * engine_load
    force *= lerpf(1.30, 1.0, smoothstep(0.0, 10.0, mps))
    force = minf(force*torque_multiplier, MASS_KG * 4.3)
    # A shoulder limit cuts drive torque; it never teleports speed to the cap.
    if speed > drive_limit + 0.01:
        force = 0.0
    var aero := 0.5 * 1.225 * 0.62 * mps * mps
    var rolling := MASS_KG * 9.81 * 0.013 * smoothstep(0.0, 0.8, mps)
    var engine_braking := (1.0 - pedal) * clampf((coupled_rpm - IDLE_RPM) / 4500.0, 0.0, 1.0) * MASS_KG * 0.65
    var acceleration := (force - aero - rolling - engine_braking) / MASS_KG - clampf(brake, 0.0, 1.0) * 8.8
    return clampf((mps + acceleration * dt) * SIM_UNITS_PER_MPS, 0.0, top_speed_limit)

func reverse_step(speed: float, dt: float, limit: float=REVERSE_MAX_SPEED) -> float:
    # The emergency crawl is selected by the player's brake/stop interlock,
    # never by pursuit AI or collision impulses. The normal forward engine is
    # deliberately unchanged.
    gear=1;shift_remaining=0.0
    engine_rpm=lerpf(engine_rpm,950.0,1.0-exp(-8.0*maxf(dt,0.0)))
    engine_load=.12
    return move_toward(minf(speed,0.0),-clampf(limit,0.0,REVERSE_MAX_SPEED),REVERSE_ACCELERATION*maxf(dt,0.0))
