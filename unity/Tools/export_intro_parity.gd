extends SceneTree
## Capture the source compositor's commands without invoking a GPU.
## Only the final drawing/texture-loading calls are instrumented in memory;
## tracks, pose selection, traffic placement, clipping and mapping stay GDScript.
## godot --headless --path white_guard --script /absolute/export_intro_parity.gd -- /absolute/output.json

func _initialize() -> void:
    call_deferred("_run")

func _run() -> void:
    var source_path := "res://scripts/intro_stage.gd"
    var plan_path := "res://data/intro_shots.json"
    var original := FileAccess.get_file_as_string(source_path)
    var source := original
    source = source.replace('const BODY_FONT := preload("res://assets/reused/fonts/Manrope-Variable.ttf")', '')
    source = source.replace('var current_time := 0.0', 'var current_time := 0.0\nvar capture_draws: Array = []\nvar capture_mirror := false')
    source = source.replace('textures[path] = load(path)', 'textures[path] = path')
    source = source.replace('func _draw() -> void:', 'func capture_current() -> void:\n    capture_draws.clear()\n    capture_mirror = false')
    source = source.replace('    draw_rect(Rect2(Vector2.ZERO, size), MATTE)\n', '')
    var label_start := source.find('    var label := String(current_shot.get("label", ""))')
    var panel_start := source.find('\nfunc _draw_panel', label_start)
    assert(label_start >= 0 and panel_start > label_start, "Source label block changed; review instrumentation.")
    source = source.substr(0, label_start) + source.substr(panel_start)
    source = source.replace('draw_set_transform(Vector2(2 * target.get_center().x, 0), 0, Vector2(-1, 1))', 'capture_mirror = true')
    source = source.replace('draw_set_transform(Vector2.ZERO)', 'capture_mirror = false')
    var terminal := 'draw_texture_rect_region(textures[data[0]], mapped, source, tint, false, true)'
    assert(source.count(terminal) == 1, "Source terminal draw changed; review instrumentation.")
    source = source.replace(terminal, 'capture_draws.append({"texture":data[0],"source":[source.position.x,source.position.y,source.size.x,source.size.y],"destination":[mapped.position.x,mapped.position.y,mapped.size.x,mapped.size.y],"tint":[tint.r,tint.g,tint.b,tint.a],"mirror":capture_mirror,"mirror_center":target.get_center().x})')
    var script := GDScript.new()
    script.source_code = source
    var result := script.reload()
    assert(result == OK, "Instrumented source failed to compile.")
    var stage: Control = script.new()
    stage.size = Vector2(768, 432)
    var plan: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(plan_path))
    assert(int(plan.schema) == 3)
    stage.configure(plan)
    var timestamps: Array[float] = []
    for shot: Dictionary in plan.shots:
        var start := float(shot.start)
        var duration := float(shot.end) - start
        for local in [0.0, minf(0.125, duration * 0.1), duration * 0.5, maxf(0, duration - 0.001)]:
            timestamps.append(start + local)
        for panel: Dictionary in shot.panels:
            var reveal := float(panel.get("reveal", 0))
            if reveal > 0 and reveal < duration:
                timestamps.append(start + reveal - 0.001)
                timestamps.append(start + reveal)
                timestamps.append(minf(float(shot.end) - 0.001, start + reveal + 0.125))
    timestamps.sort()
    var records: Array = []
    for seconds: float in timestamps:
        stage.show_at(seconds)
        stage.capture_current()
        records.append({"time":seconds,"shot_id":stage.current_shot.id,"draws":stage.capture_draws.duplicate(true)})
    var args := OS.get_cmdline_user_args()
    assert(args.size() == 1, "Pass one output path.")
    var file := FileAccess.open(args[0], FileAccess.WRITE)
    assert(file != null, "Cannot open output path.")
    var report := {"schema":1,"engine":Engine.get_version_info().string,"canvas":[768,432],
        "source_files":{"scripts/intro_stage.gd":FileAccess.get_sha256(source_path),"data/intro_shots.json":FileAccess.get_sha256(plan_path)},
        "capture":"Original Godot compositor; only terminal draw, texture loading and labels instrumented. No GPU/Unity execution.",
        "samples":records}
    # Keep IEEE-754 timestamp precision: rounding an exact panel-reveal time
    # can change which side of the visibility boundary a replay evaluates.
    file.store_string(JSON.stringify(report, "", true, true))
    file.close()
    print("INTRO ORACLE: ", records.size(), " samples; source GDScript compositor, no GPU.")
    stage.free()
    quit()
