extends Area2D
class_name ArkonLevel3Pickup
const Visual = preload("res://addons/arkon_runtime/topdown/fixtures/asset_visual.gd")
const CONE_PISTOL = preload("res://assets/fixtures/projectile.svg")
const CONE_SHOTGUN = preload("res://assets/fixtures/projectile.svg")
@export var kind: StringName = &""
var consumed := false
var _retry_time := 0.0
var _clock := 0.0
var _glow: Sprite2D
@onready var _visual: Sprite2D = %Visual
func is_automatic() -> bool: return kind==&"medkit"
func _ready() -> void:
    if kind==&"ammo":kind=&"medkit"
    collision_layer=0
    collision_mask=2
    monitoring=true
    body_entered.connect(_on_body_entered)
    _visual.texture_filter=CanvasItem.TEXTURE_FILTER_NEAREST
    _visual.position=Vector2.ZERO
    _visual.scale=Vector2.ONE*0.88
    match kind:
        &"pistol": _visual.texture=CONE_PISTOL
        &"shotgun": _visual.texture=CONE_SHOTGUN
        &"bat": _visual.texture=Visual.frame_texture("res://assets/fixtures/projectile.svg",1)
        &"bottle": _visual.texture=Visual.frame_texture("res://assets/fixtures/projectile.svg")
        &"medkit":
            _visual.visible=false
            _make_medkit_glow()
    queue_redraw()
func _on_body_entered(body: Node) -> void:
    if body is ArkonLevel3Player and is_automatic(): collect(body)
func collect(player: ArkonLevel3Player) -> bool:
    if consumed or player.is_dead or not player.combat_enabled: return false
    var world := get_parent().get_parent()
    if not world.has_line_of_sight(player.global_position,global_position): return false
    if kind==&"medkit" and player.health>=player.max_health: return false
    consumed=true
    var bus := get_node_or_null("/root/SignalBus")
    if bus!=null: bus.item_collected.emit(kind,StringName(name),1,player)
    queue_free()
    return true
func _draw() -> void:
    draw_circle(Vector2(1,2),9,Color(0.01,0.02,0.03,0.4))
    draw_arc(Vector2.ZERO,12,0,TAU,24,Color(0.55,0.89,0.81,0.67),1,true)
    if kind==&"medkit":
        # A readable mint case and pulse remain bright under the night tint.
        draw_arc(Vector2.ZERO,14,0,TAU,32,Color(.55,1,.82,.44+sin(_clock*2.6)*.16),1.3,true)
        draw_rect(Rect2(-8,-7,16,14),Color("192f32"))
        draw_rect(Rect2(-7,-6,14,12),Color("c5eee0"))
        draw_rect(Rect2(-3,-9,6,2),Color("718e86"))
        draw_rect(Rect2(-2,-4,4,8),Color("277d69"))
        draw_rect(Rect2(-4,-2,8,4),Color("277d69"))

func _make_medkit_glow() -> void:
    var unshaded:=CanvasItemMaterial.new()
    unshaded.light_mode=CanvasItemMaterial.LIGHT_MODE_UNSHADED
    material=unshaded
    var gradient:=Gradient.new()
    gradient.offsets=PackedFloat32Array([0,.3,.7,1])
    gradient.colors=PackedColorArray([Color(1,1,1,.62),Color(1,1,1,.28),Color(1,1,1,.06),Color(1,1,1,0)])
    var texture:=GradientTexture2D.new();texture.gradient=gradient
    texture.width=64;texture.height=64;texture.fill=GradientTexture2D.FILL_RADIAL
    texture.fill_from=Vector2(.5,.5);texture.fill_to=Vector2(1,.5)
    _glow=Sprite2D.new();_glow.name="HealthPickupGlow";_glow.texture=texture
    _glow.scale=Vector2.ONE*.65;_glow.modulate=Color(.3,1,.75,.64);_glow.z_index=-1
    var additive:=CanvasItemMaterial.new();additive.blend_mode=CanvasItemMaterial.BLEND_MODE_ADD
    additive.light_mode=CanvasItemMaterial.LIGHT_MODE_UNSHADED;_glow.material=additive
    add_child(_glow)

func _process(delta: float) -> void:
    if kind!=&"medkit" or not is_visible_in_tree():return
    _clock+=delta
    if is_instance_valid(_glow):_glow.scale=Vector2.ONE*(.65+sin(_clock*2.6)*.055)
    queue_redraw()

func _physics_process(delta: float) -> void:
    # A full-health player can remain on a medkit and be wounded later. The
    # body_entered edge alone would leave a usable medkit inert underfoot.
    if not is_automatic() or consumed: return
    _retry_time-=delta
    if _retry_time>0: return
    _retry_time=.12
    for body in get_overlapping_bodies():
        if body is ArkonLevel3Player and collect(body): return
