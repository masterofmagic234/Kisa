extends RefCounted

static func load_config(path: String) -> Dictionary:
    var out := {"schema":1, "mode":"auto", "hardware_fingerprint":"", "adaptive_steps":{}}
    var cfg := ConfigFile.new()
    if cfg.load(path) != OK: return out
    var schema: Variant = cfg.get_value("graphics", "schema", 0)
    if not schema is int or schema != 1: return out
    var mode := str(cfg.get_value("graphics", "mode", "auto"))
    if mode in ["auto", "low", "balanced", "high"]: out.mode = mode
    out.hardware_fingerprint = str(cfg.get_value("graphics", "hardware_fingerprint", ""))
    var steps: Variant = cfg.get_value("graphics", "adaptive_steps", {})
    if steps is Dictionary:
        for level in ["foot", "vehicle", "interior"]:
            var value: Variant = steps.get(level, 0)
            if value is int: out.adaptive_steps[level] = clampi(value, 0, 2)
    return out

static func save_config(path: String, data: Dictionary) -> Error:
    var cfg := ConfigFile.new()
    for key in ["schema", "mode", "hardware_fingerprint", "adaptive_steps"]:
        cfg.set_value("graphics", key, data.get(key, {"schema":1,"mode":"auto","hardware_fingerprint":"","adaptive_steps":{}}[key]))
    var absolute := ProjectSettings.globalize_path(path)
    var err := DirAccess.make_dir_recursive_absolute(absolute.get_base_dir())
    if err != OK: return err
    err = cfg.save(path + ".tmp")
    if err != OK: return err
    return DirAccess.rename_absolute(ProjectSettings.globalize_path(path + ".tmp"), absolute)
