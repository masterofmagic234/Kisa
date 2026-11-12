extends RefCounted
# The same world-space widths used by the scenic meshes. Swept movement stops
# at their faces; clamping a lateral offset to a MOVING road edge is not physics.
const Data=preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")
const Math=preload("res://addons/arkon_runtime/vehicle/race_math.gd")
const BODY_HALF_WIDTH := 0.85
const BODY_HALF_LENGTH := 2.1

static func limits(z: float, pattern: Array, track: PackedFloat32Array, clearance: float=BODY_HALF_WIDTH) -> Vector2:
    var left := -INF
    var right := INF
    if pattern.size()==Data.TRACK_DATA.total_segments():
        if z>=Data.SECTOR_STARTS[1]-BODY_HALF_LENGTH and z<Data.BRIDGE_START:
            left=-Data.FIELD_FENCE_X+clearance;right=Data.FIELD_FENCE_X-clearance
        if z>=Data.BRIDGE_START-BODY_HALF_LENGTH and z<Data.BRIDGE_END:
            left=-Data.BRIDGE_RAIL_X+clearance;right=Data.BRIDGE_RAIL_X-clearance
        if z>=Data.TUNNEL_START-BODY_HALF_LENGTH and z<Data.TUNNEL_END:
            left=-Data.TUNNEL_BODY_CLEAR_X+clearance;right=Data.TUNNEL_BODY_CLEAR_X-clearance
    if z>=0.0 and z<pattern.size()*Data.SEGMENT_HEIGHT:
        var curve := Math.curve_at(z/Data.SEGMENT_HEIGHT,pattern)
        if curve>.4: left=maxf(left,-Data.CURVE_RAIL_X+clearance)
        elif curve<-.4: right=minf(right,Data.CURVE_RAIL_X-clearance)
    var center := Math.track_center_x(z/Data.SEGMENT_HEIGHT,track)
    return Vector2(center+left,center+right)

static func sweep(start: Vector2, target: Vector2, pattern: Array, track: PackedFloat32Array, heading: float) -> Vector3:
    var clearance := BODY_HALF_WIDTH*absf(cos(heading))+BODY_HALF_LENGTH*absf(sin(heading))
    var bounds := limits(start.y,pattern,track,clearance)
    var x := clampf(target.x,bounds.x,bounds.y)
    var contact := 1.0 if absf(x-target.x)>.00001 else 0.0
    bounds=limits(target.y,pattern,track,clearance)
    var z := target.y
    var first_hit := 1.0
    var blocked := false
    var steps := maxi(1,ceili(absf(target.y-start.y)))
    for i in range(1,steps+1):
        var fraction := float(i)/steps
        var sample_z := lerpf(start.y,target.y,fraction)
        var sample := limits(sample_z,pattern,track,clearance)
        if x<sample.x or x>sample.y:
            first_hit=fraction;blocked=true;break
    if blocked:
        # The car's forward advance also meets a curved wall or portal. Keep
        # its legitimate lateral steering, and sweep Z to the first contact.
        var low := 0.0
        var high := first_hit
        for i in range(14):
            var middle := (low+high)*.5
            bounds=limits(lerpf(start.y,target.y,middle),pattern,track,clearance)
            if x>=bounds.x and x<=bounds.y: low=middle
            else: high=middle
        z=lerpf(start.y,target.y,low);contact=1.0
    return Vector3(x,z,contact)
