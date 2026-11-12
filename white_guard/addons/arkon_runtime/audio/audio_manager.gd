extends Node
# Autoload singleton. Global SFX pool + music control.
# Global owner of SignalBus.audio_event SFX. Levels only register scene-local music.

const SFX_POOL_SIZE := 12

var SFX_PATHS: Dictionary = {}

func configure_sfx(paths: Dictionary) -> void:
    SFX_PATHS = paths.duplicate()
    _load_sfx_streams()

var _sfx_streams: Dictionary = {}
var _sfx_pool: Array[AudioStreamPlayer] = []
var _sfx_cursor := 0
var _music_player: AudioStreamPlayer = null
var _signal_bus: Node = null
var _closing161:=false

func _ready() -> void:
    get_tree().auto_accept_quit=false
    get_tree().root.close_requested.connect(_close_application161)
    _load_sfx_streams()
    for i in SFX_POOL_SIZE:
        var p := AudioStreamPlayer.new()
        p.name = "SFX_%02d" % i
        p.bus = "Master"
        add_child(p)
        _sfx_pool.append(p)
    _signal_bus = get_node_or_null("/root/SignalBus")
    if _signal_bus != null and not _signal_bus.is_connected(&"audio_event", Callable(self, "_on_audio_event")):
        _signal_bus.connect(&"audio_event", Callable(self, "_on_audio_event"))
    _apply_volumes()

func _close_application161() -> void:
    if _closing161:return
    _closing161=true
    var tree:=get_tree()
    tree.paused=true
    var scene:=tree.current_scene
    if is_instance_valid(scene) and scene.has_method("_save"):scene.call("_save")
    _release_audio161(tree.root)
    shutdown()
    # Vorbis playback is released by the audio server across mixer updates.
    # Keep processing frames during the short real-time drain before quitting.
    var deadline:=Time.get_ticks_msec()+150
    while Time.get_ticks_msec()<deadline:
        OS.delay_msec(5)
        await tree.process_frame
    tree.quit()

func _release_audio161(node: Node) -> void:
    if node is AudioStreamPlayer or node is AudioStreamPlayer2D or node is AudioStreamPlayer3D:
        node.stop();node.stream=null
    for child in node.get_children():_release_audio161(child)

func _exit_tree() -> void:
    shutdown()


func shutdown() -> void:
    finish_transition_music160()
    # Explicitly release active audio for headless smoke-test teardown and
    # autoload shutdown. Safe to call more than once.
    _disconnect_signal_bus()

    for p in _sfx_pool:
        if is_instance_valid(p):
            p.stop()
            p.stream = null
            p.queue_free()
    _sfx_pool.clear()
    _sfx_streams.clear()

    if is_instance_valid(_music_player):
        _music_player.stop()
    _music_player = null


func _disconnect_signal_bus() -> void:
    if _signal_bus == null:
        return
    var callback := Callable(self, "_on_audio_event")
    if _signal_bus.is_connected(&"audio_event", callback):
        _signal_bus.disconnect(&"audio_event", callback)
    _signal_bus = null

func _on_audio_event(kind: StringName, _position: Vector3) -> void:
    if get_tree().get_first_node_in_group("scene_audio_owner") != null:
        return
    match kind:
        &"footstep":
            play_sfx(&"footstep1" if int(Time.get_ticks_msec() / 300) % 2 == 0 else &"footstep2")
        &"shoot":
            play_sfx(&"shoot")
        &"damage":
            play_sfx(&"damage")
        &"impact_soft":
            play_sfx(&"impact_soft")
        &"pickup":
            play_sfx(&"pickup")

func _load_sfx_streams() -> void:
    _sfx_streams.clear()
    for kind in SFX_PATHS:
        var path: String = SFX_PATHS[kind] as String
        var stream: AudioStream = load(path) as AudioStream
        if stream != null:
            _sfx_streams[kind] = stream

func play_sfx(kind: StringName) -> void:
    if GameState.sfx_muted:
        return
    var stream: AudioStream = _sfx_streams.get(kind, null) as AudioStream
    if stream == null:
        return
    var player := _next_free_player()
    player.volume_db = GameState.sfx_volume_db
    player.stream = stream
    player.play()

func _next_free_player() -> AudioStreamPlayer:
    for offset in _sfx_pool.size():
        var idx := posmod(_sfx_cursor + offset, _sfx_pool.size())
        if not _sfx_pool[idx].playing:
            _sfx_cursor = posmod(idx + 1, _sfx_pool.size())
            return _sfx_pool[idx]
    var p := _sfx_pool[_sfx_cursor]
    _sfx_cursor = posmod(_sfx_cursor + 1, _sfx_pool.size())
    return p

func register_music(player: AudioStreamPlayer) -> void:
    if is_instance_valid(_music_player) and _music_player != player:
        # The previous scene owns its music node; stop it before replacing the
        # reference so playback cannot outlive the scene transition.
        _music_player.stop()
        if _music_player.tree_exiting.is_connected(_on_music_exiting):
            _music_player.tree_exiting.disconnect(_on_music_exiting)
    _music_player = player
    if player == null:
        return
    if not player.tree_exiting.is_connected(_on_music_exiting):
        player.tree_exiting.connect(_on_music_exiting, CONNECT_ONE_SHOT)
    _apply_volumes()
    if SceneFlow.is_loading_gameplay():
        player.stop()
    elif GameState.music_muted and not player.get_meta("continuous_score159",false):
        player.stop()
    elif not player.playing:
        player.play()

func _on_music_exiting() -> void:
    _music_player = null

func toggle_music() -> bool:
    if not is_instance_valid(_music_player):
        _music_player = null
    GameState.set_music_muted(not GameState.music_muted)
    if is_instance_valid(_music_player):
        if GameState.music_muted and not _music_player.get_meta("continuous_score159",false):
            _music_player.stop()
        elif not _music_player.playing:
            _music_player.play()
    _apply_volumes()
    return GameState.music_muted

func toggle_sfx() -> bool:
    GameState.set_sfx_muted(not GameState.sfx_muted)
    if GameState.sfx_muted:
        for p in _sfx_pool:
            p.stop()
    return GameState.sfx_muted

func _apply_volumes() -> void:
    for p in _sfx_pool:
        p.volume_db = GameState.sfx_volume_db
    if is_instance_valid(_music_player):
        _music_player.volume_db = -80 if GameState.music_muted else GameState.music_volume_db+float(_music_player.get_meta("music_mix_gain160",0.0))

# A transition-owned copy keeps the outgoing cassette audible while its scene
# unloads. Only the actual insertion of the next tape ends this transport.
var _transition_music160: AudioStreamPlayer
func begin_transition_music160(source: AudioStreamPlayer=null) -> void:
    if is_instance_valid(_transition_music160):return
    if source==null:source=_music_player
    if not is_instance_valid(source) or not source.playing or GameState.music_muted:return
    _transition_music160=AudioStreamPlayer.new();_transition_music160.name="CassetteHandoff160"
    _transition_music160.process_mode=Node.PROCESS_MODE_ALWAYS
    _transition_music160.stream=source.stream;_transition_music160.volume_db=source.volume_db
    add_child(_transition_music160);_transition_music160.play(source.get_playback_position())
func finish_transition_music160() -> void:
    if not is_instance_valid(_transition_music160):return
    _transition_music160.stop();_transition_music160.stream=null;_transition_music160.queue_free();_transition_music160=null
