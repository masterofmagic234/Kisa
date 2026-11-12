extends Node
## Persistent hardware recommendation plus conservative per-level runtime fallback.
signal settings_changed()

const Policy = preload("res://addons/arkon_runtime/graphics/graphics_policy156.gd")
const Store = preload("res://addons/arkon_runtime/graphics/graphics_store156.gd")
const Monitor = preload("res://addons/arkon_runtime/graphics/graphics_monitor156.gd")
const LEVELS := [&"foot", &"vehicle", &"interior"]
var config_path := "user://graphics.cfg"
var _initialized := false
var _hardware: Dictionary = {}
var _recommendation: Dictionary = {}
var _config: Dictionary = {}
var _first_run := false
var _level_id: StringName = &""
var _owner: WeakRef
var _owner_id := 0
var _suspensions: Dictionary = {}
var _next_token := 1
var _monitor = Monitor.new()
var _previous_usec := 0
var _pipeline_count := 0
var _focused := true
var _last_valid := false
var _startup_cap := 0
var _runtime_measurements_supported := false

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    _startup_cap = Engine.max_fps
    _refresh_runtime_capability()
    if not _initialized: initialize_for_device(_detect_hardware())
    get_tree().root.size_changed.connect(_on_viewport_resize)

func _detect_hardware() -> Dictionary:
    var memory := OS.get_memory_info()
    var gpu := RenderingServer.get_video_adapter_name()
    var software := Policy.is_software_renderer(gpu, OS.get_name())
    return {"gpu_name":gpu, "gpu_vendor":RenderingServer.get_video_adapter_vendor(), "platform":OS.get_name(), "model":OS.get_model_name(), "cpu_cores":OS.get_processor_count(), "memory_bytes":memory.get("physical",0), "headless":DisplayServer.get_name() == "headless", "software_renderer":software}

func _refresh_runtime_capability() -> void:
    _runtime_measurements_supported = OS.get_name() in ["Android","iOS","Windows"] and DisplayServer.get_name() != "headless"
    if Policy.is_software_renderer(RenderingServer.get_video_adapter_name(), OS.get_name()):
        _runtime_measurements_supported = false

func initialize_for_device(info: Dictionary, path: String = "") -> void:
    if path != "": config_path = path
    _hardware = info.duplicate(true)
    _recommendation = Policy.classify_hardware(_hardware)
    _first_run = not FileAccess.file_exists(config_path)
    _config = Store.load_config(config_path)
    var fingerprint := _fingerprint(_hardware)
    if _config.hardware_fingerprint != fingerprint:
        _config.adaptive_steps = {}
        _config.hardware_fingerprint = fingerprint
    _initialized = true
    _reset_observation()
    _save()
    if is_inside_tree():
        apply_root_viewport()
        settings_changed.emit()

func _fingerprint(info: Dictionary) -> String:
    var memory := float(info.get("memory_bytes",0))
    var policy_version := "policy157.windows.1" if str(info.get("platform", "")).to_lower() == "windows" else "policy156.2"
    var fields := [policy_version,str(info.get("platform","")),str(info.get("gpu_name","")),str(info.get("gpu_vendor","")),str(info.get("model","")),str(info.get("cpu_cores",0)),str(roundi(memory / 268435456.0)) if memory > 0 else "unknown",str(info.get("software_renderer",false))]
    return "|".join(fields).sha256_text()

func _profile() -> String:
    return str(_recommendation.get("profile","low")) if _config.get("mode","auto") == "auto" else str(_config.mode)

func get_level_settings(level_id: StringName) -> Dictionary:
    var step := int(_config.get("adaptive_steps",{}).get(String(level_id),0)) if _config.get("mode","auto") == "auto" else 0
    return Policy.level_settings(_profile(),level_id,step,str(_hardware.get("platform","")))

func get_summary() -> Dictionary:
    var effective := get_level_settings(_level_id)
    return {"mode":_config.get("mode","auto"), "profile":effective.profile, "base_profile":_profile(), "target_fps":effective.target_fps, "hardware":_hardware.duplicate(true), "reason":_recommendation.get("reason",""), "first_run":_first_run, "active_level":String(_level_id), "adaptation_enabled":_can_adapt(), "adaptive_steps":_config.get("adaptive_steps",{}).duplicate(true), "monitor":_monitor.diagnostics(), "suspensions":_suspensions.size(), "config_path":config_path}

func set_mode(mode: String) -> bool:
    if mode not in ["auto","low","balanced","high"]: return false
    if _config.get("mode","auto") == mode: return true
    _config.mode = mode
    _reset_observation()
    _save()
    apply_root_viewport()
    settings_changed.emit()
    return true

func redetect() -> void:
    _config.mode = "auto"
    _config.adaptive_steps = {}
    # A real re-detection, never a menu benchmark or fake progress animation.
    _hardware = _detect_hardware()
    _refresh_runtime_capability()
    _recommendation = Policy.classify_hardware(_hardware)
    _config.hardware_fingerprint = _fingerprint(_hardware)
    _first_run = false
    _reset_observation()
    _save()
    apply_root_viewport()
    settings_changed.emit()

func enter_level(level_id: StringName, owner: Node) -> void:
    if level_id not in LEVELS or not is_instance_valid(owner): return
    if _owner_id == owner.get_instance_id() and _level_id == level_id:
        apply_root_viewport()
        return
    _owner = weakref(owner)
    _owner_id = owner.get_instance_id()
    _level_id = level_id
    var id := _owner_id
    var cleanup := _owner_exiting.bind(id)
    if not owner.tree_exiting.is_connected(cleanup):
        owner.tree_exiting.connect(cleanup, CONNECT_ONE_SHOT)
    _reset_observation()
    apply_root_viewport()

func leave_level(owner: Node) -> void:
    if is_instance_valid(owner): _owner_exiting(owner.get_instance_id())

func _owner_exiting(id: int) -> void:
    if id != _owner_id: return
    _owner = null
    _owner_id = 0
    _level_id = &""
    _suspensions.clear()
    _reset_observation()
    if is_inside_tree(): apply_root_viewport()

func apply_root_viewport() -> void:
    if not is_inside_tree() or not _initialized: return
    var viewport := get_tree().root
    var settings := get_level_settings(_level_id)
    if _level_id == &"foot":
        var pixels := maxf(1.0,float(viewport.size.x) * float(viewport.size.y))
        viewport.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
        viewport.scaling_3d_scale = minf(1.0,sqrt(float(settings.pixel_budget) / pixels))
        viewport.msaa_3d = settings.msaa_3d
        viewport.mesh_lod_threshold = settings.mesh_lod_threshold
        viewport.use_occlusion_culling = true
    else:
        viewport.scaling_3d_scale = 1.0
        viewport.msaa_3d = Viewport.MSAA_DISABLED
        viewport.mesh_lod_threshold = 1.0
        viewport.use_occlusion_culling = false
    # Headless automatic fixtures must not be throttled by a phone target.
    # Gameplay/physics clocks, vsync, root canvas scale and disable_3d are untouched.
    if _config.get("mode","auto") == "auto" and bool(_hardware.get("headless",false)):
        Engine.max_fps = _startup_cap
    else:
        Engine.max_fps = int(settings.target_fps)

func _on_viewport_resize() -> void:
    if _suspensions.is_empty(): apply_root_viewport()

func suspend_adaptation(owner: Node) -> int:
    if not is_instance_valid(owner): return 0
    var token := _next_token
    _next_token += 1
    _suspensions[token] = weakref(owner)
    _reset_observation()
    return token

func resume_adaptation(token: int) -> void:
    _suspensions.erase(token)
    _reset_observation()
    if _suspensions.is_empty(): apply_root_viewport()

func _can_adapt() -> bool:
    if not _initialized or str(_hardware.get("platform","")) not in ["Android","iOS","Windows"]: return false
    if not _runtime_measurements_supported: return false
    return _config.get("mode","auto") == "auto" and not bool(_hardware.get("headless",false)) and not bool(_recommendation.get("software",false)) and _owner != null and _owner.get_ref() != null and _suspensions.is_empty() and _focused and Engine.time_scale == 1.0

func _gameplay_allowed() -> bool:
    if not _can_adapt() or get_tree().paused: return false
    var owner: Node = _owner.get_ref()
    if owner == null or not owner.is_inside_tree() or not owner.can_process(): return false
    var flow := get_node_or_null("/root/SceneFlow")
    return flow == null or not bool(flow.get("_transitioning"))

func observe_frame(owner: Node, level_id: StringName, active: bool) -> void:
    if not is_instance_valid(owner) or owner.get_instance_id() != _owner_id or level_id != _level_id: return
    var now := Time.get_ticks_usec()
    var elapsed := float(now - _previous_usec) / 1000.0 if _previous_usec != 0 else 0.0
    _previous_usec = now
    if not active or not _gameplay_allowed():
        _monitor.note_invalid(now)
        _last_valid = false
        return
    var compiled := _compilations()
    if compiled != _pipeline_count:
        _pipeline_count = compiled
        _monitor.note_invalid(now)
        _last_valid = false
        return
    if not _last_valid:
        _monitor.reset(now)
        _last_valid = true
        return
    var settings := get_level_settings(level_id)
    if _monitor.observe(now,elapsed,true,int(settings.target_fps)):
        var steps: Dictionary = _config.adaptive_steps
        var step := int(steps.get(String(level_id),0))
        if step < 2:
            steps[String(level_id)] = step + 1
            _save()
            apply_root_viewport()
            settings_changed.emit()

func _process(_delta: float) -> void:
    for token in _suspensions.keys():
        if _suspensions[token].get_ref() == null: _suspensions.erase(token)
    if not _gameplay_allowed():
        if _last_valid: _monitor.note_invalid(Time.get_ticks_usec())
        _previous_usec = 0
        _last_valid = false

func _notification(what: int) -> void:
    if what in [NOTIFICATION_APPLICATION_FOCUS_OUT,NOTIFICATION_APPLICATION_PAUSED]:
        _focused = false
        _reset_observation()
    elif what in [NOTIFICATION_APPLICATION_FOCUS_IN,NOTIFICATION_APPLICATION_RESUMED]:
        _focused = true
        _reset_observation()

func _compilations() -> int:
    var total := 0
    for kind in [RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_SURFACE,RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_DRAW,RenderingServer.RENDERING_INFO_PIPELINE_COMPILATIONS_SPECIALIZATION]:
        total += RenderingServer.get_rendering_info(kind)
    return total

func _reset_observation() -> void:
    _previous_usec = 0
    _last_valid = false
    _monitor.reset(Time.get_ticks_usec())
    _pipeline_count = _compilations() if is_inside_tree() else 0

func _save() -> void:
    var error := Store.save_config(config_path,_config)
    if error != OK: push_warning("Graphics settings could not be saved: %s" % error_string(error))
