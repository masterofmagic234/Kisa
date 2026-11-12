extends AnimatedSprite2D
## Preserves the original scene's visual node type without squirrel assets.
var fallen := false
func _draw() -> void:
    draw_circle(Vector2.ZERO, 7, Color("bb705b") if not fallen else Color("614d43"))
    draw_circle(Vector2(2, 0), 4, Color("e6d7b3"))
    if not fallen: draw_line(Vector2(3, 4), Vector2(14, 4), Color("342b29"), 3)
