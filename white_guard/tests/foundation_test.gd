extends SceneTree
const Health = preload("res://addons/arkon_runtime/combat/health_component.gd")
const Weapon = preload("res://addons/arkon_runtime/combat/weapon_component.gd")
const Data = preload("res://addons/arkon_runtime/combat/weapon_data.gd")
const Gate = preload("res://addons/arkon_runtime/core/startup_gate.gd")
const Cues = preload("res://addons/arkon_runtime/presentation/cinematic_cues.gd")
const Dialogue = preload("res://addons/arkon_runtime/narrative/dialogue_runner.gd")
const Powertrain = preload("res://addons/arkon_runtime/vehicle/race_powertrain.gd")
const RoadProjection = preload("res://addons/arkon_runtime/vehicle/race_projection.gd")
var failures: Array[String] = []
var checks := 0

func _initialize() -> void:
    call_deferred("_run")

func check(value: bool, message: String) -> void:
    checks += 1
    if not value:
        failures.append(message)
        push_error(message)

func _run() -> void:
    await process_frame
    _health()
    _weapon()
    _clock()
    _narrative()
    _vehicle()
    _save()
    _load_scripts("res://addons/arkon_runtime")
    await _scenes()
    var report := {"checks":checks,"failures":failures,"godot":Engine.get_version_info().string,"performance_measurement":false}
    DirAccess.make_dir_recursive_absolute("res://test-results")
    var file := FileAccess.open("res://test-results/foundation.json",FileAccess.WRITE)
    file.store_string(JSON.stringify(report,"  "))
    if failures.is_empty(): print("FOUNDATION PASS: ",checks," checks")
    else: print("FOUNDATION FAIL: ",failures)
    # Let queued audio/scene resources drain before quitting.
    var audio := root.get_node("AudioManager")
    audio.shutdown()
    await process_frame
    await process_frame
    quit(0 if failures.is_empty() else 1)

func _health() -> void:
    var actor := Node.new()
    root.add_child(actor)
    var health := Health.new()
    actor.add_child(health)
    check(health.apply_damage(30),"First hit was rejected")
    check(health.current_health==70,"Damage did not reach health owner")
    check(not health.apply_damage(30),"Invulnerability admitted a duplicate same-frame hit")
    health.invulnerability_timer=0
    check(health.apply_damage(100),"Lethal hit rejected")
    check(health.is_dead and not health.force_kill(),"Death was not idempotent")
    health.heal(100)
    check(health.current_health==0,"Healing resurrected a dead entity")
    actor.queue_free()

func _weapon() -> void:
    var weapon := Weapon.new()
    var a := Data.new()
    var b := Data.new()
    a.weapon_id=&"a"
    b.weapon_id=&"b"
    weapon.configure({&"a":a,&"b":b})
    check(weapon.equip(&"a",5),"Catalog injection failed")
    check(weapon.consume_shot() and weapon.ammo==4,"Shot consumption failed")
    check(not weapon.consume_shot(),"Cooldown allowed two immediate shots")
    weapon.equip(&"b",2)
    weapon.consume_shot()
    weapon.equip(&"a")
    check(weapon.ammo==4,"Switching weapons converted another reserve into this ammunition")
    check(not weapon.equip(&"missing"),"Unknown weapon was accepted")
    weapon.free()

func _clock() -> void:
    var gate := Gate.new()
    gate.reset(0)
    gate.observe(90000000,false,1)
    check(gate.elapsed_usec==0,"Background interval consumed startup budget")
    gate.observe(180000000,true,1)
    check(gate.elapsed_usec==250000,"One long blocked operation became many visible blank frames")
    for i in range(5): gate.observe(180250000+i*250000,true,1)
    check(gate.elapsed_usec>1000000,"Foreground timeout never advances")

func _narrative() -> void:
    var cues := Cues.new()
    check(cues.configure([{"start":0.0,"end":1.0,"text":"one"},{"start":1.0,"end":2.0,"text":"two"}])==OK,"Cue track rejected valid decoder timeline")
    check(cues.at(1.0).text=="two" and cues.at(2.0).is_empty(),"Cue boundaries drifted across a seek")
    check(cues.configure([{"start":5,"end":4}])==ERR_INVALID_DATA,"Invalid cue interval was accepted")
    var dialogue := Dialogue.new()
    var observed: Array[String] = []
    dialogue.line_changed.connect(func(_speaker: String,text: String):observed.append(text))
    check(dialogue.start(&"test",[{"speaker":"one","text":"a"},{"speaker":"two","text":"b"}])==OK,"Dialogue start failed")
    dialogue.advance()
    dialogue.advance()
    dialogue.advance()
    check(observed==["a","b"] and not dialogue.active,"Dialogue duplicated a completed line")
    check(dialogue.start(&"invalid",[{"text":"missing speaker"}])==ERR_INVALID_DATA,"Invalid dialogue replaced the validated sequence")
    dialogue.free()

func _vehicle() -> void:
    var engine := Powertrain.new()
    var speed := 0.0
    for i in range(480): speed=engine.step(speed,1.0,0.0,1.0/60.0,1000.0)
    check(speed>0 and engine.gear>=1 and engine.gear<=4,"Transferred drivetrain cannot accelerate/shift")
    var before := speed
    for i in range(120):speed=engine.step(speed,0,1,1.0/60.0,1000.0)
    check(speed<before and speed>=0,"Braking created reverse motion or failed to slow down")
    var polygon := PackedVector2Array([Vector2(-4,-4),Vector2(4,-4),Vector2(4,4),Vector2(-4,4)])
    var clipped := RoadProjection.clip_screen(polygon,Rect2(-1,-1,2,2))
    check(clipped.size()>=4,"Road projection lost a crossing screen polygon")
    for point in clipped:check(absf(point.x)<=1.001 and absf(point.y)<=1.001,"Projection escaped the screen clip")

func _save() -> void:
    var state := root.get_node("GameState")
    state.reset_run()
    state.mark_completed(&"test_a")
    state.current_level=&"test_b"
    state.facts={"civilian_safe":true}
    var path := "user://foundation-test.cfg"
    check(state.save_progress(path)==OK,"Atomic progress write failed")
    state.reset_run()
    check(state.load_progress(path)==OK,"Progress read failed")
    check(state.current_level==&"test_b" and state.is_completed(&"test_a") and state.facts.civilian_safe,"Progress/mission facts did not survive the round trip")
    var bad := ConfigFile.new()
    bad.set_value("campaign","schema",99)
    bad.save(path)
    check(state.load_progress(path)==ERR_INVALID_DATA and state.is_completed(&"test_a"),"Bad save schema partially overwrote live progress")
    DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
    state.reset_run()

func _load_scripts(path: String) -> void:
    var dir := DirAccess.open(path)
    for entry in dir.get_directories():_load_scripts(path.path_join(entry))
    for entry in dir.get_files():
        if entry.ends_with(".gd"):
            var script := load(path.path_join(entry)) as GDScript
            check(script!=null and script.can_instantiate(),"Portable module cannot compile: "+entry)

func _scenes() -> void:
    var flow := root.get_node("SceneFlow")
    var plan := load("res://scenes/planning_room.tscn") as PackedScene
    var planning := plan.instantiate()
    root.add_child(planning)
    current_scene=planning
    await process_frame
    check(planning.get("_campaign").missions.size()==9,"Review workspace lost campaign data")
    flow.go_to(&"missing")
    check(not flow.is_loading_gameplay() and is_instance_valid(planning),"Missing destination stranded or destroyed the current scene")
    flow.go_to(&"lab")
    for i in range(240):
        await process_frame
        if not flow.is_loading_gameplay(): break
    check(current_scene!=null and current_scene.name=="RuntimeLab" and not flow.is_loading_gameplay(),"Actual asynchronous planning-to-lab transition failed")
    if current_scene==null or current_scene.name!="RuntimeLab":return
    for i in range(4): await physics_frame
    var actor = current_scene.get("actor")
    var target := current_scene.get_node("FixtureTarget2") if current_scene.has_node("FixtureTarget2") else null
    for child in current_scene.get_children():
        if child.name.to_lower().contains("fixturetarget") and absf(child.position.x)<0.1:target=child
    check(target!=null,"Combat fixture target is missing")
    if target!=null:
        actor.camera.look_at(target.global_position)
        check(actor.fire(),"FPS adapter refused its injected weapon")
        check(target.get_node("Health").current_health==75,"Physical ray did not reach transferred hitbox/health components")
    flow.go_to(&"planning")
    for i in range(240):
        await process_frame
        if not flow.is_loading_gameplay():break
    check(current_scene!=null and current_scene.name=="AdaptationPlanningRoom","Return transition failed")
