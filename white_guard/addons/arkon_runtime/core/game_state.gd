extends Node
## Adapted from Arkon 1.68 game_state.gd: persistent data, never actor references.
## Campaign records and settings use this project's own user directory.
const SAVE_PATH := "user://settings.cfg"
const PROGRESS_PATH := "user://campaign.cfg"
var completed_levels: Array[StringName] = []
var current_level: StringName = &""
var player_hp := 100
var player_max_hp := 100
var player_ammo := 12
var music_volume_db := -9.0
var sfx_volume_db := 0.0
var music_muted := false
var sfx_muted := false
var facts: Dictionary = {}

func _ready() -> void:
    load_settings()

func mark_completed(id: StringName) -> void:
    if not completed_levels.has(id): completed_levels.append(id)

func is_completed(id: StringName) -> bool:
    return completed_levels.has(id)

func is_unlocked(_id: StringName, predecessor: StringName = &"") -> bool:
    return predecessor == &"" or is_completed(predecessor)

func retry_level() -> void:
    player_hp = player_max_hp

func reset_run() -> void:
    completed_levels.clear()
    current_level = &""
    facts.clear()
    player_hp = player_max_hp

func set_music_muted(value: bool) -> void:
    music_muted = value
    save_settings()

func set_sfx_muted(value: bool) -> void:
    sfx_muted = value
    save_settings()

func save_settings() -> Error:
    var cfg := ConfigFile.new()
    for key in ["music_volume_db", "sfx_volume_db", "music_muted", "sfx_muted"]:
        cfg.set_value("audio", key, get(key))
    return _atomic_save(cfg, SAVE_PATH)

func load_settings() -> void:
    var cfg := ConfigFile.new()
    if cfg.load(SAVE_PATH) != OK: return
    music_volume_db = clampf(float(cfg.get_value("audio", "music_volume_db", -9.0)), -80, 6)
    sfx_volume_db = clampf(float(cfg.get_value("audio", "sfx_volume_db", 0.0)), -80, 6)
    music_muted = bool(cfg.get_value("audio", "music_muted", false))
    sfx_muted = bool(cfg.get_value("audio", "sfx_muted", false))

func save_progress(path: String = PROGRESS_PATH) -> Error:
    var cfg := ConfigFile.new()
    cfg.set_value("campaign", "schema", 1)
    cfg.set_value("campaign", "current", String(current_level))
    cfg.set_value("campaign", "completed", completed_levels)
    cfg.set_value("campaign", "facts", facts)
    return _atomic_save(cfg, path)

func load_progress(path: String = PROGRESS_PATH) -> Error:
    var cfg := ConfigFile.new()
    var err := cfg.load(path)
    if err != OK: return err
    if int(cfg.get_value("campaign", "schema", 0)) != 1: return ERR_INVALID_DATA
    var saved: Variant = cfg.get_value("campaign", "completed", [])
    var saved_facts: Variant = cfg.get_value("campaign", "facts", {})
    if not saved is Array or not saved_facts is Dictionary: return ERR_INVALID_DATA
    var validated: Array[StringName] = []
    for id in saved:
        if not id is String and not id is StringName: return ERR_INVALID_DATA
        if not validated.has(StringName(id)): validated.append(StringName(id))
    completed_levels = validated
    facts = saved_facts.duplicate(true)
    current_level = StringName(cfg.get_value("campaign", "current", ""))
    return OK

func _atomic_save(cfg: ConfigFile, path: String) -> Error:
    var absolute := ProjectSettings.globalize_path(path)
    var err := DirAccess.make_dir_recursive_absolute(absolute.get_base_dir())
    if err != OK: return err
    err = cfg.save(path + ".tmp")
    if err != OK: return err
    return DirAccess.rename_absolute(absolute + ".tmp", absolute)
