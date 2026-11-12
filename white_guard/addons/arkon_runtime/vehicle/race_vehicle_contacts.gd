extends RefCounted
# Swept SAT for the actual two car bodies. Sound/impact cooldowns never disable
# this constraint. All car pairs use it, including stopped and slow vehicles.
const HALF := Vector2(.90,2.20)
const SKIN := .025

static func _axes(car) -> Array[Vector2]:
    var yaw: float=car.heading_yaw
    return [Vector2(cos(yaw),-sin(yaw)),Vector2(sin(yaw),cos(yaw))]

static func _radius(axis: Vector2,axes: Array[Vector2]) -> float:
    return absf(axis.dot(axes[0]))*HALF.x+absf(axis.dot(axes[1]))*HALF.y

static func contact(a,b,old_a: Vector2,old_b: Vector2,swept: bool=true) -> Vector2:
    var aa := _axes(a);var ba := _axes(b)
    var axes: Array[Vector2]=[aa[0],aa[1],ba[0],ba[1]]
    var end := Vector2(a.world_x-b.world_x,a.world_z-b.world_z)
    var start := old_a-old_b if swept else end
    var movement := end-start
    var entry := 0.0;var leave := 1.0
    var entry_axis := Vector2.ZERO
    var penetration := INF;var overlap_axis := Vector2.ZERO
    var overlapping := true
    for axis in axes:
        var radius := _radius(axis,aa)+_radius(axis,ba)+SKIN
        var initial := start.dot(axis);var final := end.dot(axis)
        var depth := radius-absf(final)
        if depth<0: overlapping=false
        if depth<penetration:
            penetration=depth;overlap_axis=axis*(1.0 if final>=0 else -1.0)
        var velocity := movement.dot(axis)
        if absf(velocity)<.000001:
            if absf(initial)>radius: return Vector2.ZERO
        else:
            var low := (-radius-initial)/velocity;var high := (radius-initial)/velocity
            if low>high: var temp:=low;low=high;high=temp
            if low>entry:
                entry=low;entry_axis=axis*(-signf(velocity))
            leave=minf(leave,high)
            if entry>leave: return Vector2.ZERO
    if entry>0 and entry<=1 and entry_axis!=Vector2.ZERO:
        var radius := _radius(entry_axis,aa)+_radius(entry_axis,ba)+SKIN
        return entry_axis*maxf(0,radius-end.dot(entry_axis))
    return overlap_axis*maxf(0,penetration) if overlapping else Vector2.ZERO


static func velocity(car) -> Vector2:
    if car.has_method("contact_velocity"):return car.contact_velocity()
    return Vector2(car.lateral_velocity+car.impact_velocity,sqrt(maxf(0,car.speed*car.speed-car.lateral_velocity*car.lateral_velocity)))

static func inverse_mass(car) -> float:
    return float(car.contact_inverse_mass()) if car.has_method("contact_inverse_mass") else 1.0

static func _apply_velocity(car,value: Vector2) -> void:
    if car.has_method("apply_contact_velocity"):
        car.apply_contact_velocity(value)
    else:
        # The player's one-direction drivetrain cannot turn a frontal impact
        # into a positive-speed reverse teleport. Lateral momentum is retained.
        car.lateral_velocity=value.x
        car.speed=Vector2(value.x,maxf(0,value.y)).length()

static func solve(cars: Array,previous: Array[Vector2],pattern: Array,track: PackedFloat32Array) -> Array[Vector2i]:
    var touched: Array[Vector2i]=[]
    # First pass catches crossings; later passes settle car/car/wall constraints.
    for iteration in range(4):
        for i in range(cars.size()):
            for j in range(i+1,cars.size()):
                var a=cars[i];var b=cars[j]
                var start:=previous[i]-previous[j]
                var end:=Vector2(a.world_x-b.world_x,a.world_z-b.world_z)
                # Broad phase covers both ends of a swept crossing. Most
                # civilian cars and roadblocks are hundreds of metres apart.
                if minf(start.x,end.x)>5.0 or maxf(start.x,end.x)<-5.0 or minf(start.y,end.y)>5.0 or maxf(start.y,end.y)<-5.0:continue
                var amass:=inverse_mass(a);var bmass:=inverse_mass(b)
                var total:=amass+bmass
                if total<=0:continue
                var correction := contact(a,b,previous[i],previous[j],iteration==0)
                if correction.length_squared()<.00000001: continue
                var pair := Vector2i(i,j)
                if not pair in touched: touched.append(pair)
                var normal := correction.normalized()
                var a_before := Vector2(a.world_x,a.world_z)
                var b_before := Vector2(b.world_x,b.world_z)
                a.resolve_contact(correction*(amass/total),pattern,track)
                b.resolve_contact(-correction*(bmass/total),pattern,track)
                var moved := (Vector2(a.world_x,a.world_z)-a_before)-(Vector2(b.world_x,b.world_z)-b_before)
                var remaining := maxf(0,correction.length()-moved.dot(normal))
                # If a rail stopped one body, give the remaining separation to
                # the other, instead of pushing a vehicle through that rail.
                if remaining>.0001:
                    var am: float=(Vector2(a.world_x,a.world_z)-a_before).dot(normal)
                    if am<correction.length()*.45: b.resolve_contact(-normal*remaining,pattern,track)
                    else: a.resolve_contact(normal*remaining,pattern,track)
                var av:=velocity(a)
                var bv:=velocity(b)
                var closing := (av-bv).dot(normal)
                if closing<0:
                    var impulse := -closing/total
                    _apply_velocity(a,av+normal*impulse*amass)
                    _apply_velocity(b,bv-normal*impulse*bmass)
    return touched
