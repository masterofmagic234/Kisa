extends "res://addons/arkon_runtime/topdown/world.gd"
## Small map exercising the real Level 3 systems; not a book chapter.
const PlayerScene = preload("res://addons/arkon_runtime/topdown/scenes/player.tscn")
const EnemyScene = preload("res://addons/arkon_runtime/topdown/scenes/enemy.tscn")
const DoorScene = preload("res://addons/arkon_runtime/topdown/scenes/door.tscn")
const PickupScene = preload("res://addons/arkon_runtime/topdown/scenes/pickup.tscn")
const Style = preload("res://addons/arkon_runtime/presentation/presentation_style163.gd")
var _hud: Label
var _ready_for_play := false
var _armed := false
var _manual_inputs := false

func _ready() -> void:
    _build_map()
    player = PlayerScene.instantiate()
    player.name = "Player"
    player.position = Vector2(210, 260)
    add_child(player)
    var camera := player.get_node("Camera2D") as Camera2D
    camera.enabled = false
    var overview := Camera2D.new()
    overview.position = Vector2(450, 240)
    overview.zoom = Vector2(1.22, 1.22)
    add_child(overview)
    var enemies := Node2D.new()
    enemies.name = "Enemies"
    add_child(enemies)
    for at in [Vector2(620, 165), Vector2(700, 340)]:
        var enemy := EnemyScene.instantiate() as ArkonLevel3Enemy
        enemy.position = at
        enemies.add_child(enemy)
        enemy.set_physics_process(false)
        _enemies.append(enemy)
    var door := DoorScene.instantiate() as ArkonLevel3Door
    door.position = Vector2(440, 236)
    door.rotation = PI / 2
    add_child(door)
    door.setup(Vector2(440, 236), false, PI / 2)
    _doors.append(door)
    pickups_root = Node2D.new()
    pickups_root.name = "Pickups"
    add_child(pickups_root)
    for description in [[&"medkit", Vector2(180,360)], [&"shotgun", Vector2(280,360)]]:
        var pickup := PickupScene.instantiate() as ArkonLevel3Pickup
        pickup.kind = description[0]
        pickup.position = description[1]
        pickups_root.add_child(pickup)
    initialize_combat()
    player.action_requested.connect(_interact)
    _bind_inputs()
    _build_hud()
    PauseManager.pause_state_changed.connect(func(_paused: bool): player.clear_controls())
    GraphicsSettings.enter_level(&"interior", self)
    call_deferred("_build_navigation")

func _build_navigation() -> void:
    await get_tree().physics_frame
    await get_tree().physics_frame
    await navigation.build_sliced(self, Vector2(900, 520), _doors, 8)
    _ready_for_play = true

func is_ready_for_gameplay() -> bool:
    return _ready_for_play and is_instance_valid(player) and is_instance_valid(_hud)

func _build_map() -> void:
    _tile(Rect2(0, 0, 900, 520), Color("273832"))
    _tile(Rect2(18, 18, 412, 484), Color("3f5147"))
    _tile(Rect2(450, 18, 432, 484), Color("554c3b"))
    for rect in [Rect2(0,0,900,12), Rect2(0,508,900,12), Rect2(0,0,12,520), Rect2(888,0,12,520), Rect2(434,0,12,220), Rect2(434,252,12,268), Rect2(570,270,65,24), Rect2(165,110,100,32)]:
        _tile(rect, Color("b5a57f"))
        var wall := StaticBody2D.new()
        wall.collision_layer = 1
        wall.collision_mask = 0
        wall.position = rect.get_center()
        var collider := CollisionShape2D.new()
        collider.shape = RectangleShape2D.new()
        collider.shape.size = rect.size
        wall.add_child(collider)
        add_child(wall)

func _tile(rect: Rect2, color: Color) -> void:
    var tile := Polygon2D.new()
    tile.polygon = PackedVector2Array([rect.position, Vector2(rect.end.x,rect.position.y), rect.end, Vector2(rect.position.x,rect.end.y)])
    tile.color = color
    tile.z_index = -10
    add_child(tile)

func _bind_inputs() -> void:
    for pair in [["td_left",KEY_A], ["td_right",KEY_D], ["td_up",KEY_W], ["td_down",KEY_S], ["td_sprint",KEY_SHIFT], ["td_action",KEY_E], ["td_throw",KEY_Q], ["td_switch",KEY_TAB]]:
        if not InputMap.has_action(pair[0]):
            InputMap.add_action(pair[0])
            var key := InputEventKey.new()
            key.physical_keycode = pair[1]
            InputMap.action_add_event(pair[0], key)

func _physics_process(_delta: float) -> void:
    if _manual_inputs or not is_ready_for_gameplay() or SceneFlow.is_loading_gameplay(): return
    var aim := player.global_position.direction_to(get_global_mouse_position())
    player.set_input(Input.get_vector("td_left","td_right","td_up","td_down"), aim,
        Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT) and get_viewport().gui_get_hovered_control() == null, Input.is_action_just_pressed("td_action"),
        Input.is_action_just_pressed("td_throw"), Input.is_action_pressed("td_sprint"))
    if Input.is_action_just_pressed("td_switch"): player.switch_next_weapon()

func _interact() -> void:
    if is_combat_paused(): return
    var enemy := _find_nearest_stunned_enemy()
    if enemy != null:
        enemy.kill()
        return
    var pickup := _find_nearest_pickup()
    if pickup != null:
        pickup.collect(player)
        return
    var door := _find_nearest_closed_door()
    if door != null: door.interact(player.global_position)

func _build_hud() -> void:
    var layer := CanvasLayer.new()
    layer.process_mode = Node.PROCESS_MODE_ALWAYS
    add_child(layer)
    var panel := PanelContainer.new()
    panel.position = Vector2(18,18)
    panel.add_theme_stylebox_override("panel", Style.style(Color("16211d"), Style.BRASS))
    layer.add_child(panel)
    var list := VBoxContainer.new()
    panel.add_child(list)
    var title := Label.new()
    title.text = "LEVEL 3 / 2D СТЕНД · ГРАФИЧЕСКИЕ ЗАГЛУШКИ"
    Style.label(title, 17, Style.BRASS)
    list.add_child(title)
    _hud = Label.new()
    Style.label(_hud, 15)
    list.add_child(_hud)
    var row := HBoxContainer.new()
    list.add_child(row)
    _button(row, "Включить / остановить AI", func():
        _armed = not _armed
        for enemy in _enemies: enemy.set_physics_process(_armed))
    _button(row, "Дробовик", func(): player.equip_weapon(&"shotgun", 8))
    _button(row, "Бита", func(): player.equip_weapon(&"bat"))
    var second := HBoxContainer.new()
    list.add_child(second)
    _button(second, "Заново", func(): PauseManager.resume(); SceneFlow.go_to(&"lab"))
    _button(second, "К плану", func(): PauseManager.resume(); SceneFlow.go_to(&"planning"))

func _button(parent: Node, text_: String, action: Callable) -> void:
    var button := Button.new()
    button.text = text_
    Style.button(button)
    button.pressed.connect(action)
    parent.add_child(button)

func _process(_delta: float) -> void:
    if not is_instance_valid(_hud): return
    _hud.text = "WASD · мышь / ЛКМ · E дверь / добивание · Q бросок · Tab оружие\nShift бег · Esc пауза · здоровье %d · AI %s%s" % [player.health, "активен" if _armed else "ждёт", " · ПАУЗА" if PauseManager.is_paused else ""]

func _exit_tree() -> void:
    super._exit_tree()
    GraphicsSettings.leave_level(self)
