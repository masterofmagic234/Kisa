extends Control
class_name ArkonLevel3TouchJoystick
signal vector_changed(value: Vector2)
signal released
@export var active_radius := 67.0
var _pointer_id := -1000
var _active := false
var _value := Vector2.ZERO
var caption := ""
func _ready() -> void:
    mouse_filter = Control.MOUSE_FILTER_IGNORE
    queue_redraw()
func begin_pointer(id: int, screen_position: Vector2) -> bool:
    if _active: return false
    _active = true
    _pointer_id = id
    update_pointer(screen_position)
    return true
func update_pointer(screen_position: Vector2) -> void:
    var offset := screen_position-get_global_rect().get_center()
    var raw := offset.limit_length(active_radius)/maxf(active_radius,1)
    _value = Vector2.ZERO if raw.length()<0.12 else raw.normalized()*inverse_lerp(0.12,1.0,raw.length())
    vector_changed.emit(_value)
    queue_redraw()
func _release() -> void:
    _active = false
    _pointer_id = -1000
    _value = Vector2.ZERO
    vector_changed.emit(_value)
    released.emit()
    queue_redraw()
func get_value() -> Vector2: return _value
func _draw() -> void:
    var center := size*0.5
    draw_circle(center,active_radius,Color(0.035,0.07,0.10,0.34))
    draw_arc(center,active_radius,0,TAU,48,Color(0.50,0.75,0.79,0.32),1.5,true)
    draw_arc(center,active_radius*0.57,0,TAU,40,Color(0.50,0.75,0.79,0.12),1,true)
    var knob := center+_value*active_radius
    draw_circle(knob,active_radius*0.29,Color(0.43,0.72,0.76,0.56 if _active else 0.34))
    draw_circle(knob,active_radius*0.29,Color(0.68,0.86,0.84,0.47),false,1.5,true)
    if not caption.is_empty():
        var font := ThemeDB.fallback_font
        draw_string(font,Vector2(center.x-font.get_string_size(caption,HORIZONTAL_ALIGNMENT_LEFT,-1,10).x*0.5,size.y-1),caption,HORIZONTAL_ALIGNMENT_LEFT,-1,10,Color(0.59,0.77,0.79,0.55))
