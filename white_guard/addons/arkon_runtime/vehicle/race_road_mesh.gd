extends RefCounted
# Two coherent ground surfaces consume the existing shared main camera.
# UV floats carry homogeneous road coordinates without colour quantization.
const ROWS := 128
const GROUND_RANGE := 2400.0
var row_count := ROWS
var last_sample_count := 0

func apply_graphics_profile(profile: Dictionary) -> void:
    # Reduce tessellation work, never the authored world or rear-view span.
    row_count = clampi(int(profile.get("road_rows", ROWS)), 32, ROWS)
    row_count -= row_count % 2
const Data=preload("res://addons/arkon_runtime/vehicle/race_level_data.gd")
const RaceProjection=preload("res://addons/arkon_runtime/vehicle/race_projection.gd")
var frame=preload("res://addons/arkon_runtime/vehicle/race_projection_frame.gd").new()
var extend_under_hud := true
var road := ArrayMesh.new()
var verge := ArrayMesh.new()
var far_road := ArrayMesh.new()
var far_verge := ArrayMesh.new()
var far_road_node: MeshInstance2D
var far_verge_node: MeshInstance2D
var _far_road_indices := PackedInt32Array()
var _far_verge_indices := PackedInt32Array()
var road_node: MeshInstance2D
var verge_node: MeshInstance2D
var back: Polygon2D
var road_material: ShaderMaterial
var verge_material: ShaderMaterial
var _road_vertices := PackedVector3Array()
var _road_uv := PackedVector2Array()
var _verge_vertices := PackedVector3Array()
var _verge_uv := PackedVector2Array()
var _road_indices := PackedInt32Array()
var _verge_indices := PackedInt32Array()
var _road_arrays: Array=[]
var _verge_arrays: Array=[]
func setup(host: Node2D, asphalt: Texture2D, grass: Texture2D) -> void:
    back=Polygon2D.new();back.color=Color("152534");back.z_index=-5;host.add_child(back)
    road_material=ShaderMaterial.new();road_material.shader=preload("res://addons/arkon_runtime/shaders/race_ground.gdshader")
    verge_material=road_material.duplicate();verge_material.set_shader_parameter("meadow",true)
    far_verge_node=_node(host,"DistantVerge",far_verge,grass,verge_material,-4)
    far_road_node=_node(host,"DistantRoad",far_road,asphalt,road_material,-4)
    verge_node=_node(host,"ProjectedVerge",verge,grass,verge_material,-2)
    road_node=_node(host,"ProjectedRoad",road,asphalt,road_material,-2)
    _road_vertices.resize(ROWS*2);_road_uv.resize(ROWS*2)
    _verge_vertices.resize(ROWS*4);_verge_uv.resize(ROWS*4)
    for i in range(ROWS-2,-1,-1):
        _quad_indices(_road_indices,i*2,i*2+1,(i+1)*2+1,(i+1)*2)
        _quad_indices(_verge_indices,i*4,i*4+1,(i+1)*4+1,(i+1)*4)
        _quad_indices(_verge_indices,i*4+2,i*4+3,(i+1)*4+3,(i+1)*4+2)
    _road_arrays.resize(Mesh.ARRAY_MAX);_verge_arrays.resize(Mesh.ARRAY_MAX)
    _road_arrays[Mesh.ARRAY_INDEX]=_road_indices;_verge_arrays[Mesh.ARRAY_INDEX]=_verge_indices
func _node(host: Node2D, name_: String, mesh_: ArrayMesh, tex: Texture2D, mat: ShaderMaterial, depth: int) -> MeshInstance2D:
    var node := MeshInstance2D.new()
    node.name=name_;node.mesh=mesh_;node.texture=tex;node.material=mat;node.z_index=depth
    node.texture_filter=CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS_ANISOTROPIC
    host.add_child(node);return node
func _quad_indices(target: PackedInt32Array, a: int,b: int,c: int,d: int) -> void:
    target.append_array(PackedInt32Array([a,b,c,a,c,d]))
func update(host: Node2D, size_: Vector2) -> void:
    # Reuse the existing near road/verge strips under the glass dashboard.
    # Their projection is unchanged; only this ground instance's bottom clip
    # reaches the window edge. Scenery and the 3D vehicle keep their old frame.
    frame.configure(host,size_,size_.y if extend_under_hud else -1.0)
    var play_h: float=size_.y*host.PLAYFIELD_FRACTION
    var horizon: float=host._horizon(play_h)
    var at: float=host.player_car.track_phase()
    var camera_x: float=host._camera_world_x(at)
    var behind: float=host._behind()
    var zoom: float=host.camera_state.zoom if host.camera_state!=null else 1.0
    var world_horizon: float=horizon+host.CAMERA_DEPTH*zoom*size_.x*host.ROAD_SCREEN_SCALE*(float(host.camera_state.road_grade) if host.camera_state!=null else 0.0)
    back.polygon=PackedVector2Array([Vector2(0,world_horizon),Vector2(size_.x,world_horizon),size_,Vector2(0,size_.y)])
    for material in [road_material,verge_material]:
        material.set_shader_parameter("logical_size",size_)
        material.set_shader_parameter("horizon",horizon)
        material.set_shader_parameter("vertical_scale",(play_h-horizon)*6.0*zoom)
        material.set_shader_parameter("focal",.84*zoom*size_.x*.75)
        material.set_shader_parameter("camera_yaw",float(host.camera_state.yaw_offset) if host.camera_state!=null else 0.0)
        material.set_shader_parameter("camera_roll",float(host.camera_state.roll) if host.camera_state!=null else 0.0)
        material.set_shader_parameter("origin",Vector2(camera_x,at*Data.SEGMENT_HEIGHT-behind))
        material.set_shader_parameter("car_position",Vector2(float(host.player_car.world_x),at*Data.SEGMENT_HEIGHT))
        material.set_shader_parameter("car_yaw",float(host.player_car.heading_yaw))
        material.set_shader_parameter("field_start",Data.SECTOR_STARTS[1])
        material.set_shader_parameter("bridge_start",Data.BRIDGE_START)
        material.set_shader_parameter("bridge_end",Data.BRIDGE_END)
        material.set_shader_parameter("tunnel_start",Data.TUNNEL_START)
        material.set_shader_parameter("tunnel_end",Data.TUNNEL_END)
    var rivals := PackedVector4Array()
    for ai in host.ai_cars:
        var z: float=ai.car.world_z
        rivals.append(Vector4(float(ai.car.world_x),z,float(ai.car.heading_yaw),1))
    for material in [road_material,verge_material]: material.set_shader_parameter("rival_lights",rivals)
    _verge_vertices.clear();_verge_uv.clear();_verge_indices.clear()
    _road_vertices.clear();_road_uv.clear();_road_indices.clear()
    _far_road_indices.clear();_far_verge_indices.clear()
    # The world exists on both sides of the car. A heading change must reveal
    # already travelled asphalt, not the start of a forward-only mesh ribbon.
    # Keep the old logarithmic detail in front and mirror that bounded budget
    # behind; frustum clipping discards whichever half is actually out of view.
    var origin: float=host.player_car.world_z
    var samples := PackedFloat32Array()
    var yaw: float=host._camera_yaw()
    var horizontal: float= (size_.x*.5+absf(sin(float(host.camera_state.roll)))*play_h)/frame.focal
    var facing := cos(yaw)
    var spread: float= absf(sin(yaw))*horizontal
    var front_only: bool= facing-spread>.02
    var rear_only: bool= facing+spread<-.02
    # Spend rows only on longitudinal directions intersecting the lens. A
    # normal chase view keeps the profile's front rows plus one near closing
    # row; side views share that budget, and reverse gets the full budget.
    var back_rows := 1 if front_only else (row_count if rear_only else row_count/2)
    var front_rows := 1 if rear_only else (row_count if front_only else row_count/2)
    for i in range(back_rows-1,-1,-1):
        samples.append(-4.8*pow(GROUND_RANGE/4.8,float(i)/float(maxi(back_rows-1,1)))-behind)
    for i in range(front_rows):
        samples.append(4.8*pow(GROUND_RANGE/4.8,float(i)/float(maxi(front_rows-1,1)))-behind)
    samples.append(-behind)
    samples.sort()
    var lower := samples[0]
    var upper := samples[-1]
    for portal: float in [Data.TUNNEL_START,Data.TUNNEL_END]:
        var relative := portal-origin
        if relative>lower and relative<upper: samples.append(relative)
    samples.sort()
    last_sample_count = samples.size()
    var rows: Array[Dictionary]=[]
    var edge := Data.ROAD_WIDTH*.5+.32
    for sample in samples:
        var world_z: float=origin+sample
        var centre: float=host._smooth_track_x(world_z/Data.SEGMENT_HEIGHT)
        var height := Data.road_height(world_z)
        var terrain := 0.0 if world_z>=Data.BRIDGE_END else height
        # Near transverse ground needs a wide physical apron too: using only
        # signed Z made the outside verge invert/disappear during hard steering.
        var outer := absf(sample+behind)*3.0+80.0
        var profile:=Data.verge_profile_cached(world_z,outer,height,terrain)
        var road_sides: Array[Vector3]=[]
        var verge_sides: Array[Vector3]=[]
        for side in range(2):
            var direction := -1.0 if side==0 else 1.0
            road_sides.append(frame.transform*Vector3(centre+edge*direction,height,world_z))
            for boundary: Vector2 in profile:
                verge_sides.append(frame.transform*Vector3(centre+boundary.x*direction,boundary.y,world_z))
        # On level shoulders all four profile points share one plane. Keep
        # bridge aprons and every embankment/cutting as separate faces.
        var flat := height==terrain and not (world_z>=Data.BRIDGE_START and world_z<Data.BRIDGE_END)
        rows.append({"z":world_z,"road":road_sides,"verge":verge_sides,"flat":flat})
    var strips: Array[Dictionary]=[]
    for i in range(1,rows.size()):
        var a: Dictionary=rows[i-1];var b: Dictionary=rows[i]
        var middle := (float(a.z)+float(b.z))*.5
        var distance: float=(frame.transform*Vector3(host._smooth_track_x(middle/Data.SEGMENT_HEIGHT),Data.road_height(middle),middle)).z
        strips.append({"a":a,"b":b,"depth":distance,"distant":_beyond_portal(origin,middle)})
    # Canvas has no depth buffer: submit visible ribbons far-to-near in the
    # actual camera basis, including a reverse view of a bridge or tunnel.
    strips.sort_custom(func(a,b): return float(a.depth)>float(b.depth))
    for strip in strips:
        var a: Dictionary=strip.a;var b: Dictionary=strip.b
        _append_road(host,[a.road[0],a.road[1],b.road[1],b.road[0]],bool(strip.distant))
        # These three transverse grass bands have identical material output
        # outdoors and coplanar reciprocal-depth interpolation. One face keeps
        # the same silhouette/texture while avoiding two redundant clips and
        # triangulations per shoulder. Retain tunnel surface-kind boundaries.
        var flat: bool=bool(a.flat) and bool(b.flat) and (float(b.z)<Data.TUNNEL_START or float(a.z)>=Data.TUNNEL_END)
        for side in range(2):
            if flat:
                var offset := side*4
                _append_verge([a.verge[offset],a.verge[offset+3],b.verge[offset+3],b.verge[offset]],0,bool(strip.distant))
                continue
            for span in range(3):
                var offset := side*4+span
                var face: Array[Vector3]=[a.verge[offset],a.verge[offset+1],b.verge[offset+1],b.verge[offset]]
                var vertical := absf(face[0].y-face[1].y)>.05 and Vector2(face[0].x,face[0].z).distance_squared_to(Vector2(face[1].x,face[1].z))<.0001
                _append_verge(face,3 if vertical else (2 if span==2 else 0),bool(strip.distant))
    _road_arrays[Mesh.ARRAY_VERTEX]=_road_vertices;_road_arrays[Mesh.ARRAY_TEX_UV]=_road_uv
    _road_arrays[Mesh.ARRAY_INDEX]=_road_indices
    _verge_arrays[Mesh.ARRAY_VERTEX]=_verge_vertices;_verge_arrays[Mesh.ARRAY_TEX_UV]=_verge_uv
    _verge_arrays[Mesh.ARRAY_INDEX]=_verge_indices
    _submit(road,road_node,_road_arrays)
    _submit(verge,verge_node,_verge_arrays)
    _road_arrays[Mesh.ARRAY_INDEX]=_far_road_indices
    _verge_arrays[Mesh.ARRAY_INDEX]=_far_verge_indices
    _submit(far_road,far_road_node,_road_arrays)
    _submit(far_verge,far_verge_node,_verge_arrays)

func _beyond_portal(origin: float,z: float) -> bool:
    if origin<Data.TUNNEL_START: return z>=Data.TUNNEL_START
    if origin>=Data.TUNNEL_END: return z<Data.TUNNEL_END
    return z<Data.TUNNEL_START or z>=Data.TUNNEL_END

func _append_road(host,source: Array[Vector3],distant: bool) -> void:
    var clipped: Array[Vector3]=frame.clip(source,true)
    if clipped.size()<3: return
    var first := _road_vertices.size()
    var whole:=is_same(clipped,source)
    for corner in range(clipped.size()):
        var point:=clipped[corner]
        var lateral: float
        if whole:
            # Original row corners are authored at exactly +/-VERGE_EDGE.
            # Only a NEW frustum intersection needs an inverse world lookup.
            lateral=-Data.VERGE_EDGE if corner in [0,3] else Data.VERGE_EDGE
        else:
            var world: Vector3=frame.inverse*point
            lateral=world.x-host._smooth_track_x(world.z/Data.SEGMENT_HEIGHT)
        var screen: Vector2=frame.project(point)
        _road_vertices.append(Vector3(screen.x,screen.y,0))
        _road_uv.append(Vector2(lateral/point.z,1.0/point.z))
    for corner in range(1,clipped.size()-1):
        if distant: _far_road_indices.append_array(PackedInt32Array([first,first+corner,first+corner+1]))
        else: _road_indices.append_array(PackedInt32Array([first,first+corner,first+corner+1]))

func _append_verge(source: Array[Vector3],kind: int,distant: bool) -> void:
    var clipped: Array[Vector3]=frame.clip(source,true)
    if clipped.size()<3: return
    var first := _verge_vertices.size()
    for point in clipped:
        var screen: Vector2=frame.project(point)
        _verge_vertices.append(Vector3(screen.x,screen.y,0))
        _verge_uv.append(Vector2(kind,1.0/point.z))
    for corner in range(1,clipped.size()-1):
        if distant: _far_verge_indices.append_array(PackedInt32Array([first,first+corner,first+corner+1]))
        else: _verge_indices.append_array(PackedInt32Array([first,first+corner,first+corner+1]))

func _submit(mesh_: ArrayMesh,node: MeshInstance2D,data: Array) -> void:
    mesh_.clear_surfaces()
    node.visible=not data[Mesh.ARRAY_INDEX].is_empty()
    if node.visible: mesh_.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,data)
