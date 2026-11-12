extends Control
## A storyboard reader fixture. These geometric panels are not final artwork.
const Sequence = preload("res://addons/arkon_runtime/narrative/illustrated_sequence.gd")
const Style = preload("res://addons/arkon_runtime/presentation/presentation_style163.gd")
var sequence: Node
var _caption: RichTextLabel
var _location: Label
var _progress: Label
var _art: PanelContainer
var _next: Button

func _ready() -> void:
    sequence = Sequence.new()
    add_child(sequence)
    var background := ColorRect.new()
    background.color = Color("121b18")
    background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    add_child(background)
    var margin := MarginContainer.new()
    margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    for edge in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + edge, 36)
    add_child(margin)
    var page := VBoxContainer.new()
    page.add_theme_constant_override("separation", 14)
    margin.add_child(page)
    _location = Label.new()
    Style.label(_location, 26, Style.BRASS, true)
    page.add_child(_location)
    _art = PanelContainer.new()
    _art.size_flags_vertical = Control.SIZE_EXPAND_FILL
    page.add_child(_art)
    var art_label := Label.new()
    art_label.text = "МАЗУР                         ЛЕОН\n\nРЕЗИДЕНЦИЯ / НОЧЬ\n\nМесто для статичной иллюстрации и отдельных слоёв"
    art_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    art_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    Style.label(art_label, 25, Style.IVORY, true)
    _art.add_child(art_label)
    _caption = RichTextLabel.new()
    _caption.custom_minimum_size.y = 140
    _caption.bbcode_enabled = true
    _caption.add_theme_font_override("normal_font", Style.body_font())
    _caption.add_theme_font_size_override("normal_font_size", 24)
    _caption.add_theme_color_override("default_color", Style.IVORY)
    page.add_child(_caption)
    _progress = Label.new()
    Style.label(_progress, 15, Style.SAGE)
    page.add_child(_progress)
    var row := HBoxContainer.new()
    page.add_child(row)
    _next = _button(row, "Далее / Space", func(): sequence.advance())
    _button(row, "Пропустить сцену", func(): sequence.skip())
    _button(row, "К плану", func(): PauseManager.resume(); SceneFlow.go_to(&"planning"))
    sequence.panel_changed.connect(_show_panel)
    sequence.finished.connect(func(_id: StringName, _facts: Dictionary, skipped: bool):
        _caption.text = "[b]Макет завершён.[/b]\nСюжетное сохранение не изменено."
        _progress.text = "Пропуск" if skipped else "Все панели прочитаны"
        _next.disabled = true)
    sequence.play(&"leon_pact_fixture", [
        {"heading":"01 / ДОГОВОР", "speaker":"Леон", "text":"Пока президент жив, мы выполняем работу. Если он погибнет — каждый решает, как выбираться.", "color":"384a40"},
        {"heading":"02 / ЗАПАСНОЙ ВЫХОД", "speaker":"Мазур", "text":"Значит, надо заранее договориться, куда идти и кого брать с собой.", "color":"4b4737"},
        {"heading":"03 / ОБЕЩАНИЕ", "speaker":"Леон", "text":"Есть ход. Запомни его. Если придётся уходить, не трать время на поиски.", "color":"323e39"}
    ])

func _show_panel(panel: Dictionary, index: int, total: int) -> void:
    _location.text = panel.heading + "  ·  МАКЕТ ПОДАЧИ"
    _art.add_theme_stylebox_override("panel", Style.style(Color(panel.color), Style.BRASS))
    _caption.text = "[b]%s[/b]\n%s" % [panel.speaker, panel.text]
    _progress.text = "%d / %d · Адаптационный набросок, не цитата · Esc — пауза" % [index + 1, total]

func _button(parent: Node, text_: String, action: Callable) -> Button:
    var button := Button.new()
    button.text = text_
    Style.button(button)
    button.pressed.connect(action)
    parent.add_child(button)
    return button

func _unhandled_input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_accept") and not event.is_echo():
        sequence.advance()
        get_viewport().set_input_as_handled()

func is_ready_for_gameplay() -> bool:
    return is_instance_valid(sequence) and is_instance_valid(_caption)
