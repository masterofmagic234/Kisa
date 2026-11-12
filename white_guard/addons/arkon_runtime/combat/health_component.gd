extends Node
class_name ArkonHealthComponent

signal health_changed(current: int, maximum: int)
signal died

@export var max_health: int = 100
@export var invulnerability_duration: float = 0.24

var current_health: int = 100
var invulnerability_timer: float = 0.0
var is_dead: bool = false

func _ready() -> void:
    reset(max_health)

func _process(delta: float) -> void:
    invulnerability_timer = maxf(0.0, invulnerability_timer - delta)

func reset(value: int = max_health) -> void:
    max_health = maxi(value, 1)
    current_health = max_health
    invulnerability_timer = 0.0
    is_dead = false
    health_changed.emit(current_health, max_health)

func can_take_damage() -> bool:
    return not is_dead and invulnerability_timer <= 0.0

func apply_damage(amount: int, _source: Node = null) -> bool:
    if not can_take_damage():
        return false
    var damage := maxi(amount, 0)
    if damage <= 0:
        return false
    invulnerability_timer = invulnerability_duration
    current_health = maxi(current_health - damage, 0)
    health_changed.emit(current_health, max_health)
    _emit_global_damage(damage, _source)
    _emit_global_health()
    if current_health <= 0:
        is_dead = true
        died.emit()
        _emit_global_death()
    return true

func force_kill() -> bool:
    if is_dead:
        return false
    current_health = 0
    is_dead = true
    health_changed.emit(current_health, max_health)
    _emit_global_health()
    died.emit()
    _emit_global_death()
    return true

func heal(amount: int) -> void:
    if is_dead:
        return
    var old := current_health
    current_health = mini(current_health + maxi(amount, 0), max_health)
    if current_health != old:
        health_changed.emit(current_health, max_health)
        _emit_global_health()

func _emit_global_health() -> void:
    var actor := get_parent()
    var bus := get_node_or_null("/root/SignalBus")
    if bus != null and bus.has_signal("health_changed"):
        bus.health_changed.emit(actor, current_health, max_health)

func _emit_global_damage(amount: int, source: Node) -> void:
    var entity := get_parent()
    var bus := get_node_or_null("/root/SignalBus")
    if bus != null and bus.has_signal("entity_damaged"):
        bus.entity_damaged.emit(entity, amount, source)

func _emit_global_death() -> void:
    var entity := get_parent()
    var bus := get_node_or_null("/root/SignalBus")
    if bus != null and bus.has_signal("entity_died"):
        bus.entity_died.emit(entity)
