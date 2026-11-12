extends Node2D
## Geometry for engineering checks only; not Mazur's final sprite.
var _dead := false
var _armed := true
var _weapon: StringName = &"pistol"
var _pulse := 0.0
func configure() -> void: queue_redraw()
func set_weapon(value: StringName) -> void:
    _weapon = value
    queue_redraw()
func set_weapon_drawn(value: bool) -> void:
    _armed = value
    queue_redraw()
func update_motion(delta: float, _velocity: Vector2) -> void:
    _pulse = maxf(0, _pulse - delta)
    queue_redraw()
func clear_motion() -> void: pass
func attack() -> void: _pulse = 0.08
func die() -> void:
    _dead = true
    queue_redraw()
func _draw() -> void:
    draw_circle(Vector2.ZERO, 7, Color("88bba5") if not _dead else Color("67534d"))
    draw_circle(Vector2(2, 0), 4, Color("e6d7b3"))
    if _armed and not _dead:
        draw_line(Vector2(3, 4), Vector2(15 if _weapon != &"bat" else 20, 4), Color("253330"), 3)
        if _pulse > 0: draw_circle(Vector2(17, 4), 3, Color("ffd58d"))
