extends Node
## Generalized Arkon SceneFlow contract; campaign paths belong to the consumer.
signal transition_finished(id: StringName)
signal transition_failed(id: StringName, reason: String)
const Gate = preload("res://addons/arkon_runtime/core/startup_gate.gd")
var routes: Dictionary = {}
var campaign: Array[StringName] = []
var _transitioning := false
var _generation := 0
var _active := true
var transition_log: Array[Dictionary] = []

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    SignalBus.level_completed.connect(complete_level)

func _notification(what: int) -> void:
    if what in [NOTIFICATION_APPLICATION_PAUSED, NOTIFICATION_APPLICATION_FOCUS_OUT]: _active = false
    elif what in [NOTIFICATION_APPLICATION_RESUMED, NOTIFICATION_APPLICATION_FOCUS_IN]: _active = true

func configure(scene_routes: Dictionary, order: Array[StringName] = []) -> void:
    routes = scene_routes.duplicate()
    campaign = order.duplicate()

func is_loading_gameplay() -> bool:
    return _transitioning

func complete_level(id: StringName) -> void:
    if _transitioning or not campaign.has(id): return
    if GameState.is_completed(id) and GameState.current_level != id: return
    GameState.mark_completed(id)
    var index := campaign.find(id)
    if index + 1 < campaign.size(): go_to(campaign[index + 1])

func go_to(id: StringName) -> void:
    if _transitioning: return
    var path := String(routes.get(id, ""))
    if path.is_empty() or not ResourceLoader.exists(path, "PackedScene"):
        transition_failed.emit(id, "Scene is not registered or is missing")
        return
    PauseManager.resume()
    AudioManager.begin_transition_music160()
    _transitioning = true
    _generation += 1
    call_deferred("_load", id, path, _generation)

func _load(id: StringName, path: String, token: int) -> void:
    # Leave physics callbacks and give the outgoing presentation one frame.
    await get_tree().process_frame
    if token != _generation: return
    var err := ResourceLoader.load_threaded_request(path, "PackedScene")
    if err != OK:
        _fail(id, "Resource request failed", token)
        return
    var gate = Gate.new()
    gate.reset(Time.get_ticks_usec())
    while ResourceLoader.load_threaded_get_status(path) == ResourceLoader.THREAD_LOAD_IN_PROGRESS:
        await get_tree().process_frame
        if token != _generation: return
        if gate.observe(Time.get_ticks_usec(), _active and not get_tree().paused, 30):
            _fail(id, "Resource preparation timed out", token)
            return
    if ResourceLoader.load_threaded_get_status(path) != ResourceLoader.THREAD_LOAD_LOADED:
        _fail(id, "Resource load failed", token)
        return
    var packed := ResourceLoader.load_threaded_get(path) as PackedScene
    err = get_tree().change_scene_to_packed(packed)
    if err != OK:
        _fail(id, "Scene replacement failed", token)
        return
    await get_tree().scene_changed
    await get_tree().process_frame
    if token != _generation: return
    gate.reset(Time.get_ticks_usec())
    var scene := get_tree().current_scene
    while is_instance_valid(scene) and scene.has_method("is_ready_for_gameplay") and not bool(scene.call("is_ready_for_gameplay")):
        await get_tree().process_frame
        if token != _generation: return
        if gate.observe(Time.get_ticks_usec(), _active and not get_tree().paused, 45):
            _fail(id, "Scene readiness timed out; retry is available", token)
            return
    GameState.current_level = id
    _transitioning = false
    AudioManager.finish_transition_music160()
    transition_log.append({"id": String(id), "result": "ready"})
    transition_finished.emit(id)

func _fail(id: StringName, reason: String, token: int) -> void:
    if token != _generation: return
    _transitioning = false
    PauseManager.resume()
    AudioManager.finish_transition_music160()
    transition_log.append({"id": String(id), "result": "failed", "reason": reason})
    transition_failed.emit(id, reason)
