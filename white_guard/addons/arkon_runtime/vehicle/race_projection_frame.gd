extends RefCounted
# One camera transform per frame, rather than recomputing its basis, lens,
# terrain lookup and five frustum planes for every scenery triangle.
const NEAR := .8
var transform := Transform3D.IDENTITY
var inverse := Transform3D.IDENTITY
var lens := Transform2D.IDENTITY
var focal := 1.0
var vertical := 1.0
var pivot := Vector2.ZERO
var viewport := Rect2()
var planes: Array[Vector4] = []
var world_planes: Array[Vector4] = []

func configure(host, size_: Vector2, clip_bottom: float=-1.0) -> void:
    var camera=host.camera_state
    var yaw: float=camera.yaw_offset
    var c := cos(yaw);var s := sin(yaw)
    var origin: float=host.player_car.world_z
    var x: float=host._camera_world_x(host.player_car.track_phase())
    var behind: float=host._behind()
    var grade: float=camera.road_grade
    var height: float=camera.road_height
    transform=Transform3D(Basis(Vector3(c,0,s),Vector3(0,1,0),Vector3(-s,-grade,c)),Vector3(-x*c+(origin-behind)*s,-height+origin*grade,-x*s+(-origin+behind)*c))
    inverse=transform.affine_inverse()
    lens=Transform2D(-float(camera.roll),Vector2.ZERO)
    focal=host.CAMERA_DEPTH*float(camera.zoom)*size_.x*host.ROAD_SCREEN_SCALE
    var h: float=size_.y*host.PLAYFIELD_FRACTION
    var horizon: float=host._horizon(h)
    vertical=(h-horizon)*host.CAMERA_BEHIND*float(camera.zoom)
    pivot=Vector2(size_.x*.5,horizon)
    # The established playfield still defines the lens and car framing. A
    # ground surface may extend its lower scissor beneath translucent controls
    # without moving the horizon or changing any projected world point.
    var clip_h := h if clip_bottom<0.0 else clampf(clip_bottom,h,size_.y)
    viewport=Rect2(Vector2.ZERO,Vector2(size_.x,clip_h))
    var cr := cos(float(camera.roll));var sr := sin(float(camera.roll))
    planes=[Vector4(0,0,1,-NEAR),Vector4(focal*cr,-focal*sr,pivot.x,vertical*sr),Vector4(-focal*cr,focal*sr,pivot.x,-vertical*sr),Vector4(-focal*sr,-focal*cr,horizon,vertical*cr),Vector4(focal*sr,focal*cr,clip_h-horizon,-vertical*cr)]

    world_planes.clear()
    for plane in planes:
        var normal := Vector3(plane.x,plane.y,plane.z)
        var world_normal := transform.basis.transposed()*normal
        world_planes.append(Vector4(world_normal.x,world_normal.y,world_normal.z,plane.w+normal.dot(transform.origin)))

func bounds_visible(box: AABB) -> bool:
    var middle := box.get_center()
    var extent := box.size*.5
    for plane in world_planes:
        var normal := Vector3(plane.x,plane.y,plane.z)
        if normal.dot(middle)+plane.w+normal.abs().dot(extent)<0: return false
    return true

func project(p: Vector3) -> Vector2:
    return pivot+(lens*Vector2(focal*p.x,vertical-focal*p.y))/p.z

func clip(points: Array[Vector3], all_planes: bool=false) -> Array[Vector3]:
    var result := points
    if points.is_empty(): return result
    var minimum := points[0];var maximum := points[0]
    for point in points: minimum=minimum.min(point);maximum=maximum.max(point)
    var middle := (minimum+maximum)*.5;var extent := (maximum-minimum)*.5
    for plane in planes if all_planes else [planes[0]]:
        # Most foliage/facade polygons fit completely inside each plane.
        # Accept/reject their bounds before allocating another polygon array.
        var normal := Vector3(plane.x,plane.y,plane.z)
        var distance := normal.dot(middle)+plane.w
        var radius := normal.abs().dot(extent)
        if distance+radius<0: return []
        if distance-radius>=0: continue
        var output: Array[Vector3]=[]
        if result.is_empty(): break
        for i in range(result.size()):
            var a := result[i];var b := result[(i+1)%result.size()]
            var da := a.x*plane.x+a.y*plane.y+a.z*plane.z+plane.w
            var db := b.x*plane.x+b.y*plane.y+b.z*plane.z+plane.w
            if da>=0: output.append(a)
            if (da>=0)!=(db>=0): output.append(a.lerp(b,da/(da-db)))
        result=output
    return result
