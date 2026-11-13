extends Control
## Composites the opening's PNG plates at absolute narration time.
const FRAME := Vector2(768, 432)
const MATTE := Color("141713")
const AVENUE_HORIZON := Vector2(689, 117)
const AVENUE_NEAR_Y := 450.0
const BODY_FONT := preload("res://assets/reused/fonts/Manrope-Variable.ttf")

var storyboard: Dictionary = {}
var textures: Dictionary = {}
var current_shot: Dictionary = {}
var current_time := 0.0

func configure(plan: Dictionary) -> void:
    storyboard = plan
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    for asset: Array in storyboard.art.values():
        var path: String = asset[0]
        if not textures.has(path):
            textures[path] = load(path)
    show_at(0)

func show_at(seconds: float) -> void:
    current_time = seconds
    current_shot = shot_at(seconds)
    queue_redraw()

func shot_at(seconds: float) -> Dictionary:
    var shots: Array = storyboard.get("shots", [])
    for shot: Dictionary in shots:
        if seconds < float(shot.end):
            return shot
    return shots.back() if not shots.is_empty() else {}

func _rect(values: Array) -> Rect2:
    return Rect2(float(values[0]), float(values[1]), float(values[2]), float(values[3]))

func _track(keys: Array, elapsed: float, ease: bool) -> Array:
    var previous: Array = keys[0]
    for i in range(1, keys.size()):
        var next: Array = keys[i]
        if elapsed < float(next[0]):
            var weight := clampf(inverse_lerp(float(previous[0]), float(next[0]), elapsed), 0, 1)
            if ease:
                weight = weight * weight * (3 - 2 * weight)
            var result: Array = []
            for field in range(1, previous.size()):
                result.append(lerpf(float(previous[field]), float(next[field]), weight))
            return result
        previous = next
    return previous.slice(1)

func _draw() -> void:
    if current_shot.is_empty() or size.x <= 0 or size.y <= 0:
        return
    draw_rect(Rect2(Vector2.ZERO, size), MATTE)
    var scale_to_screen := minf(size.x / FRAME.x, size.y / FRAME.y)
    var frame := Rect2((size - FRAME * scale_to_screen) / 2, FRAME * scale_to_screen)
    var elapsed := maxf(0, current_time - float(current_shot.start))
    for panel: Dictionary in current_shot.panels:
        if elapsed >= float(panel.get("reveal", 0)):
            _draw_panel(panel, frame, scale_to_screen, elapsed - float(panel.get("reveal", 0)))
    var label := String(current_shot.get("label", ""))
    if not label.is_empty():
        var opacity := clampf((4.4 - elapsed) * 1.5, 0, 1)
        var pos := frame.position + Vector2(28, 46) * scale_to_screen
        var font_size := maxi(12, roundi(14 * scale_to_screen))
        var width := BODY_FONT.get_string_size(label, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size).x
        draw_rect(Rect2(pos - Vector2(10, 20) * scale_to_screen, Vector2(width + 20 * scale_to_screen, 28 * scale_to_screen)), Color(0.08, 0.09, 0.07, 0.88 * opacity))
        draw_string(BODY_FONT, pos, label, HORIZONTAL_ALIGNMENT_LEFT, -1, font_size, Color(0.94, 0.9, 0.8, opacity))

func _draw_panel(panel: Dictionary, frame: Rect2, scale_to_screen: float, elapsed: float) -> void:
    var placement := _rect(panel.get("rect", [0, 0, FRAME.x, FRAME.y]))
    var target := Rect2(frame.position + placement.position * scale_to_screen, placement.size * scale_to_screen)
    if panel.get("mirror", false):
        draw_set_transform(Vector2(2 * target.get_center().x, 0), 0, Vector2(-1, 1))
    var view := _rect(panel.crop)
    if panel.has("camera"):
        var offset := _track(panel.camera, elapsed, true)
        view.position += Vector2(offset[0], offset[1])
    # Fit to the panel without stretching any part of the drawing.
    var fitted := Vector2(minf(view.size.x, view.size.y * target.size.aspect()), minf(view.size.y, view.size.x / target.size.aspect()))
    view = Rect2(view.get_center() - fitted / 2, fitted)
    var tone: Array = panel.get("tone", [1, 1, 1])
    var tint := Color(float(tone[0]), float(tone[1]), float(tone[2]))
    if panel.has("art"):
        _draw_sprite(panel.art, Vector2.ZERO, 1, view, target, tint)
    for layer: Dictionary in panel.get("behind", []):
        _draw_layer(layer, view, target, tint, elapsed)
    if panel.has("traffic"):
        _draw_traffic(panel.traffic, view, target, tint, elapsed)
    for layer: Dictionary in panel.get("layers", []):
        _draw_layer(layer, view, target, tint, elapsed)
    draw_set_transform(Vector2.ZERO)

func _draw_layer(layer: Dictionary, view: Rect2, target: Rect2, tint: Color, elapsed: float) -> void:
    var motion := _track(layer.motion, elapsed, bool(layer.get("ease", true))) if layer.has("motion") else [0, 0, 1]
    if float(motion[2]) <= 0:
        return
    var at := Vector2(layer.at[0], layer.at[1]) + Vector2(motion[0], motion[1])
    var sprite := String(layer.get("art", ""))
    if layer.has("pose"):
        var frames: Array = storyboard.poses[layer.pose]
        var phase := float(_track(layer.phase, elapsed, true)[0])
        sprite = frames[roundi(clampf(phase, 0, 1) * (frames.size() - 1))]
    var visible_width := float(_track(layer.wipe, elapsed, false)[0]) if layer.has("wipe") else 1.0
    tint.a *= float(motion[2])
    _draw_sprite(sprite, at, float(layer.get("scale", 1)), view, target, tint, visible_width)

func _draw_sprite(sprite: String, at: Vector2, scale_: float, view: Rect2, target: Rect2, tint: Color, visible_width := 1.0) -> void:
    var data: Array = storyboard.art[sprite]
    var extent := Vector2(data[3], data[4])
    var destination := Rect2(at, extent * scale_ * Vector2(visible_width, 1))
    var visible := destination.intersection(view)
    if not visible.has_area():
        return
    var source := Rect2(Vector2(data[1], data[2]) + (visible.position - at) / scale_, visible.size / scale_)
    var ratio := target.size / view.size
    var mapped := Rect2(target.position + (visible.position - view.position) * ratio, visible.size * ratio)
    draw_texture_rect_region(textures[data[0]], mapped, source, tint, false, true)

func _draw_traffic(traffic: Dictionary, view: Rect2, target: Rect2, tint: Color, elapsed: float) -> void:
    var distance := float(traffic.get("offset", 0)) + elapsed * float(traffic.rate)
    var fleet: Dictionary = storyboard.formations[traffic.fleet]
    # The arrays are painted in ground depth order, furthest first.
    for vehicle: Array in fleet.vehicles:
        var sprite := String(vehicle[0])
        var art: Array = storyboard.art[sprite]
        var placement := vehicle_placement(fleet.projection, vehicle, distance)
        if placement.is_empty():
            continue
        var scale_: float = placement.scale
        var anchor := Vector2(art[3], art[4]) * Vector2(vehicle[4], vehicle[5])
        var at: Vector2 = placement.ground - anchor * scale_
        _draw_sprite(sprite, at, scale_, view, target, tint)
        for wheel: Array in storyboard.wheels.get(sprite, []):
            var frames: Array = storyboard.poses[wheel[0]]
            var phase := posmod(floori(0.11 * distance / scale_), frames.size())
            _draw_sprite(frames[phase], at + Vector2(wheel[1], wheel[2]) * scale_, scale_, view, target, tint)

func vehicle_placement(projection: String, vehicle: Array, distance: float) -> Dictionary:
    var width: float = storyboard.art[vehicle[0]][3]
    if projection == "side":
        return {"ground": Vector2(float(vehicle[1]) + distance, vehicle[2]), "scale": float(vehicle[3]) / width}
    var depth := float(vehicle[2]) - distance
    if depth < 0.58:
        return {} # The ground point and the entire vehicle have passed the frame.
    return {
        "ground": AVENUE_HORIZON + (Vector2(float(vehicle[1]), AVENUE_NEAR_Y) - AVENUE_HORIZON) / depth,
        "scale": float(vehicle[3]) / (width * depth),
    }

func clear() -> void:
    textures.clear()
    storyboard = {}
    current_shot = {}
