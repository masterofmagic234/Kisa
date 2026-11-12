extends RefCounted
# Clip in camera space BEFORE the perspective divide. Every roadside connector
# keeps its authored world endpoints; a passing camera never drags a beam along.
const NEAR := 0.8

static func clip_camera(host, source: Array[Vector3], vp: Vector2) -> Array[Vector3]:
    var points := source
    for plane in range(5):
        if points.is_empty(): break
        var output: Array[Vector3] = []
        for i in range(points.size()):
            var a := points[i]
            var b := points[(i+1)%points.size()]
            var da := _plane_distance(host,a,vp,plane)
            var db := _plane_distance(host,b,vp,plane)
            if da>=0.0: output.append(a)
            if (da>=0.0)!=(db>=0.0):
                output.append(a.lerp(b,da/(da-db)))
        points=output
    return points

static func _plane_distance(host, p: Vector3, vp: Vector2, plane: int) -> float:
    if plane==0: return p.z-NEAR
    var h: float=vp.y*host.PLAYFIELD_FRACTION
    var horizon: float=host._horizon(h)
    var zoom: float=host.camera_state.zoom if host.camera_state!=null else 1.0
    var focal: float=host.CAMERA_DEPTH*zoom*vp.x*host.ROAD_SCREEN_SCALE
    var numerator := Vector2(focal*p.x,(h-horizon)*host.CAMERA_BEHIND*zoom-focal*p.y)
    numerator=numerator.rotated(-float(host.camera_state.roll) if host.camera_state!=null else 0.0)
    if plane==1: return numerator.x+vp.x*.5*p.z
    if plane==2: return vp.x*.5*p.z-numerator.x
    if plane==3: return numerator.y+horizon*p.z
    return (h-horizon)*p.z-numerator.y

static func surface(host, source: Array[Vector3], vp: Vector2) -> PackedVector2Array:
    var camera: Array[Vector3] = []
    for world in source: camera.append(host._camera_point(world))
    var screen := PackedVector2Array()
    for point in clip_camera(host,camera,vp):
        var p: Vector2=host._project_camera_point(point,vp)
        if screen.is_empty() or screen[-1].distance_squared_to(p)>.0001: screen.append(p)
    if screen.size()>1 and screen[0].distance_squared_to(screen[-1])<.0001: screen.remove_at(screen.size()-1)
    return screen

static func ribbon(host, a: Vector3, b: Vector3, thickness: float, vp: Vector2) -> PackedVector2Array:
    var p: Vector3=host._camera_point(a)
    var q: Vector3=host._camera_point(b)
    if p.z<NEAR and q.z<NEAR: return PackedVector2Array()
    if p.z<NEAR: p=p.lerp(q,(NEAR-p.z)/(q.z-p.z))
    elif q.z<NEAR: q=q.lerp(p,(NEAR-q.z)/(p.z-q.z))
    var sp: Vector2=host._project_camera_point(p,vp)
    var sq: Vector2=host._project_camera_point(q,vp)
    if sp.distance_squared_to(sq)<.0001: return PackedVector2Array()
    var side := (sq-sp).normalized().orthogonal()
    # Each end has its own depth. A near thick rail must taper into the distance.
    var wp: float=maxf(.35,host._camera_projection_scale(p.z)*vp.x*host.ROAD_SCREEN_SCALE*thickness*.5)
    var wq: float=maxf(.35,host._camera_projection_scale(q.z)*vp.x*host.ROAD_SCREEN_SCALE*thickness*.5)
    var polygon := PackedVector2Array([sp+side*wp,sq+side*wq,sq-side*wq,sp-side*wp])
    return clip_screen(polygon,Rect2(Vector2.ZERO,Vector2(vp.x,vp.y*host.PLAYFIELD_FRACTION)))

static func clip_screen(source: PackedVector2Array, rect: Rect2) -> PackedVector2Array:
    var points := source
    for plane in range(4):
        var output := PackedVector2Array()
        for i in range(points.size()):
            var a := points[i]
            var b := points[(i+1)%points.size()]
            var da := _screen_distance(a,rect,plane)
            var db := _screen_distance(b,rect,plane)
            if da>=0.0: output.append(a)
            if (da>=0.0)!=(db>=0.0): output.append(a.lerp(b,da/(da-db)))
        points=output
    return points

static func _screen_distance(p: Vector2, rect: Rect2, plane: int) -> float:
    if plane==0: return p.x-rect.position.x
    if plane==1: return rect.end.x-p.x
    if plane==2: return p.y-rect.position.y
    return rect.end.y-p.y

static func clip_textured_screen(source: PackedVector2Array, uv: PackedVector2Array, rect: Rect2) -> Dictionary:
    var points := source
    var coords := uv
    for plane in range(4):
        var output := PackedVector2Array()
        var output_uv := PackedVector2Array()
        for i in range(points.size()):
            var next := (i+1)%points.size()
            var a := points[i];var b := points[next]
            var da := _screen_distance(a,rect,plane);var db := _screen_distance(b,rect,plane)
            if da>=0: output.append(a);output_uv.append(coords[i])
            if (da>=0)!=(db>=0):
                var t := da/(da-db)
                output.append(a.lerp(b,t));output_uv.append(coords[i].lerp(coords[next],t))
        points=output;coords=output_uv
    return {"points":points,"uv":coords}
