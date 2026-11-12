extends SceneTree
const Health = preload("res://addons/arkon_runtime/combat/health_component.gd")
const Weapon = preload("res://addons/arkon_runtime/combat/weapon_component.gd")
const Data = preload("res://addons/arkon_runtime/combat/weapon_data.gd")
const Gate = preload("res://addons/arkon_runtime/core/startup_gate.gd")
const Cues = preload("res://addons/arkon_runtime/presentation/cinematic_cues.gd")
const Dialogue = preload("res://addons/arkon_runtime/narrative/dialogue_runner.gd")
const Panels = preload("res://addons/arkon_runtime/narrative/illustrated_sequence.gd")
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
    await _panels()
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

func _panels() -> void:
    var reader := Panels.new()
    root.add_child(reader)
    var completions: Array = []
    reader.finished.connect(func(id: StringName, facts: Dictionary, skipped: bool): completions.append([id, facts, skipped]))
    check(reader.play(&"one", [{"text":"a","minimum_hold":10.0},{"text":"b"}], {"pact_known":true})==OK,"Illustrated sequence refused valid panels")
    check(not reader.advance(),"Reading hold was bypassed by a repeated advance")
    check(reader.play(&"bad", [{}])==ERR_INVALID_DATA and reader.sequence_id==&"one","Invalid panels destroyed the active story")
    var held: float = reader.get("_held")
    root.get_node("PauseManager").set_paused(true)
    for i in range(4): await process_frame
    check(is_equal_approx(reader.get("_held"),held),"Paused panel consumed reading time")
    root.get_node("PauseManager").resume()
    reader.skip()
    reader.skip()
    reader.advance()
    check(completions.size()==1 and completions[0][1].pact_known and completions[0][2],"Skip lost facts or emitted completion twice")
    check(root.get_node("GameState").facts.is_empty(),"Reader wrote story progress without its scene owner")
    reader.play(&"two", [{"text":"a"},{"text":"b"}], {"pact_known":true})
    reader.advance()
    check(reader.active and reader.index==1,"Reader skipped the second panel")
    reader.advance()
    check(completions.size()==2 and completions[1][1]==completions[0][1] and not completions[1][2],"Normal and skipped completion disagree on story facts")
    reader.queue_free()

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
    await _topdown(current_scene)
    flow.go_to(&"illustrations")
    for i in range(240):
        await process_frame
        if not flow.is_loading_gameplay():break
    check(current_scene!=null and current_scene.name=="IllustrationLab","Illustrated scene route failed")
    if current_scene!=null and current_scene.name=="IllustrationLab":
        check(current_scene.sequence.active and current_scene.sequence.index==0,"Storyboard view did not start its first panel")
        current_scene.sequence.advance()
        check(current_scene.sequence.index==1,"View did not receive the next panel")
    flow.go_to(&"planning")
    for i in range(240):
        await process_frame
        if not flow.is_loading_gameplay():break
    check(current_scene!=null and current_scene.name=="AdaptationPlanningRoom","Return transition failed")

func _topdown(world: Node2D) -> void:
    world.set("_manual_inputs",true)
    var actor = world.player
    check(actor is CharacterBody2D and world.navigation.ready,"Level 3 player/navigation were replaced or not ready")
    check(actor.move_speed==120.0 and actor.acceleration==900.0 and actor.friction==1100.0,"Release movement tuning drifted")
    check(not world.has_line_of_sight(Vector2(210,260),Vector2(620,165)),"Enemy can see through a solid wall")
    check(world.is_body_path_clear(Vector2(210,260),Vector2(360,260)) and not world.is_body_path_clear(Vector2(410,260),Vector2(470,260)),"Body sweep ignores wall clearance")
    check(not world.navigation.path(Vector2(210,260),Vector2(620,260)).is_empty(),"Closed-door route cannot be planned")
    var start: Vector2 = actor.global_position
    actor.set_input(Vector2.RIGHT,Vector2.RIGHT,false,false,false,false)
    for i in range(20):await physics_frame
    check(actor.global_position.x>start.x+25 and actor.global_position.x<start.x+50,"Transferred acceleration/move_and_slide did not move the player")
    actor.clear_controls()
    var edges := [0,0]
    actor.action_requested.connect(func():edges[0]+=1)
    actor.throw_requested.connect(func(_at: Vector2,_aim: Vector2):edges[1]+=1)
    actor.set_input(Vector2.ZERO,Vector2.RIGHT,false,true,true,false)
    for i in range(3):await physics_frame
    check(edges==[1,1] and actor.throwable==&"","Action/throw edge repeated across physics ticks")
    var enemy = world._enemies[0]
    actor.global_position=Vector2(210,260)
    enemy.global_position=Vector2(340,260)
    for i in range(3):await physics_frame
    actor.set_input(Vector2.ZERO,Vector2.RIGHT,true,false,false,false)
    for i in range(2):await physics_frame
    actor.set_input(Vector2.ZERO,Vector2.RIGHT,false,false,false,false)
    for i in range(25):await physics_frame
    check(enemy.state==enemy.State.DEAD,"Swept projectile did not damage the transferred enemy/hitbox")
    check(actor.health==100,"Player's projectile hit its own shooter")
    var second = world._enemies[1]
    second.global_position=Vector2(620,260)
    for i in range(3):await physics_frame
    world._trace_weapon_shot(Vector2(210,260),Vector2.RIGHT,actor)
    for i in range(45):await physics_frame
    check(second.state!=second.State.DEAD,"Shot crossed a wall and killed a hidden enemy")
    check(world._projectile_pool.active_count()==0,"Finished projectiles were not recycled")
    var door = world._doors[0]
    check(not world.has_line_of_sight(Vector2(410,236),Vector2(470,236)),"Closed door did not occlude the room")
    door.locked=true
    check(not door.interact(Vector2(410,236),true),"Locked door accepted a slam")
    door.locked=false
    check(door.interact(Vector2(410,236),true),"Unlocked release door rejected a slam")
    door._on_hit_area_body_entered(second)
    check(second.is_stunned(),"Door slam failed to stun an enemy")
    for i in range(12):await physics_frame
    check(door.is_open and world.has_line_of_sight(Vector2(410,236),Vector2(470,236)),"Opened door kept its closed collider")
    var medkit = world.pickups_root.get_child(0)
    actor.global_position = medkit.global_position
    actor.clear_controls()
    for i in range(12):await physics_frame
    check(is_instance_valid(medkit) and not medkit.consumed,"Full-health player consumed a health pickup")
    actor.take_damage(40)
    for i in range(20):await physics_frame
    check(actor.health==95 and not is_instance_valid(medkit),"Standing wounded player did not collect exactly one +35 medkit")
    second.global_position=Vector2(720,350)
    actor.global_position=Vector2(620,165)
    second.stun(0.01)
    second.set_physics_process(true)
    for i in range(20):await physics_frame
    check(second.state==second.State.ALERT and second.last_known_position.distance_to(actor.global_position)<1,"Release AI did not reacquire a visible player after stun")
    second.set_physics_process(false)
    actor.clear_controls()
