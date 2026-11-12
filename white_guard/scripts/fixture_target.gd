extends Node3D
func _ready() -> void:
    $Health.died.connect(func(): visible = false; $Hitbox.set_deferred("monitorable", false))

func take_damage(amount: int, source: Node = null) -> bool:
    return $Health.apply_damage(amount, source)
