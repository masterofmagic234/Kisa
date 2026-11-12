extends RefCounted
## Typography and quiet metallic rules shared by the title, credits and result.
const DISPLAY=preload("res://assets/reused/fonts/Forum-Regular.ttf")
const BODY=preload("res://assets/reused/fonts/Manrope-Variable.ttf")
const INK=Color("0b1420")
const IVORY=Color("eee6d7")
const BRASS=Color("c6a56d")
const SAGE=Color("a5bcb4")
const WEIGHT_AXIS163=0x77676874 # OpenType wght; string names use Godot aliases.
static var _regular163:FontVariation
static var _strong163:FontVariation
static func body_font(strong:=false)->Font:
    if strong:
        if _strong163==null:
            _strong163=FontVariation.new();_strong163.base_font=BODY
            _strong163.variation_opentype={WEIGHT_AXIS163:600.0}
        return _strong163
    if _regular163==null:
        _regular163=FontVariation.new();_regular163.base_font=BODY
        _regular163.variation_opentype={WEIGHT_AXIS163:450.0}
    return _regular163
static func style(fill:Color,line:Color,padding:=Vector4(20,10,20,10),width:=1)->StyleBoxFlat:
    var box=StyleBoxFlat.new();box.bg_color=fill;box.border_color=line
    box.set_border_width_all(width);box.set_corner_radius_all(3)
    box.content_margin_left=padding.x;box.content_margin_top=padding.y
    box.content_margin_right=padding.z;box.content_margin_bottom=padding.w
    return box
static func button(target:Button,primary:=false)->void:
    target.add_theme_font_override("font",body_font(true))
    target.alignment=HORIZONTAL_ALIGNMENT_LEFT
    target.add_theme_color_override("font_color",INK if primary else IVORY)
    target.add_theme_color_override("font_hover_color",INK if primary else Color.WHITE)
    target.add_theme_color_override("font_pressed_color",INK if primary else BRASS)
    target.add_theme_color_override("font_focus_color",INK if primary else IVORY)
    target.add_theme_stylebox_override("normal",style(IVORY if primary else Color(.04,.07,.10,.36),Color.TRANSPARENT if primary else Color(.77,.65,.43,.35)))
    target.add_theme_stylebox_override("hover",style(Color("e3c99a") if primary else Color(.08,.13,.16,.75),BRASS))
    target.add_theme_stylebox_override("pressed",style(Color("b99b6a") if primary else Color(.06,.10,.13,.92),BRASS))
    target.add_theme_stylebox_override("focus",style(Color.TRANSPARENT,Color("ecce98"),Vector4(20,10,20,10),2))
static func label(target:Label,size:int,colour:=IVORY,display:=false)->void:
    target.add_theme_font_override("font",DISPLAY if display else body_font())
    target.add_theme_font_size_override("font_size",size)
    target.add_theme_color_override("font_color",colour)
    target.add_theme_color_override("font_shadow_color",Color(0,0,0,.7))
    target.add_theme_constant_override("shadow_offset_y",1)
    target.mouse_filter=Control.MOUSE_FILTER_IGNORE
