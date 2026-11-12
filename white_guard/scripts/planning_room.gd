extends Control
## Review workspace only. Campaign entries are proposals, not selectable levels.
const Style = preload("res://addons/arkon_runtime/presentation/presentation_style163.gd")
var _campaign: Dictionary
var _dialogues: Dictionary
var _items: ItemList
var _body: RichTextLabel
var _mode := "campaign"

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    PauseManager.resume()
    Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
    _campaign = JSON.parse_string(FileAccess.get_file_as_string("res://data/campaign_plan.json"))
    _dialogues = JSON.parse_string(FileAccess.get_file_as_string("res://data/dialogue_selection.json"))
    var order: Array[StringName] = []
    SceneFlow.configure({&"planning": "res://scenes/planning_room.tscn", &"lab": "res://scenes/runtime_lab.tscn"}, order)
    var background := ColorRect.new()
    background.color = Color("121b18")
    background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    add_child(background)
    var margin := MarginContainer.new()
    margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    for edge in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + edge, 28)
    add_child(margin)
    var page := VBoxContainer.new()
    page.add_theme_constant_override("separation", 14)
    margin.add_child(page)
    var title := Label.new()
    title.text = "БЕЛАЯ ГВАРДИЯ"
    Style.label(title, 40, Style.IVORY, true)
    page.add_child(title)
    var subtitle := Label.new()
    subtitle.text = "Александр Бушков · Пиранья  /  План экранизации и стенд движка"
    Style.label(subtitle, 18, Style.BRASS)
    page.add_child(subtitle)
    var notice := Label.new()
    notice.text = "ПРЕДПРОИЗВОДСТВО. Сюжетные уровни ещё не созданы. План ждёт решения автора проекта."
    Style.label(notice, 14, Style.SAGE)
    page.add_child(notice)
    var navigation := HBoxContainer.new()
    navigation.add_theme_constant_override("separation", 12)
    page.add_child(navigation)
    _button(navigation, "Кампания", func(): _show("campaign"))
    _button(navigation, "Ключевые диалоги", func(): _show("dialogues"))
    _button(navigation, "Перенесённые системы", func(): _show("engine"))
    _button(navigation, "Испытательный стенд", func(): SceneFlow.go_to(&"lab"))
    var split := HSplitContainer.new()
    split.size_flags_vertical = Control.SIZE_EXPAND_FILL
    split.split_offset = 300
    page.add_child(split)
    _items = ItemList.new()
    _items.custom_minimum_size.x = 275
    _items.add_theme_font_override("font", Style.body_font())
    _items.add_theme_font_size_override("font_size", 17)
    _items.add_theme_constant_override("v_separation", 10)
    split.add_child(_items)
    _items.item_selected.connect(_select)
    _body = RichTextLabel.new()
    _body.bbcode_enabled = true
    _body.selection_enabled = true
    _body.add_theme_font_override("normal_font", Style.body_font())
    _body.add_theme_font_override("bold_font", Style.body_font(true))
    _body.add_theme_font_size_override("normal_font_size", 19)
    _body.add_theme_color_override("default_color", Style.IVORY)
    split.add_child(_body)
    _show("campaign")

func _button(parent: Node, title: String, callback: Callable) -> void:
    var button := Button.new()
    button.text = title
    Style.button(button)
    button.pressed.connect(callback)
    parent.add_child(button)

func _show(mode: String) -> void:
    _mode = mode
    _items.clear()
    if mode == "campaign":
        for entry in _campaign.missions: _items.add_item("%s  %s" % [entry.id.to_upper(), entry.title])
    elif mode == "dialogues":
        for entry in _dialogues.scenes: _items.add_item("%s  %s" % [entry.id, entry.title])
    else:
        for text in ["Происхождение", "Боевые компоненты", "Движение", "Машины", "Графика и звук", "Что ещё предстоит"]: _items.add_item(text)
    _items.select(0)
    _select(0)

func _select(index: int) -> void:
    if _mode == "campaign":
        var m: Dictionary = _campaign.missions[index]
        _body.text = "[font_size=30]%s[/font_size]\n%s · главы %s · около %d минут\n\n[b]Задача[/b]\n%s\n\n[b]Действия игрока[/b]\n%s\n\n[b]Сохраняем из книги[/b]\n%s\n\n[b]Сокращаем[/b]\n%s\n\n[b]Постановочное ограничение[/b]\n%s\n\n[b]Реализация[/b]\n%s\n\n[i]Предложение; игровая сцена этой главы ещё не реализована.[/i]" % [m.title,m.act,_chapters(m.source_chapters),m.estimated_minutes,m.goal,m.player_activity,m.canonical_beats,m.compression,m.invariant,m.implementation]
    elif _mode == "dialogues":
        var d: Dictionary = _dialogues.scenes[index]
        _body.text = "[font_size=30]%s[/font_size]\n%s · главы %s · до %d секунд\n\n[b]Зачем сохраняем[/b]\n%s\n\n[b]Ограничение[/b]\n%s\n\n[b]Адаптационный набросок[/b]\n" % [d.title,"Обязательная сцена" if d.required else "Необязательная сцена",_chapters(d.source_chapters),d.budget_seconds,d.purpose,d.invariant]
        for line in d.lines: _body.append_text("\n[b]%s:[/b] %s\n" % [line.speaker,line.text])
        _body.append_text("\n[i]Это не окончательный сценарий и не дословные цитаты книги.[/i]")
    else:
        var cards := [
            "[b]Основа: Arkon Hunter 1.68[/b]\nПеренос привязан к коммиту a1fd971. В provenance/runtime-extraction.json записаны исходные и конечные SHA-256 каждого перенесённого файла. Полный текст книги и личные материалы сюда не включены.",
            "[b]Бой[/b]\nЗдоровье, hitbox 2D/3D, оружие, отдельный боезапас каждого вида, cooldown и чистые проверки. Каталог оружия задаёт новая игра; старые спрайты и конусомёт не являются зависимостями.",
            "[b]Движение[/b]\nМатематика движения и настоящие физические проходы по ступенькам из релиза. Переносимый FPS-адаптер проверяется на стенде. Прыжки, укрытия и полноценные союзники ещё не готовы.",
            "[b]Автомобили[/b]\nСохранены проверенные расчёты дороги, трансмиссии, контактов, камеры и проекции. Старая трасса служит тестовой фикстурой. Это ещё не тяжёлый автобус и не готовый уровень погони.",
            "[b]Представление[/b]\nПрофили качества, монитор времени кадра, пул SFX, передача музыки между сценами, шрифты и нейтральные материалы. Таймер готовности не расходует лимит на паузе и в фоне. Linux требует отдельной проверки производительности на устройстве.",
            "[b]Следующая цель после обсуждения[/b]\nВертикальный срез «Дом антиквара»: 10–15 минут, один интерьер, вход под легендой, скоординированный штурм и спасение. Нужны человеческий AI, анимации, роли союзников, взаимодействие с дверями и постановка диалога."
        ]
        _body.text = cards[index]
    _body.scroll_to_line(0)

func _chapters(values: Array) -> String:
    var labels := PackedStringArray()
    for value in values: labels.append(str(int(value)))
    return ", ".join(labels)
