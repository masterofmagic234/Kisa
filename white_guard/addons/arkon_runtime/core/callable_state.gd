extends RefCounted
class_name CallableState

var _enter: Callable
var _update: Callable
var _exit: Callable

func _init(enter_callback: Callable = Callable(), update_callback: Callable = Callable(), exit_callback: Callable = Callable()) -> void:
    _enter = enter_callback
    _update = update_callback
    _exit = exit_callback

func enter() -> void:
    if _enter.is_valid():
        _enter.call()

func update(delta: float) -> void:
    if _update.is_valid():
        _update.call(delta)

func exit() -> void:
    if _exit.is_valid():
        _exit.call()
