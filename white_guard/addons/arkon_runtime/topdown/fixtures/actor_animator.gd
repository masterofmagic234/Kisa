extends RefCounted
## Presentation adapter with the release animator's call interface.
var _sprite: AnimatedSprite2D
func configure(sprite: AnimatedSprite2D, _kind: StringName, _phase: float) -> void:
    _sprite = sprite
func update(_delta: float, _velocity: Vector2, _rotation: float, _windup: float, _duration: float, _stunned: bool, dead: bool) -> void:
    if is_instance_valid(_sprite):
        _sprite.set("fallen", dead)
        _sprite.queue_redraw()
func attack() -> void: pass
func interrupt() -> void: pass
func fall() -> void:
    if is_instance_valid(_sprite):
        _sprite.set("fallen", true)
        _sprite.queue_redraw()
