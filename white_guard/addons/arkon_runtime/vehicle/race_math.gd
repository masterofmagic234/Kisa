extends RefCounted

# Level 2 — pure race mathematics.
# No scene, UI, or global-state ownership lives here.

const CURVE_TABLE := {
    0: 0.0,
    1: -1.6,
    2: 1.6,
    3: -3.2,
    4: 3.2,
    5: 2.2,
}

const TURN_SHIFT := {
    1: -56.0,
    2: 56.0,
    3: -72.0,
    4: 72.0,
}

static func curve_of(seg: int) -> float:
    return float(CURVE_TABLE.get(seg, 0.0))

static func accumulate_track_x(pattern: Array) -> PackedFloat32Array:
    var n := pattern.size()
    var out := PackedFloat32Array()
    out.resize(n)

    if n == 0:
        return out

    var i := 0
    var current_x := 0.0

    while i < n:
        var seg_type: int = int(pattern[i])
        var j := i + 1
        while j < n and int(pattern[j]) == seg_type:
            j += 1

        if TURN_SHIFT.has(seg_type):
            var delta_x: float = float(TURN_SHIFT[seg_type])
            var block_len: int = j - i
            for k in block_len:
                var t: float = float(k + 1) / float(block_len)
                var eased := (1.0 - cos(t * PI)) * 0.5
                out[i + k] = current_x + delta_x * eased
            current_x += delta_x
        else:
            for k in j - i:
                out[i + k] = current_x

        i = j

    # A closed track is an authoring invariant. Do not silently bend the
    # centerline to compensate for an invalid sum of turn shifts.
    return out

static func track_closure_error(pattern: Array) -> float:
    var total := 0.0
    var i := 0

    while i < pattern.size():
        var seg_type: int = int(pattern[i])
        var j := i + 1
        while j < pattern.size() and int(pattern[j]) == seg_type:
            j += 1

        if TURN_SHIFT.has(seg_type):
            total += float(TURN_SHIFT[seg_type])

        i = j

    return total

static func track_elevation(track_position: float, track_size: int) -> float:
    if track_size <= 0:
        return 0.0

    var ratio := fposmod(
        track_position,
        float(track_size)
    ) / float(track_size)

    # Gentle physical undulation keeps the asphalt alive without turning the
    # race into a roller coaster. Start and finish share the same height.
    return (
        sin(ratio * TAU * 2.0) * 1.6
        + sin(ratio * TAU * 5.0) * 0.45
    )

const STADIUM_RADIUS := 190.0

static func _stadium_base_point(
        track_position: float,
        track_size: int,
        segment_height: float
) -> Vector3:
    if track_size <= 0:
        return Vector3.ZERO

    var total_length := float(track_size) * maxf(segment_height, 0.001)
    var half_straight := maxf(
        1.0,
        (total_length - TAU * STADIUM_RADIUS) * 0.25
    )
    var wrapped := fposmod(
        track_position,
        float(track_size)
    )
    var distance := (
        wrapped / float(track_size)
        * total_length
    )

    var straight_length := half_straight * 2.0
    var arc_length := PI * STADIUM_RADIUS

    if distance < straight_length:
        return Vector3(
            STADIUM_RADIUS,
            0.0,
            -half_straight + distance
        )

    distance -= straight_length
    if distance < arc_length:
        var theta := distance / STADIUM_RADIUS
        return Vector3(
            STADIUM_RADIUS * cos(theta),
            0.0,
            half_straight + STADIUM_RADIUS * sin(theta)
        )

    distance -= arc_length
    if distance < straight_length:
        return Vector3(
            -STADIUM_RADIUS,
            0.0,
            half_straight - distance
        )

    distance -= straight_length
    var theta := PI + distance / STADIUM_RADIUS
    return Vector3(
        STADIUM_RADIUS * cos(theta),
        0.0,
        -half_straight + STADIUM_RADIUS * sin(theta)
    )

static func _stadium_base_tangent(
        track_position: float,
        track_size: int,
        segment_height: float
) -> Vector3:
    if track_size <= 0:
        return Vector3(0.0, 0.0, 1.0)

    var total_length := float(track_size) * maxf(segment_height, 0.001)
    var half_straight := maxf(
        1.0,
        (total_length - TAU * STADIUM_RADIUS) * 0.25
    )
    var wrapped := fposmod(
        track_position,
        float(track_size)
    )
    var distance := (
        wrapped / float(track_size)
        * total_length
    )

    var straight_length := half_straight * 2.0
    var arc_length := PI * STADIUM_RADIUS

    if distance < straight_length:
        return Vector3(0.0, 0.0, 1.0)

    distance -= straight_length
    if distance < arc_length:
        var theta := distance / STADIUM_RADIUS
        return Vector3(
            -sin(theta),
            0.0,
            cos(theta)
        ).normalized()

    distance -= arc_length
    if distance < straight_length:
        return Vector3(0.0, 0.0, -1.0)

    distance -= straight_length
    var theta := PI + distance / STADIUM_RADIUS
    return Vector3(
        -sin(theta),
        0.0,
        cos(theta)
    ).normalized()

static func track_world_position(
        track_position: float,
        track_x: PackedFloat32Array,
        track_size: int,
        segment_height: float
) -> Vector3:
    var base := _stadium_base_point(
        track_position,
        track_size,
        segment_height
    )
    var tangent := _stadium_base_tangent(
        track_position,
        track_size,
        segment_height
    )
    var right := Vector3.UP.cross(tangent)
    if right.length_squared() < 0.0001:
        right = Vector3(1.0, 0.0, 0.0)
    else:
        right = right.normalized()

    var lateral := track_center_x(
        track_position,
        track_x
    )
    base += right * lateral
    base.y = track_elevation(
        track_position,
        track_size
    )
    return base

static func track_world_tangent(
        track_position: float,
        track_x: PackedFloat32Array,
        track_size: int,
        segment_height: float
) -> Vector3:
    var sample := 0.02
    var p0 := track_world_position(
        track_position,
        track_x,
        track_size,
        segment_height
    )
    var p1 := track_world_position(
        track_position + sample,
        track_x,
        track_size,
        segment_height
    )
    var tangent := p1 - p0
    if tangent.length_squared() < 0.0001:
        tangent = _stadium_base_tangent(
            track_position,
            track_size,
            segment_height
        )
    return tangent.normalized()

static func nearest_track_progress(
        world_position: Vector3,
        track_x: PackedFloat32Array,
        track_size: int,
        segment_height: float,
        hint_progress: float = -1.0
) -> float:
    if track_size <= 0 or track_x.is_empty():
        return 0.0

    var best_progress := 0.0
    var best_distance := INF

    var candidate_indices: Array[int] = []
    if hint_progress >= 0.0:
        var center_index := int(
            floor(
                fposmod(
                    hint_progress,
                    float(track_size)
                )
            )
        )
        const SEARCH_RADIUS := 3
        for offset in range(-SEARCH_RADIUS, SEARCH_RADIUS + 1):
            candidate_indices.append(
                posmod(
                    center_index + offset,
                    track_size
                )
            )
    else:
        for i in range(track_size):
            candidate_indices.append(i)

    for i in candidate_indices:
        var p0 := track_world_position(
            float(i),
            track_x,
            track_size,
            segment_height
        )
        var p1 := track_world_position(
            float(i + 1),
            track_x,
            track_size,
            segment_height
        )
        var segment := p1 - p0
        var length_sq := segment.length_squared()
        var t := 0.0
        if length_sq > 0.0001:
            t = clampf(
                (world_position - p0).dot(segment) / length_sq,
                0.0,
                1.0
            )

        var closest := p0 + segment * t
        var distance_sq := (
            world_position - closest
        ).length_squared()

        if distance_sq < best_distance:
            best_distance = distance_sq
            best_progress = float(i) + t

    # A collision can throw the car well away from the road. In that case,
    # recover by doing the full scan once rather than locking onto a wrong
    # local segment forever.
    if hint_progress >= 0.0 and best_distance > 256.0:
        return nearest_track_progress(
            world_position,
            track_x,
            track_size,
            segment_height,
            -1.0
        )

    return best_progress

static func track_heading(
        track_position: float,
        track_x: PackedFloat32Array,
        segment_height: float
) -> float:
    var tangent := track_world_tangent(
        track_position,
        track_x,
        track_x.size(),
        segment_height
    )
    return atan2(
        tangent.x,
        tangent.z
    )

static func track_center_x(
        track_position: float,
        track_x: PackedFloat32Array
) -> float:
    var n := track_x.size()
    if n == 0:
        return 0.0
    if n == 1:
        return track_x[0]

    track_position=clampf(track_position,0.0,float(n-1))
    var base := int(floor(track_position))
    var t: float = track_position - floor(track_position)

    var p1: float = track_x[clampi(base, 0, n-1)]
    var p2: float = track_x[clampi(base + 1, 0, n-1)]

    if n < 4:
        return lerpf(p1, p2, t)

    var p0: float = track_x[clampi(base - 1, 0, n-1)]
    var p3: float = track_x[clampi(base + 2, 0, n-1)]

    var t2: float = t * t
    var t3: float = t2 * t

    return 0.5 * (
        2.0 * p1
        + (-p0 + p2) * t
        + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
        + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
    )

static func track_center_slope(
        track_position: float,
        track_x: PackedFloat32Array
) -> float:
    var n := track_x.size()
    if n < 2 or track_position<=0.0 or track_position>=n-1:
        return 0.0

    track_position=clampf(track_position,0.0,float(n-1))
    var base := int(floor(track_position))
    var t: float = track_position - floor(track_position)

    var p1: float = track_x[clampi(base, 0, n-1)]
    var p2: float = track_x[clampi(base + 1, 0, n-1)]

    if n < 4:
        return p2 - p1

    var p0: float = track_x[clampi(base - 1, 0, n-1)]
    var p3: float = track_x[clampi(base + 2, 0, n-1)]
    var t2: float = t * t

    return 0.5 * (
        (-p0 + p2)
        + 2.0 * (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t
        + 3.0 * (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t2
    )

static func curve_at(
        track_position: float,
        pattern: Array
) -> float:
    var n := pattern.size()
    if n == 0:
        return 0.0

    var base := int(floor(track_position))
    var t: float = track_position - floor(track_position)

    const RADIUS := 2
    var c0 := 0.0
    var c1 := 0.0
    var w0 := 0.0
    var w1 := 0.0

    for k in range(-RADIUS, RADIUS + 1):
        var weight: float = float(RADIUS + 1 - abs(k))
        c0 += curve_of(int(pattern[posmod(base + k, n)])) * weight
        c1 += curve_of(int(pattern[posmod(base + 1 + k, n)])) * weight
        w0 += weight
        w1 += weight

    c0 /= maxf(w0, 0.001)
    c1 /= maxf(w1, 0.001)

    var eased_t: float = t * t * (3.0 - 2.0 * t)
    return lerpf(c0, c1, eased_t)

static func step_speed(
        speed: float,
        throttle: float,
        brake: float,
        dt: float,
        max_speed: float,
        accel: float,
        brake_force: float,
        drag: float
) -> float:
    if dt <= 0.0:
        return clampf(speed, 0.0, max_speed)
    # Brake wins when both pedals are held and can bring the car to a stop.
    if brake > 0.01:
        return maxf(speed - brake_force * clampf(brake, 0.0, 1.0) * dt, 0.0)
    var target := max_speed * clampf(throttle, 0.0, 1.0)
    var response := accel if speed < target else accel * 0.5
    speed = move_toward(speed, target, response * dt)
    speed *= exp(-drag * dt * 0.001)
    return maxf(speed, 0.0)

static func steering_delta(
        steer_in: float,
        speed: float,
        dt: float,
        rate: float,
        max_speed: float
) -> float:
    var safe_max_speed := maxf(max_speed, 0.001)
    var factor := clampf(
        1.0 - (speed / safe_max_speed) * 0.55,
        0.45,
        1.0
    )
    return clampf(
        steer_in,
        -1.0,
        1.0
    ) * rate * factor * dt

static func road_offset(
        lateral_x: float,
        center_x: float,
        half_width: float
) -> float:
    if half_width <= 0.0:
        return 0.0
    return (lateral_x - center_x) / half_width

static func ai_brake_for(seg: int, distance_ahead: int) -> float:
    var c := absf(curve_of(seg))
    if c < 0.5:
        return 0.0

    var urgency := clampf(
        1.0 - float(distance_ahead) / 12.0,
        0.0,
        1.0
    )

    return clampf(
        c / 14.0 * urgency,
        0.0,
        1.0
    )

static func format_time(seconds: float) -> String:
    if seconds < 0.0:
        return "--:--.--"

    var m := int(seconds) / 60
    var s := int(seconds) % 60
    var cs := int(fmod(seconds, 1.0) * 100.0)

    return "%d:%02d.%02d" % [m, s, cs]

# Geometric turn speed plus the braking distance to each upcoming sample.
# This assists AI and validation drivers only; player inputs stay manual.
static func safe_corner_speed(at: float, track_x: PackedFloat32Array, max_speed: float, authority: float, grip: float) -> float:
    var safe := max_speed
    for metres in range(0,181,15):
        var p := at+metres/40.0
        var angle := atan2(track_center_slope(p,track_x),40.0)
        var before := atan2(track_center_slope(p-.03,track_x),40.0)
        var after := atan2(track_center_slope(p+.03,track_x),40.0)
        var curvature := absf(after-before)/2.4
        var local_limit := minf(max_speed,sqrt(grip/maxf(curvature,.00001)))
        local_limit=minf(local_limit,max_speed*clampf((1.0-absf(angle)/authority)/.52,.20,1.0))
        safe=minf(safe,sqrt(local_limit*local_limit+2.0*7.2*metres))
    return safe
