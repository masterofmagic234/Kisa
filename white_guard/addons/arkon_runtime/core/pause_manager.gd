extends Node
# Autoload singleton. Centralized pause handling.

signal pause_state_changed(paused: bool)

var is_paused := false
var pause_action := "ui_cancel"

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS

func _unhandled_input(event: InputEvent) -> void:
    if event.is_action_pressed(pause_action):
        toggle()
        get_viewport().set_input_as_handled()

func toggle() -> void:
    set_paused(not is_paused)

func set_paused(paused: bool) -> void:
    if is_paused == paused:
        return
    is_paused = paused
    get_tree().paused = paused
    pause_state_changed.emit(paused)

func resume() -> void:
    set_paused(false)
