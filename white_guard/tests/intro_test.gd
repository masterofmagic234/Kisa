extends SceneTree
var checks := 0
var failures: Array[String] = []

func _initialize() -> void: call_deferred("_run")

func check(value: bool, message: String) -> void:
    checks += 1
    if not value:
        failures.append(message)
        push_error(message)

func _nodes_below(nodes: Array) -> Array:
    var result: Array = []
    for node: Dictionary in nodes:
        result.append(node)
        result.append_array(_nodes_below(node.get("children", [])))
    return result

func _articulation_sample(art: Dictionary) -> Dictionary:
    for record: Dictionary in art.scenes.values():
        for node: Dictionary in _nodes_below(record.nodes):
            var base: Dictionary = node.get("transform", {})
            var previous_time := 0.0
            var previous_value: float = float(base.get("rotation", 0.0))
            for key: Dictionary in node.get("keys", []):
                if not key.has("rotation"): continue
                var time: float = float(key.time)
                var value: float = float(key.rotation)
                if time > previous_time and not is_equal_approx(previous_value, value):
                    return {"node": node, "a": previous_time, "b": (previous_time+time)*0.5}
                previous_time = time
                previous_value = value
    return {}

func _run() -> void:
    await process_frame
    var scene := (load("res://scenes/intro.tscn") as PackedScene).instantiate()
    root.add_child(scene)
    current_scene = scene
    await process_frame
    check(ProjectSettings.get_setting("application/run/main_scene")=="res://scenes/intro.tscn", "Default entry is not the intro")
    check(scene.state==scene.State.TITLE and not scene._audio.playing, "Voice played before the start screen")
    check(scene.book.blocks.size()==12 and scene._streams.size()==12, "Opening paragraph or voice is missing")
    check(not scene.captions_enabled and not scene._caption_panel.visible, "The picture is covered by default captions")
    check(int(scene.storyboard.schema)==2, "Intro did not load the vector staging revision")
    var first_shot: Dictionary = scene._art.shot_at(0)
    var vector_art: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://data/intro_art.json"))
    var articulation := _articulation_sample(vector_art)
    check(not articulation.is_empty(), "The scene library has no articulated rotation")
    if not articulation.is_empty():
        var pose_a: Dictionary = scene._art.state_at(articulation.node, articulation.a)
        var pose_b: Dictionary = scene._art.state_at(articulation.node, articulation.b)
        check(not is_equal_approx(float(pose_a.rotation), float(pose_b.rotation)), "The articulated pose stayed rigid during its actual key interval")
        check(scene._art.state_at(articulation.node, articulation.a)==pose_a, "Seeking back did not restore the original pose")
    var reveal_shot: Dictionary = {}
    for shot: Dictionary in scene.storyboard.shots:
        if shot.get("panels", []).size()<2: continue
        for panel: Dictionary in shot.panels:
            if float(panel.get("reveal", 0))>0: reveal_shot = shot
        if not reveal_shot.is_empty(): break
    check(not reveal_shot.is_empty(), "No composition reveals a detail panel during narration")
    if not reveal_shot.is_empty():
        var panel_rects: Array = scene._art.panel_rects(reveal_shot, Vector2(1280,720))
        check(panel_rects.size()==reveal_shot.panels.size(), "Panel layout omitted a staged detail")
        for panel_rect: Rect2 in panel_rects:
            check(panel_rect.has_area() and Rect2(0,0,1280,720).encloses(panel_rect), "A panel escaped or collapsed outside the picture")
    var cut: Dictionary = scene._art.shot_at(float(first_shot.end)+0.01)
    check(cut.id!=first_shot.id, "Seeking through a cut kept the preceding composition")
    scene._art.show_at(float(first_shot.end)+0.01)
    scene._art.show_at(0)
    check(scene._art.current_shot.id==first_shot.id, "Seeking back kept a future composition")
    for i in range(scene.book.blocks.size()):
        check(absf(scene._streams[i].get_length()-float(scene.book.blocks[i].duration))<0.18, "Voice duration does not match captions: %d" % i)
    var facts: Dictionary = root.get_node("GameState").facts.duplicate(true)
    var completions: Array = []
    scene.finished.connect(func(skipped: bool): completions.append(skipped))
    scene.start()
    check(scene._audio.playing and scene._road.playing and scene.block_index==0, "Start did not play exactly the first voice")
    await create_timer(0.4).timeout
    var second: Dictionary = scene.book.blocks[0].timed_captions[1]
    scene.show_caption_at(second.start+0.01)
    check(scene._caption.text==second.text, "Caption does not follow decoded time")
    scene.show_caption_at(0)
    check(scene._caption.text==scene.book.blocks[0].timed_captions[0].text, "Seeking back kept a future caption")
    var pause := root.get_node("PauseManager")
    pause.set_paused(true)
    var at: float = scene._audio.get_playback_position()
    var picture_at: float = scene._art.current_time
    for i in range(6): await process_frame
    check(scene._audio.stream_paused and scene._road.stream_paused and absf(scene._audio.get_playback_position()-at)<0.06, "Pause did not freeze narration and ambience")
    check(scene._art.current_time==picture_at, "Paused actors/camera continued moving")
    pause.resume()
    scene.toggle_captions()
    check(scene._caption_panel.visible, "Optional captions did not appear")
    scene.toggle_captions()
    check(not scene._caption_panel.visible, "Caption toggle did not hide the panel")
    scene.toggle_mute()
    check(scene._caption_panel.visible and scene._audio.volume_db==-80 and scene._audio.playing, "Muted narration stopped the clock or hid readable text")
    scene.toggle_mute()
    scene.notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
    check(pause.is_paused and scene._audio.stream_paused, "Backgrounding consumed the monologue")
    scene.notification(Node.NOTIFICATION_APPLICATION_FOCUS_IN)
    check(pause.is_paused, "Returning to the app unexpectedly resumed the monologue")
    pause.resume()
    scene._audio.seek(float(scene.book.blocks[0].duration)-0.15)
    for i in range(180):
        await physics_frame
        if scene.block_index==1: break
    check(scene.block_index==1 and scene._audio.playing, "Real decoder completion did not start the next paragraph")
    check(int(scene._art.current_shot.block)==1, "The picture did not follow the next paragraph")
    scene.play_block(11)
    scene._audio.seek(float(scene.book.blocks[11].duration)-0.15)
    for i in range(180):
        await physics_frame
        if scene.state==scene.State.FINISHED: break
    check(scene.state==scene.State.FINISHED and completions==[false], "Final decoder completion did not finish the intro once")
    check(scene._ending.visible and not scene._audio.playing and not scene._road.playing, "Ending did not stop sound and show the chapter card")
    scene.complete(true)
    scene._on_audio_finished()
    check(completions==[false], "Late finish/skip duplicated completion")
    scene.start()
    check(scene.block_index==0 and scene._audio.playing, "Replay did not reset to the first paragraph")
    scene.complete(true)
    check(completions==[false,true], "Skip after replay did not complete exactly once")
    check(root.get_node("GameState").facts==facts, "Intro modified the unfinished game's campaign facts")
    var players := 0
    for child in scene.get_children():
        if child is AudioStreamPlayer: players+=1
    check(players==2, "Replay created overlapping voice players")
    var result := {"checks":checks,"failures":failures,"godot":Engine.get_version_info().string,"scope":"intro_only","performance_measurement":false}
    DirAccess.make_dir_recursive_absolute("res://test-results")
    var file := FileAccess.open("res://test-results/intro.json",FileAccess.WRITE)
    file.store_string(JSON.stringify(result,"  "))
    print("INTRO ", "PASS" if failures.is_empty() else "FAIL", ": ", checks, " checks")
    current_scene = null
    scene.queue_free()
    await process_frame
    # Audio playback is retired by the mixer after the nodes have gone away.
    await create_timer(0.3).timeout
    root.get_node("AudioManager").shutdown()
    await process_frame
    await process_frame
    quit(0 if failures.is_empty() else 1)
