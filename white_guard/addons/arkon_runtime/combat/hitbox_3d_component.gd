extends Area3D
class_name ArkonHitbox3DComponent

signal hit(source: Node, amount: int)

@export var hit_layer: int = 2
@export var hit_mask: int = 0

@onready var collider: CollisionShape3D = $CollisionShape3D

func _ready() -> void:
    collision_layer = hit_layer
    collision_mask = hit_mask
    monitoring = false
    monitorable = true
    if collider == null:
        push_error("ArkonHitbox3DComponent requires a child CollisionShape3D in its PackedScene.")

func receive_hit(amount: int, source: Node = null) -> bool:
    hit.emit(source, amount)
    var parent_node := get_parent()
    if parent_node != null and parent_node.has_method("take_damage"):
        return bool(parent_node.take_damage(amount, source))
    return false
