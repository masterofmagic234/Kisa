extends Node3D
## Small engineering fixture; no chapter or combat encounter is claimed complete.
const Actor = preload("res://addons/arkon_runtime/locomotion/first_person_actor.gd")
const Health = preload("res://addons/arkon_runtime/combat/health_component.gd")
const Weapon = preload("res://addons/arkon_runtime/combat/weapon_component.gd")
const WeaponData = preload("res://addons/arkon_runtime/combat/weapon_data.gd")
const Hitbox = preload("res://addons/arkon_runtime/combat/hitbox_3d_component.gd")
const Style = preload("res://addons/arkon_runtime/presentation/presentation_style163.gd")
const Powertrain = preload("res://addons/arkon_runtime/vehicle/race_powertrain.gd")
var actor: CharacterBody3D
var _hud: Label
var _engine = Powertrain.new()
var _speed := 0.0
var _elapsed := 0.0

func _ready() -> void:
    var environment := WorldEnvironment.new()
    environment.environment = Environment.new()
    environment.environment.background_mode = Environment.BG_COLOR
    environment.environment.background_color = Color("313c34")
    environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
    environment.environment.ambient_light_color = Color("b9c5af")
    environment.environment.ambient_light_energy = 0.8
    add_child(environment)
    var sun := DirectionalLight3D.new()
    sun.rotation_degrees = Vector3(-45, -25, 0)
    sun.light_color = Color("ffe4aa")
    add_child(sun)
    _box(Vector3(0,-0.25,0), Vector3(20,0.5,20), Color("716f50"), true)
    _box(Vector3(-10,1,0),Vector3(0.4,2,20),Color("817d68"),true)
    _box(Vector3(10,1,0),Vector3(0.4,2,20),Color("817d68"),true)
    for i in range(4): _box(Vector3(-3,float(i+1)*0.08,-3-float(i)*0.7), Vector3(2,float(i+1)*0.16,0.7),Color("a49a7b"),true)
    actor = Actor.new()
    actor.name = "Player"
    actor.position = Vector3(0,0.95,5)
    actor.collision_layer = 4
    actor.collision_mask = 1
    var body := CollisionShape3D.new()
    body.shape = CapsuleShape3D.new()
    body.shape.radius = 0.3
    body.shape.height = 1.8
    actor.add_child(body)
    var camera := Camera3D.new()
    camera.name = "Camera3D"
    camera.position.y = 0.65
    actor.add_child(camera)
    var health := Health.new()
    health.name = "Health"
    actor.add_child(health)
    var weapon := Weapon.new()
    weapon.name = "Weapon"
    var pistol := WeaponData.new()
    pistol.damage = 25
    pistol.fire_interval = 0.2
    weapon.configure({&"fixture":pistol})
    weapon.equip(&"fixture", 24)
    actor.add_child(weapon)
    add_child(actor)
    for i in range(3): _target(Vector3(-3+3*i,0.8,-5))
    _bind_inputs()
    PauseManager.pause_state_changed.connect(_paused)
    GraphicsSettings.enter_level(&"foot", self)
    _build_hud()

func is_ready_for_gameplay() -> bool:
    return is_instance_valid(actor) and is_instance_valid(_hud)

func _bind_inputs() -> void:
    for pair in [["move_left",KEY_A],["move_right",KEY_D],["move_forward",KEY_W],["move_back",KEY_S],["capture_pointer",KEY_F]]:
        if not InputMap.has_action(pair[0]):
            InputMap.add_action(pair[0])
            var key := InputEventKey.new()
            key.physical_keycode = pair[1]
            InputMap.action_add_event(pair[0],key)
    if not InputMap.has_action("fire"):
        InputMap.add_action("fire")
        var mouse := InputEventMouseButton.new()
        mouse.button_index = MOUSE_BUTTON_LEFT
        InputMap.action_add_event("fire",mouse)

func _box(position_: Vector3, size: Vector3, color: Color, solid: bool, parent: Node = self) -> MeshInstance3D:
    var mesh := MeshInstance3D.new()
    mesh.mesh = BoxMesh.new()
    mesh.mesh.size = size
    mesh.position = position_
    var material := StandardMaterial3D.new()
    material.albedo_color = color
    material.roughness = 0.85
    mesh.material_override = material
    parent.add_child(mesh)
    if solid:
        var body := StaticBody3D.new()
        var collider := CollisionShape3D.new()
        collider.shape = BoxShape3D.new()
        collider.shape.size = size
        body.add_child(collider)
        mesh.add_child(body)
    return mesh

func _target(position_: Vector3) -> void:
    var target := Node3D.new()
    target.name = "FixtureTarget_%d" % int(position_.x + 3)
    target.position = position_
    var script := load("res://scripts/fixture_target.gd") as Script
    target.set_script(script)
    var health := Health.new()
    health.name = "Health"
    target.add_child(health)
    var hitbox := Hitbox.new()
    hitbox.name = "Hitbox"
    var shape := CollisionShape3D.new()
    shape.name = "CollisionShape3D"
    shape.shape = BoxShape3D.new()
    shape.shape.size = Vector3(0.8,1.4,0.35)
    hitbox.add_child(shape)
    target.add_child(hitbox)
    _box(Vector3.ZERO, Vector3(0.8,1.4,0.35), Color("b38c56"), false, target)
    add_child(target)

func _build_hud() -> void:
    var layer := CanvasLayer.new()
    layer.process_mode = Node.PROCESS_MODE_ALWAYS
    add_child(layer)
    var panel := PanelContainer.new()
    panel.position = Vector2(20,20)
    panel.add_theme_stylebox_override("panel",Style.style(Color("16211d"),Style.BRASS))
    layer.add_child(panel)
    var list := VBoxContainer.new()
    panel.add_child(list)
    var title := Label.new()
    title.text = "СТЕНД ARKON RUNTIME / НЕ СЮЖЕТНЫЙ УРОВЕНЬ"
    Style.label(title,18,Style.BRASS)
    list.add_child(title)
    _hud = Label.new()
    Style.label(_hud,16)
    list.add_child(_hud)
    var back := Button.new()
    back.text = "Вернуться к плану"
    Style.button(back)
    back.pressed.connect(func(): PauseManager.resume(); SceneFlow.go_to(&"planning"))
    list.add_child(back)
    var crosshair := Label.new()
    crosshair.text = "+"
    crosshair.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
    crosshair.add_theme_font_size_override("font_size",24)
    layer.add_child(crosshair)

func _process(delta: float) -> void:
    _elapsed += delta
    # The drivetrain runs in a fixture; no bus model/handling is implied.
    _speed = _engine.step(_speed, 1.0 if fmod(_elapsed,12)<8 else 0.0, 1.0 if fmod(_elapsed,12)>=8 else 0.0, delta, 1000.0)
    _hud.text = "WASD — ходьба и ступеньки · F — захват мыши\nЛКМ — мишени · Esc — пауза / освобождение мыши\nПатроны: %d  |  Мотор на стенде: %.0f RPM, передача %d\nМишени проверяют hitbox и здоровье; люди и их AI ещё не созданы." % [actor.weapon.ammo,_engine.engine_rpm,_engine.gear]

func _paused(value: bool) -> void:
    if value: Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _exit_tree() -> void:
    GraphicsSettings.leave_level(self)
    Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
