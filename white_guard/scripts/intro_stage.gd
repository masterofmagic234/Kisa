extends Control
## A shot is a staged visual beat, independent of paragraph length or frame rate.
var shots: Array = []
var textures: Dictionary = {}
var current_shot: Dictionary = {}
var current_time := 0.0
var _font := preload("res://assets/reused/fonts/Manrope-Variable.ttf")

func configure(records: Array) -> void:
    shots = records
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    clip_contents = true
    for shot: Dictionary in shots:
        _load_asset(shot.asset)
        for layer: Dictionary in shot.get("layers", []): _load_asset(layer.asset)
    show_at(0)

func _load_asset(path: String) -> void:
    if not textures.has(path): textures[path] = load(path)

func show_at(seconds: float) -> void:
    current_time = seconds
    current_shot = shot_at(seconds)
    queue_redraw()

func shot_at(seconds: float) -> Dictionary:
    for shot: Dictionary in shots:
        if seconds >= float(shot.start) and seconds < float(shot.end): return shot
    return shots.back() if not shots.is_empty() else {}

func _quad(texture: Texture2D, quadrant: int) -> Rect2:
    var half := texture.get_size() * 0.5
    return Rect2(Vector2(quadrant % 2, floori(float(quadrant)/2.0)) * half, half)

func _rect(values: Array) -> Rect2:
    return Rect2(float(values[0]), float(values[1]), float(values[2]), float(values[3]))

func _mix_rect(a: Array, b: Array, t: float) -> Rect2:
    return Rect2(_rect(a).position.lerp(_rect(b).position,t), _rect(a).size.lerp(_rect(b).size,t))

func layer_rect(layer: Dictionary, progress: float) -> Rect2:
    return _mix_rect(layer.from, layer.to, progress)

func _draw() -> void:
    if current_shot.is_empty() or size.x<=0 or size.y<=0: return
    var shot := current_shot
    var elapsed := current_time - float(shot.start)
    var progress := clampf(elapsed/(float(shot.end)-float(shot.start)),0,1)
    var texture: Texture2D = textures[shot.asset]
    var quad := _quad(texture, int(shot.quadrant))
    var view := _mix_rect(shot.view_from,shot.view_to,progress)
    var source := Rect2(quad.position + view.position*quad.size, view.size*quad.size)
    # Cover the screen, preserving geometry rather than stretching the drawing.
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
    var label := String(shot.get("label",""))
    if not label.is_empty() and elapsed<3.5:
        var fade := clampf((3.5-elapsed)*2,0,1)
        var at := Vector2(34,48)*size/Vector2(1280,720)
        var font_size := maxi(14,int(18*size.x/1280))
        var width := _font.get_string_size(label,HORIZONTAL_ALIGNMENT_LEFT,-1,font_size).x
        draw_rect(Rect2(at-Vector2(12,font_size+8),Vector2(width+24,font_size+19)),Color(0.06,0.07,0.05,0.80*fade))
        draw_string(_font,at,label,HORIZONTAL_ALIGNMENT_LEFT,-1,font_size,Color(0.94,0.90,0.80,fade))
    _effects(String(shot.get("effect","")),elapsed,progress)

func _effects(effect: String, elapsed: float, progress: float) -> void:
    if effect=="four_attempts":
        for i in range(4):
            if elapsed<float(i)*0.65: continue
            var x := 0.065+float(i)*0.032
            draw_line(Vector2(x,0.72)*size,Vector2(x+0.017,0.63)*size,Color("ad3b2e"),5,true)
    elif effect=="rifle_fire":
        var pulse := fposmod(elapsed,0.43)<0.075 and elapsed<1.8
        if pulse:
            for at in [Vector2(0.49,0.21),Vector2(0.75,0.24)]:
                draw_circle(at*size,8*size.x/1280,Color(1,0.85,0.50,0.8))
    elif effect=="rocket":
        var t := clampf(elapsed/2.0,0,1)
        var end := Vector2(0.22,0.65).lerp(Vector2(0.89,0.08),t)*size
        var start := end-Vector2(0.02,-0.02)*size
        draw_line(start,end,Color("fff1b6"),3,true)

func clear() -> void:
    textures.clear()
    shots.clear()
    current_shot = {}
