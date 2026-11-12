extends RefCounted
# One indexed surface per texture, with reusable meshes and submission arrays.
var solid_uv := Vector2.ZERO
var node: MeshInstance2D
var mesh := ArrayMesh.new()
var vertices := PackedVector3Array()
var colors := PackedColorArray()
var uv := PackedVector2Array()
var indices := PackedInt32Array()
var arrays: Array=[]
func setup(host: Node2D, name_: String, texture: Texture2D, order: int) -> void:
    node=MeshInstance2D.new();node.name=name_;node.mesh=mesh;node.texture=texture;node.z_index=order
    node.texture_filter=CanvasItem.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS
    host.add_child(node);arrays.resize(Mesh.ARRAY_MAX)
func begin() -> void:
    vertices.clear();colors.clear();uv.clear();indices.clear()
func polygon(points: PackedVector2Array, color: Color, coords: PackedVector2Array=PackedVector2Array(), reciprocal_depth: PackedFloat32Array=PackedFloat32Array()) -> void:
    if points.size()<3: return
    var first := vertices.size()
    for i in range(points.size()):
        # Canvas rendering consumes XY only. Z retains unquantized reciprocal
        # camera depth for the airframe's shared-mesh occlusion rasterizer.
        vertices.append(Vector3(points[i].x,points[i].y,reciprocal_depth[i] if reciprocal_depth.size()==points.size() else 0.0));colors.append(color)
        uv.append(coords[i] if coords.size()==points.size() else solid_uv)
    for i in range(1,points.size()-1):
        if absf((points[i]-points[0]).cross(points[i+1]-points[0]))>.01:
            indices.append_array(PackedInt32Array([first,first+i,first+i+1]))
func rectangle(rect: Rect2, color: Color, clip: Rect2=Rect2(),region: Rect2=Rect2(0,0,1,1)) -> void:
    var visible := rect.intersection(clip) if clip.size.x>0 else rect
    if visible.size.x<=0 or visible.size.y<=0: return
    var a := region.position+(visible.position-rect.position)/rect.size*region.size
    var b := region.position+(visible.end-rect.position)/rect.size*region.size
    polygon(PackedVector2Array([visible.position,Vector2(visible.end.x,visible.position.y),visible.end,Vector2(visible.position.x,visible.end.y)]),color,PackedVector2Array([a,Vector2(b.x,a.y),b,Vector2(a.x,b.y)]))
func commit() -> void:
    mesh.clear_surfaces()
    if indices.is_empty(): node.visible=false;return
    node.visible=true
    arrays[Mesh.ARRAY_VERTEX]=vertices;arrays[Mesh.ARRAY_COLOR]=colors;arrays[Mesh.ARRAY_TEX_UV]=uv;arrays[Mesh.ARRAY_INDEX]=indices
    mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
