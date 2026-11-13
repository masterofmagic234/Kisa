extends Control
## The opening only: Mazur's interior voice, decoder-timed captions and a raster storyboard.
signal finished(skipped: bool)
const Style = preload("res://addons/arkon_runtime/presentation/presentation_style163.gd")
const Cues = preload("res://addons/arkon_runtime/presentation/cinematic_cues.gd")
const Stage = preload("res://scripts/intro_stage.gd")
enum State { TITLE, PLAYING, FINISHED }
var state := State.TITLE
var block_index := -1
var book: Dictionary
var muted := false
var captions_enabled := false
var _audio: AudioStreamPlayer
var _road: AudioStreamPlayer
var _art: Control
var _caption: Label
var _caption_panel: PanelContainer
var _title: Control
var _ending: Control
var _toolbar: HBoxContainer
var _pause_button: Button
var _subtitles_button: Button
var _mute_button: Button
var _progress: ProgressBar
var _cue_track := Cues.new()
var _streams: Array[AudioStream] = []
var _completion_emitted := false
var storyboard: Dictionary
var _offsets: Array[float] = []
var _controls_until := 0.0

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    PauseManager.resume()
    book = JSON.parse_string(FileAccess.get_file_as_string("res://data/intro.json"))
    storyboard = JSON.parse_string(FileAccess.get_file_as_string(book.storyboard))
    captions_enabled = bool(storyboard.captions_default)
    var elapsed := 0.0
    for block: Dictionary in book.blocks:
        _offsets.append(elapsed)
        elapsed += float(block.duration)
        _streams.append(load(block.audio) as AudioStream)
    _build_view()
    _audio = AudioStreamPlayer.new()
    _audio.process_mode = Node.PROCESS_MODE_ALWAYS
    add_child(_audio)
    _audio.finished.connect(_on_audio_finished)
    _road = AudioStreamPlayer.new()
    _road.process_mode = Node.PROCESS_MODE_ALWAYS
    var ambience := load("res://assets/intro/road-ambience.ogg") as AudioStreamOggVorbis
    ambience.loop = true
    _road.stream = ambience
    _road.volume_db = -27.0
    add_child(_road)
    PauseManager.pause_state_changed.connect(_pause_changed)
    _show_art(0)
    _art.modulate = Color(0.42, 0.42, 0.42)

func _build_view() -> void:
    var background := ColorRect.new()
    background.color = Color("151915")
    _full(background)
    add_child(background)
    _art = Stage.new()
    _full(_art)
    add_child(_art)
    _art.configure(storyboard)
    _caption_panel = PanelContainer.new()
    _caption_panel.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
    _caption_panel.offset_left = 105
    _caption_panel.offset_right = -105
    _caption_panel.offset_top = -104
    _caption_panel.offset_bottom = -20
    _caption_panel.grow_vertical = Control.GROW_DIRECTION_BEGIN
    _caption_panel.add_theme_stylebox_override("panel", Style.style(Color(0.025,0.03,0.025,0.72), Color.TRANSPARENT, Vector4(12,4,12,4),0))
    add_child(_caption_panel)
    var margin := MarginContainer.new()
    for edge in ["left", "right"]: margin.add_theme_constant_override("margin_" + edge, 12)
    margin.add_theme_constant_override("margin_top", 4)
    margin.add_theme_constant_override("margin_bottom", 4)
    _caption_panel.add_child(margin)
    var lines := VBoxContainer.new()
    lines.add_theme_constant_override("separation", 10)
    margin.add_child(lines)
    _caption = Label.new()
    _caption.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
    _caption.size_flags_vertical = Control.SIZE_EXPAND_FILL
    Style.label(_caption, 22, Color("eee7d5"))
    lines.add_child(_caption)
    _caption_panel.hide()
    _toolbar = HBoxContainer.new()
    _toolbar.set_anchors_and_offsets_preset(Control.PRESET_TOP_RIGHT)
    _toolbar.offset_left = -560
    _toolbar.offset_right = -24
    _toolbar.offset_top = 20
    _toolbar.offset_bottom = 58
    _toolbar.add_theme_constant_override("separation", 8)
    add_child(_toolbar)
    _pause_button = _button(_toolbar, "Пауза", func(): PauseManager.toggle())
    _subtitles_button = _button(_toolbar, "Субтитры", toggle_captions)
    _mute_button = _button(_toolbar, "Звук: вкл", toggle_mute)
    _button(_toolbar, "Пропустить", func(): complete(true))
    _toolbar.hide()
    _progress = ProgressBar.new()
    _progress.set_anchors_and_offsets_preset(Control.PRESET_BOTTOM_WIDE)
    _progress.offset_top = -3
    _progress.show_percentage = false
    _progress.mouse_filter = Control.MOUSE_FILTER_IGNORE
    _progress.add_theme_stylebox_override("background", Style.style(Color("20271f"), Color.TRANSPARENT, Vector4.ZERO, 0))
    _progress.add_theme_stylebox_override("fill", Style.style(Color("c0a46d"), Color.TRANSPARENT, Vector4.ZERO, 0))
    add_child(_progress)
    _progress.hide()
    _title = _card("АЛЕКСАНДР БУШКОВ", "БЕЛАЯ ГВАРДИЯ", "Мысли Кирилла Мазура", "Начать", start)
    _ending = _card("ГЛАВА ПЕРВАЯ", "КОРТЕЖ", "Мужичка того недреманная стража взяла…", "Повторить вступление", start)
    _ending.hide()

func _full(control: Control) -> void:
    control.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)

func _button(parent: Node, text_: String, action: Callable) -> Button:
    var button := Button.new()
    button.text = text_
    button.focus_mode = Control.FOCUS_NONE
    Style.button(button)
    button.add_theme_font_size_override("font_size", 14)
    button.pressed.connect(action)
    parent.add_child(button)
    return button

func _card(kicker: String, title_: String, subtitle: String, button_text: String, action: Callable) -> Control:
    var card := CenterContainer.new()
    _full(card)
    add_child(card)
    var box := VBoxContainer.new()
    box.add_theme_constant_override("separation", 20)
    card.add_child(box)
    for entry in [[kicker,16], [title_,64], [subtitle,20]]:
        var label := Label.new()
        label.text = entry[0]
        label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
        Style.label(label, entry[1], Color("eee7d5"), true)
        box.add_child(label)
    _button(box, button_text, action)
    return card

func start() -> void:
    PauseManager.resume()
    _completion_emitted = false
    state = State.PLAYING
    _title.hide()
    _ending.hide()
    _toolbar.show()
    _controls_until = Time.get_ticks_msec()/1000.0 + float(storyboard.controls_idle_seconds)
    _caption_panel.visible = captions_enabled
    _progress.show()
    _road.play()
    play_block(0)

func play_block(index: int) -> void:
    if state != State.PLAYING or index < 0 or index >= book.blocks.size(): return
    block_index = index
    _audio.stop()
    var block: Dictionary = book.blocks[index]
    _cue_track.configure(block.timed_captions)
    _show_art(index)
    _audio.stream = _streams[index]
    _audio.play()
    _audio.stream_paused = PauseManager.is_paused
    show_caption_at(0)

func _show_art(index: int) -> void:
    _art.show_at(_offsets[index])
    _art.modulate = Color.WHITE

func show_caption_at(seconds: float) -> void:
    if block_index < 0: return
    var block: Dictionary = book.blocks[block_index]
    var time := clampf(seconds, 0.0, float(block.duration) - 0.0001)
    var cue: Dictionary = _cue_track.at(time)
    if not cue.is_empty(): _caption.text = cue.text
    _art.show_at(_offsets[block_index]+time)

func _process(_delta: float) -> void:
    if state != State.PLAYING: return
    var visible_controls := PauseManager.is_paused or Time.get_ticks_msec()/1000.0 < _controls_until
    _toolbar.visible = visible_controls
    _progress.visible = visible_controls
    if PauseManager.is_paused: return
    var seconds := maxf(0, _audio.get_playback_position() + AudioServer.get_time_since_last_mix() - AudioServer.get_output_latency())
    show_caption_at(seconds)
    var before := 0.0
    for i in range(block_index): before += float(book.blocks[i].duration)
    _progress.value = (before + seconds) / float(book.duration) * 100.0

func _on_audio_finished() -> void:
    if state != State.PLAYING or _audio.playing: return
    if block_index + 1 < book.blocks.size(): play_block(block_index + 1)
    else: complete(false)

func _pause_changed(paused: bool) -> void:
    _audio.stream_paused = paused
    _road.stream_paused = paused
    _pause_button.text = "Продолжить" if paused else "Пауза"

func toggle_mute() -> void:
    muted = not muted
    _audio.volume_db = -80 if muted else 0
    _road.volume_db = -80 if muted else -27
    _mute_button.text = "Звук: выкл" if muted else "Звук: вкл"
    if muted and not captions_enabled: toggle_captions()

func toggle_captions() -> void:
    if muted and captions_enabled: return
    captions_enabled = not captions_enabled
    _caption_panel.visible = captions_enabled and state == State.PLAYING
    _subtitles_button.text = "Субтитры: вкл" if captions_enabled else "Субтитры"

func complete(skipped: bool = false) -> void:
    if state != State.PLAYING or _completion_emitted: return
    _completion_emitted = true
    state = State.FINISHED
    PauseManager.resume()
    _audio.stop()
    _road.stop()
    _art.modulate = Color(0.24,0.24,0.24)
    _art.scale = Vector2.ONE
    _toolbar.hide()
    _caption_panel.hide()
    _progress.hide()
    _ending.show()
    finished.emit(skipped)

func _unhandled_key_input(event: InputEvent) -> void:
    if event.is_echo(): return
    if event.is_action_pressed("ui_accept"):
        if state == State.PLAYING: PauseManager.toggle()
        else: start()
        get_viewport().set_input_as_handled()

func _input(event: InputEvent) -> void:
    if state==State.PLAYING and (event is InputEventMouseMotion or event is InputEventScreenTouch):
        _controls_until = Time.get_ticks_msec()/1000.0 + float(storyboard.controls_idle_seconds)

func _notification(what: int) -> void:
    if what in [NOTIFICATION_APPLICATION_FOCUS_OUT, NOTIFICATION_APPLICATION_PAUSED] and state == State.PLAYING:
        PauseManager.set_paused(true)

func is_ready_for_gameplay() -> bool:
    return is_instance_valid(_audio) and _streams.size() == book.blocks.size()

func _exit_tree() -> void:
    if is_instance_valid(_audio):
        _audio.stop()
        _audio.stream = null
    if is_instance_valid(_road):
        _road.stop()
        _road.stream = null
    _streams.clear()
    if is_instance_valid(_art): _art.clear()
