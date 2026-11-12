extends Node

# SignalBus (Autoload)
# Global gameplay event contract.
#
# RULE:
# - Commands stay local to the scene/actor that owns them.
# - Signals here describe facts that already happened.
# - Presentation systems may listen without gameplay systems knowing about them.

# ==========================================
# GAMEPLAY / PROGRESSION
# ==========================================

signal mission_changed(level_id: StringName, status: StringName)
signal level_completed(level_id: StringName)


# ==========================================
# COMBAT / HEALTH
# ==========================================

signal entity_damaged(entity: Node, amount: int, source: Node)
signal health_changed(entity: Node, current_hp: int, max_hp: int)
signal entity_died(entity: Node)
signal entity_stunned(entity: Node, duration: float)

signal weapon_changed(
    entity: Node,
    weapon_id: StringName,
    current_ammo: int
)

signal combat_event(kind: StringName, position: Vector2)


# ==========================================
# INTERACTION / WORLD
# ==========================================

signal item_collected(
    item_kind: StringName,
    item_id: StringName,
    amount: int,
    collector: Node
)

signal object_interacted(
    object_id: StringName,
    state: StringName
)

signal enemy_defeated(enemy: Node)


# ==========================================
# RACING (LEVEL 2)
# ==========================================

signal race_started()
signal race_countdown_changed(value: int)
signal race_time_changed(race_time: float, lap_time: float, best_lap: float)
signal racer_lap_completed(racer: Node, lap_time: float, best_time: float)
signal racer_finished(racer: Node, position: int)
signal racer_position_changed(racer: Node, current_position: int)
signal racer_progress_changed(racer: Node, progress: float)


# ==========================================
# PRESENTATION
# ==========================================

signal show_message(text: String, duration: float)
signal audio_event(kind: StringName, position: Vector3)


func emit_audio_event(kind: StringName, position: Vector3 = Vector3.ZERO) -> void:
    audio_event.emit(kind, position)
