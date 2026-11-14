extends SceneTree
## Execute the untouched public Godot components to capture a C# parity oracle.
## Run: godot --headless --path white_guard --script /absolute/path/to/this.gd -- /absolute/output.json
## Movement records velocity/facing decisions, not a claim about Unity collision.

const Health = preload("res://addons/arkon_runtime/combat/health_component.gd")
const Weapon = preload("res://addons/arkon_runtime/topdown/weapon_component.gd")
const PlayerScene = preload("res://addons/arkon_runtime/topdown/scenes/player.tscn")

var _health_counts := [0,0,0]
var _weapon_changes := 0

func _initialize() -> void:
    call_deferred("_run")

func _health_state(model: Node) -> Dictionary:
    return {"current":model.current_health,"maximum":model.max_health,"dead":model.is_dead,
        "invulnerability":model.invulnerability_timer,"changed":_health_counts[0],
        "damaged":_health_counts[1],"deaths":_health_counts[2]}

func _health_trace(owner: Node) -> Dictionary:
    var model := Health.new()
    model.process_mode = Node.PROCESS_MODE_DISABLED
    owner.add_child(model)
    _health_counts = [0,0,0]
    model.health_changed.connect(func(_current: int,_maximum: int):_health_counts[0]+=1)
    model.died.connect(func():_health_counts[2]+=1)
    var bus := root.get_node("SignalBus")
    var on_damage := func(entity: Node,_amount: int,_source: Node):
        if entity == owner: _health_counts[1]+=1
    bus.entity_damaged.connect(on_damage)
    var steps: Array = []
    var commands: Array = [
        {"op":"initial"},{"op":"damage","amount":0},{"op":"damage","amount":30},
        {"op":"damage","amount":30},{"op":"tick","delta":.23},
        {"op":"tick","delta":.011},{"op":"damage","amount":10},
        {"op":"heal","amount":35},{"op":"heal","amount":1000},
        {"op":"tick","delta":.24},{"op":"damage","amount":999},
        {"op":"force_kill"},{"op":"heal","amount":100},{"op":"reset","maximum":50}]
    for command: Dictionary in commands:
        var record := command.duplicate(true)
        match String(command.op):
            "damage":record["accepted"] = model.apply_damage(int(command.amount), owner)
            "tick":model._process(float(command.delta))
            "heal":model.heal(int(command.amount))
            "force_kill":record["accepted"] = model.force_kill()
            "reset":model.reset(int(command.maximum))
        record["state"] = _health_state(model)
        steps.append(record)
    bus.entity_damaged.disconnect(on_damage)
    model.queue_free()
    return {"initial":{"maximum":100,"invulnerability":.24},"steps":steps}

func _weapon_state(model: Node) -> Dictionary:
    return {"id":String(model.current_weapon),"ammo":model.ammo,"cooldown":model.cooldown,
        "reserves":model.ammo_by_weapon.duplicate(true),"changed":_weapon_changes}

func _weapon_trace(owner: Node, infinite: bool) -> Dictionary:
    var model := Weapon.new()
    model.initial_ammo = 2
    model.infinite_ammo = infinite
    model.process_mode = Node.PROCESS_MODE_DISABLED
    owner.add_child(model)
    _weapon_changes = 0
    model.weapon_changed.connect(func(_id: StringName,_ammo: int):_weapon_changes+=1)
    var steps: Array = []
    var commands: Array = [{"op":"initial"},{"op":"shot"},{"op":"shot"},
        {"op":"tick","delta":.27},{"op":"tick","delta":.011},{"op":"shot"},
        {"op":"tick","delta":1.0},{"op":"shot"},
        {"op":"equip","id":"shotgun","additional_ammo":2},{"op":"shot"},
        {"op":"equip","id":"pistol","additional_ammo":0},
        {"op":"equip","id":"pistol","additional_ammo":4},{"op":"shot"},
        {"op":"equip","id":"missing","additional_ammo":50},
        {"op":"equip","id":"shotgun","additional_ammo":-50},
        {"op":"equip","id":"bat","additional_ammo":0},{"op":"shot"},{"op":"shot"},
        {"op":"tick","delta":.38},{"op":"shot"}]
    for command: Dictionary in commands:
        var record := command.duplicate(true)
        match String(command.op):
            "shot":record["accepted"] = model.consume_shot()
            "tick":model.tick(float(command.delta))
            "equip":
                record["accepted"] = model.weapons.has(StringName(command.id))
                model.equip(StringName(command.id),int(command.additional_ammo))
        record["state"] = _weapon_state(model)
        steps.append(record)
    model.queue_free()
    return {"infinite":infinite,"initial_id":"pistol","initial_ammo":2,"steps":steps}

func _pair(value: Vector2) -> Array:
    return [value.x,value.y]

func _movement_trace(owner: Node2D) -> Dictionary:
    var actor := PlayerScene.instantiate()
    actor.name = "ParityPlayer"
    actor.position = Vector2(100,100)
    owner.add_child(actor)
    # Disabling process_mode removes CollisionObject2D from its space; disable
    # only the automatic callback so manual source ticks still use real physics.
    actor.set_physics_process(false)
    await physics_frame
    var commands: Array = []
    for i in range(20):commands.append({"delta":1.0/60,"movement":[1,0],"aim":[1,0],"sprint":false})
    for i in range(20):commands.append({"delta":1.0/60,"movement":[1,1],"aim":[0,-1],"sprint":true})
    for i in range(12):commands.append({"delta":1.0/60,"movement":[0,0],"aim":[.1,0],"sprint":false})
    for i in range(3):commands.append({"delta":1.0/60,"movement":[1,0],"aim":[1,0],"sprint":false,"controls_enabled":false})
    commands.append({"delta":.5,"movement":[1,0],"aim":[1,0],"sprint":false,"apply_flashbang":8.0})
    commands.append({"delta":1.3,"movement":[1,0],"aim":[1,0],"sprint":false})
    commands.append({"delta":1.0/60,"movement":[0,-1],"aim":[1,0],"sprint":false,"combat_enabled":false})
    var steps: Array = []
    for command: Dictionary in commands:
        await physics_frame
        actor.controls_enabled = bool(command.get("controls_enabled",true))
        actor.combat_enabled = bool(command.get("combat_enabled",true))
        if command.has("apply_flashbang"):actor.apply_flashbang(float(command.apply_flashbang))
        var movement: Array = command.movement
        var aim: Array = command.aim
        actor.set_input(Vector2(float(movement[0]),float(movement[1])),Vector2(float(aim[0]),float(aim[1])),
            false,false,false,bool(command.sprint))
        actor._physics_process(float(command.delta))
        var record := command.duplicate(true)
        record["controls_enabled"] = actor.controls_enabled
        record["combat_enabled"] = actor.combat_enabled
        record["state"] = {"velocity":_pair(actor.velocity),"aim":_pair(actor.get_aim_direction()),
            "rotation":actor.rotation,"flashbang":actor.flashbang_time}
        steps.append(record)
    var edges := {"action":0,"throw":0}
    actor.controls_enabled = true
    actor.combat_enabled = true
    actor.action_requested.connect(func():edges["action"]+=1)
    actor.throw_requested.connect(func(_origin: Vector2,_direction: Vector2):edges["throw"]+=1)
    actor.set_input(Vector2.ZERO,Vector2.RIGHT,false,true,true,false)
    for i in range(3):
        await physics_frame
        actor._physics_process(1.0/60)
    edges["throwable"] = String(actor.throwable)
    actor.queue_free()
    return {"steps":steps,"edges":edges,
        "coverage":"Original player decision path in an empty real Godot 2D space. No Unity or wall-collision coverage is claimed."}

func _run() -> void:
    await process_frame
    var owner := Node2D.new()
    root.add_child(owner)
    var report := {"schema":1,"source_files":{},"health":_health_trace(owner),
        "weapons":[_weapon_trace(owner,false),_weapon_trace(owner,true)]}
    for path in ["combat/health_component.gd","topdown/weapon_component.gd","topdown/level3_player.gd",
            "topdown/weapons/pistol.tres","topdown/weapons/shotgun.tres","topdown/weapons/bat.tres"]:
        report.source_files[path] = FileAccess.get_sha256("res://addons/arkon_runtime/"+path)
    report["movement"] = await _movement_trace(owner)
    var args := OS.get_cmdline_user_args()
    var output := args[0] if args.size()>0 else "user://unity-combat-golden.json"
    var file := FileAccess.open(output,FileAccess.WRITE)
    if file == null:
        push_error("Cannot write source parity oracle: "+output)
        root.get_node("AudioManager").shutdown()
        quit(1)
        return
    file.store_string(JSON.stringify(report,"  "))
    file.close()
    print("COMBAT SOURCE ORACLE PASS / ",output," / ",report.movement.steps.size()," movement steps")
    owner.queue_free()
    await process_frame
    root.get_node("AudioManager").shutdown()
    await process_frame
    quit()
