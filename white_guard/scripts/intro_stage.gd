extends Control
## Deterministic vector staging shared with the offline Canvas player.
## Audio time is the sole clock: seeking and pausing preserve every joint pose.
var shots: Array = []
var textures: Dictionary = {} # Legacy preview compatibility only.
var artwork: Dictionary = {}
var current_shot: Dictionary = {}
var current_time := 0.0
var _clips: Array[Control] = []
var _painters: Array[PanelPainter] = []
var _label: Label
var _font := preload("res://assets/reused/fonts/Manrope-Variable.ttf")

class PanelPainter extends Control:
    var director: Control
    var record: Dictionary = {}
    func _draw() -> void:
        if is_instance_valid(director): director._draw_panel(self, record)

func configure(records: Array, art_data: Dictionary = {}) -> void:
    clear()
    shots = records.duplicate()
    artwork = art_data.duplicate()
    if artwork.is_empty() and FileAccess.file_exists("res://data/intro_art.json"):
        artwork = JSON.parse_string(FileAccess.get_file_as_string("res://data/intro_art.json"))
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    clip_contents = true
    for i in range(3):
        var clip := Control.new()
        clip.clip_contents = true
        clip.mouse_filter = Control.MOUSE_FILTER_IGNORE
        add_child(clip)
        var painter := PanelPainter.new()
        painter.director = self
        painter.mouse_filter = Control.MOUSE_FILTER_IGNORE
        clip.add_child(painter)
        _clips.append(clip)
        _painters.append(painter)
    _label = Label.new()
    _label.mouse_filter = Control.MOUSE_FILTER_IGNORE
    _label.add_theme_font_override("font",_font)
    _label.add_theme_color_override("font_color",Color("d2c9ad"))
    _label.add_theme_color_override("font_shadow_color",Color(0.03,0.04,0.04,0.95))
    _label.add_theme_constant_override("shadow_offset_x",1)
    _label.add_theme_constant_override("shadow_offset_y",2)
    add_child(_label)
    for shot: Dictionary in shots:
        if shot.has("asset"): _load_asset(shot.asset)
        for layer: Dictionary in shot.get("layers", []): _load_asset(layer.asset)
    show_at(0)

func _load_asset(path: String) -> void:
    if not textures.has(path): textures[path] = load(path)

func show_at(seconds: float) -> void:
    current_time = seconds
    current_shot = shot_at(seconds)
    _update_panels()
    _update_label()
    queue_redraw()

func shot_at(seconds: float) -> Dictionary:
    for shot: Dictionary in shots:
        if seconds >= float(shot.start) and seconds < float(shot.end): return shot
    return shots.back() if not shots.is_empty() else {}

func _notification(what: int) -> void:
    if what == NOTIFICATION_RESIZED:
        _update_panels()
        _update_label()

func _update_label() -> void:
    if not is_instance_valid(_label) or current_shot.is_empty(): return
    var elapsed := current_time-float(current_shot.start)
    _label.text = String(current_shot.get("label",""))
    _label.visible = not _label.text.is_empty() and elapsed < 3
    _label.position = Vector2(64,54)*size/Vector2(1280,720)
    _label.add_theme_font_size_override("font_size",maxi(10,roundi(16*size.x/1280)))
    _label.modulate.a = clampf((3-elapsed)/0.35,0,1)

func panel_rects(shot: Dictionary, viewport: Vector2 = Vector2(1280,720)) -> Array[Rect2]:
    if shot.get("panels", []).is_empty(): return [Rect2(Vector2.ZERO,viewport)]
    var margin := Vector2(40,40)*viewport/Vector2(1280,720)
    var gutter := Vector2(22,22)*viewport/Vector2(1280,720)
    var area := viewport-margin*2
    var output: Array[Rect2] = []
    match String(shot.get("layout", "single")):
        "split":
            var height := (area.y-gutter.y)/2
            output = [Rect2(margin,Vector2(area.x,height)),Rect2(margin+Vector2(0,height+gutter.y),Vector2(area.x,height))]
        "triptych":
            var top := (area.y-gutter.y)*0.45
            var bottom := area.y-gutter.y-top
            var half := (area.x-gutter.x)/2
            output = [Rect2(margin,Vector2(area.x,top)),Rect2(margin+Vector2(0,top+gutter.y),Vector2(half,bottom)),Rect2(margin+Vector2(half+gutter.x,top+gutter.y),Vector2(half,bottom))]
        "strip":
            var width := (area.x-gutter.x*2)/3
            for i in range(3): output.append(Rect2(margin+Vector2(i*(width+gutter.x),0),Vector2(width,area.y)))
        _:
            output = [Rect2(margin,area)]
    return output

func _update_panels() -> void:
    if _clips.is_empty() or current_shot.is_empty(): return
    var vector := current_shot.has("scene") and not artwork.is_empty()
    var records: Array = current_shot.get("panels", [])
    if records.is_empty(): records = [{"scene":current_shot.get("scene", "")}]
    var rects := panel_rects(current_shot,size)
    var elapsed := current_time-float(current_shot.start)
    for i in range(_clips.size()):
        var active := vector and i < records.size() and i < rects.size()
        if active: active = elapsed >= float(records[i].get("reveal",0))
        _clips[i].visible = active
        if not active: continue
        _clips[i].position = rects[i].position
        _clips[i].size = rects[i].size
        _painters[i].position = Vector2.ZERO
        _painters[i].size = rects[i].size
        _painters[i].record = records[i]
        _painters[i].queue_redraw()

func _interpolate(a: Variant, b: Variant, progress: float) -> Variant:
    if a is Array and b is Array and a.size() == b.size():
        var output: Array = []
        for i in range(a.size()): output.append(_interpolate(a[i],b[i],progress))
        return output
    if (a is float or a is int) and (b is float or b is int): return lerpf(float(a),float(b),progress)
    return a if progress < 1 else b

func state_at(node: Dictionary, seconds: float) -> Dictionary:
    var base := {"x":0.0,"y":0.0,"rotation":0.0,"scale_x":1.0,"scale_y":1.0,"alpha":1.0,"points":node.get("points",[])}
    base.merge(node.get("transform",{}),true)
    if node.get("keys",[]).is_empty(): return base
    var output := base.duplicate()
    var time := fposmod(seconds,float(node.loop)) if float(node.get("loop",0)) > 0 else seconds
    for property: String in ["x","y","rotation","scale_x","scale_y","alpha","points"]:
        var previous: Variant = base[property]
        var at := 0.0
        for key: Dictionary in node.get("keys",[]):
            if not key.has(property): continue
            var next := float(key.time)
            if time < next:
                output[property] = _interpolate(previous,key[property],clampf((time-at)/maxf(0.000000001,next-at),0,1))
                break
            previous = key[property]
            at = next
            output[property] = previous
    return output

func _node_transform(state: Dictionary) -> Transform2D:
    return Transform2D(deg_to_rad(float(state.rotation)),Vector2(float(state.scale_x),float(state.scale_y)),0,Vector2(float(state.x),float(state.y)))

func _colour(value: String, alpha: float) -> Color:
    var colour := Color(value)
    colour.a *= alpha
    return colour

func _clean_points(points: PackedVector2Array) -> PackedVector2Array:
    var output := PackedVector2Array()
    for point: Vector2 in points:
        if output.is_empty() or output[-1].distance_squared_to(point) > 0.0000000001: output.append(point)
    if output.size() > 1 and output[0].distance_squared_to(output[-1]) <= 0.0000000001: output.remove_at(output.size()-1)
    return output

func _edges_cross(a: Vector2, b: Vector2, c: Vector2, d: Vector2) -> bool:
    return (b-a).cross(c-a)*(b-a).cross(d-a) < 0 and (d-c).cross(a-c)*(d-c).cross(b-c) < 0

func polygon_points(source: PackedVector2Array) -> PackedVector2Array:
    # Near-plane clipping pads morph tracks with repeated vertices. Repair only
    # these duplicates and crossed quads; preserve authored concave contours.
    var points := _clean_points(source)
    if points.size() < 3: return PackedVector2Array()
    if points.size() == 4 and (_edges_cross(points[0],points[1],points[2],points[3]) or _edges_cross(points[1],points[2],points[3],points[0])):
        points = _clean_points(Geometry2D.convex_hull(points))
    var area := 0.0
    var origin := points[0]
    for i in range(1,points.size()-1): area += (points[i]-origin).cross(points[i+1]-origin)
    return points if absf(area)*0.5 >= 0.05 else PackedVector2Array()

func _inside_panel(points: PackedVector2Array, matrix: Transform2D, viewport: Vector2) -> bool:
    if points.is_empty(): return false
    var bounds := Rect2(matrix*points[0],Vector2.ZERO)
    for point: Vector2 in points: bounds = bounds.expand(matrix*point)
    return bounds.grow(1).intersects(Rect2(Vector2.ZERO,viewport))

func _draw_node(painter: Control, node: Dictionary, seconds: float, parent: Transform2D, inherited_alpha: float) -> void:
    if seconds < float(node.get("visible_from",0)) or seconds >= float(node.get("visible_until",INF)): return
    var state := state_at(node,seconds)
    var alpha := inherited_alpha*float(state.alpha)
    if alpha <= 0: return
    var matrix := parent*_node_transform(state)
    painter.draw_set_transform_matrix(matrix)
    var points := PackedVector2Array()
    for point: Array in state.points: points.append(Vector2(float(point[0]),float(point[1])))
    var type := String(node.get("type","group"))
    if type == "ellipse":
        var rect: Array = node.rect
        var center := Vector2(float(rect[0])+float(rect[2])/2,float(rect[1])+float(rect[3])/2)
        for i in range(64):
            var angle := TAU*float(i)/64.0
            points.append(center+Vector2(cos(angle)*absf(float(rect[2]))/2,sin(angle)*absf(float(rect[3]))/2))
    if type in ["polygon","ellipse"] and points.size() >= 3 and _inside_panel(points,matrix,painter.size):
        points = polygon_points(points)
        if points.size() >= 3:
            # Triangulate near the origin: very thin projected faces otherwise
            # lose orientation precision at large screen coordinates.
            var origin := points[0]
            for i in range(points.size()): points[i] -= origin
            painter.draw_set_transform_matrix(matrix*Transform2D(0,origin))
            if node.has("fill"): painter.draw_colored_polygon(points,_colour(String(node.fill),alpha))
            if node.has("stroke"):
                points.append(points[0])
                painter.draw_polyline(points,_colour(String(node.stroke),alpha),float(node.get("line_width",1)),true)
    elif type == "line" and points.size() >= 2:
        painter.draw_polyline(points,_colour(String(node.get("stroke","#000000")),alpha),float(node.get("line_width",1)),true)
    for child: Dictionary in node.get("children",[]): _draw_node(painter,child,seconds,matrix,alpha)

func _draw_panel(painter: Control, panel: Dictionary) -> void:
    if current_shot.is_empty() or painter.size.x <= 0 or painter.size.y <= 0: return
    var scene_id := String(panel.get("scene",current_shot.get("scene","")))
    if not artwork.get("scenes",{}).has(scene_id): return
    var scene: Dictionary = artwork.scenes[scene_id]
    painter.draw_set_transform_matrix(Transform2D.IDENTITY)
    painter.draw_rect(Rect2(Vector2.ZERO,painter.size),Color(String(scene.get("background","#1a2327"))))
    var elapsed := current_time-float(current_shot.start)
    var progress := clampf(elapsed/(float(current_shot.end)-float(current_shot.start)),0,1)
    var from: Array = panel.get("view_from",[0,0,1,1])
    var view: Array = _interpolate(from,panel.get("view_to",from),progress)
    var art_size := Vector2(float(artwork.get("width",1280)),float(artwork.get("height",720)))
    var source := Rect2(Vector2(float(view[0]),float(view[1]))*art_size,Vector2(float(view[2]),float(view[3]))*art_size)
    var contain := String(panel.get("fit","cover")) == "contain"
    var wider := source.size.x/source.size.y > painter.size.x/painter.size.y
    if wider != contain:
        var width := source.size.y*painter.size.x/painter.size.y
        source.position.x += (source.size.x-width)/2
        source.size.x = width
    else:
        var height := source.size.x*painter.size.y/painter.size.x
        source.position.y += (source.size.y-height)/2
        source.size.y = height
    var scale := painter.size/source.size
    var camera := Transform2D(0,scale,0,-source.position*scale)
    var panel_time := maxf(0,elapsed-float(panel.get("reveal",0)))+float(panel.get("time_offset",0))
    for node: Dictionary in scene.get("nodes",[]):
        _draw_node(painter,node,panel_time,camera,1)
    painter.draw_set_transform_matrix(Transform2D.IDENTITY)

func _rect(values: Array) -> Rect2:
    return Rect2(float(values[0]),float(values[1]),float(values[2]),float(values[3]))

func layer_rect(layer: Dictionary, progress: float) -> Rect2:
    return Rect2(_rect(layer.from).position.lerp(_rect(layer.to).position,progress),_rect(layer.from).size.lerp(_rect(layer.to).size,progress))

func _quad(texture: Texture2D, quadrant: int) -> Rect2:
    var half := texture.get_size()*0.5
    return Rect2(Vector2(quadrant%2,floori(float(quadrant)/2))*half,half)

func _draw() -> void:
    draw_rect(Rect2(Vector2.ZERO,size),Color("080b0c"))
    if current_shot.is_empty() or size.x <= 0 or size.y <= 0 or current_shot.has("scene"): return
    var shot := current_shot
    var progress := clampf((current_time-float(shot.start))/(float(shot.end)-float(shot.start)),0,1)
    var texture: Texture2D = textures[shot.asset]
    var quad := _quad(texture,int(shot.quadrant))
    var view := Rect2(_rect(shot.view_from).position.lerp(_rect(shot.view_to).position,progress),_rect(shot.view_from).size.lerp(_rect(shot.view_to).size,progress))
    var source := Rect2(quad.position+view.position*quad.size,view.size*quad.size)
    if source.size.x/source.size.y > size.x/size.y:
        var width := source.size.y*size.x/size.y
        source.position.x += (source.size.x-width)*0.5
        source.size.x = width
    else:
        var height := source.size.x*size.y/size.x
        source.position.y += (source.size.y-height)*0.5
        source.size.y = height
    draw_texture_rect_region(texture,Rect2(Vector2.ZERO,size),source)
    for layer: Dictionary in shot.get("layers",[]):
        var rect := layer_rect(layer,progress)
        var actor: Texture2D = textures[layer.asset]
        draw_texture_rect_region(actor,Rect2(rect.position*size,rect.size*size),_quad(actor,int(layer.quadrant)),Color(1,1,1,float(layer.get("alpha",1))))

func clear() -> void:
    for clip in _clips:
        remove_child(clip)
        clip.queue_free()
    _clips.clear()
    _painters.clear()
    textures.clear()
    artwork.clear()
    shots.clear()
    current_shot = {}
    if is_instance_valid(_label):
        remove_child(_label)
        _label.queue_free()
    _label = null
