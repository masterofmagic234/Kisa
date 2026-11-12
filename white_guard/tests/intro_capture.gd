extends SceneTree
func _initialize() -> void: call_deferred("_run")

func capture(name_: String) -> void:
    for i in range(6): await process_frame
    await RenderingServer.frame_post_draw
    root.get_texture().get_image().save_png("res://test-results/intro-"+name_+".png")

func _run() -> void:
    DirAccess.make_dir_recursive_absolute("res://test-results")
    var scene := (load("res://scenes/intro.tscn") as PackedScene).instantiate()
    root.add_child(scene)
    current_scene=scene
    await capture("title")
    scene.start()
    scene._controls_until=0
    scene._audio.seek(3.0)
    await capture("convoy")
    scene.play_block(6)
    root.get_node("PauseManager").resume()
    await create_timer(0.45).timeout
    scene._controls_until=0
    await capture("appointment")
    scene.play_block(9)
    root.get_node("PauseManager").resume()
    await create_timer(0.45).timeout
    scene._controls_until=0
    await capture("warehouses")
    scene._audio.seek(49.0)
    await capture("reconstruction")
    root.get_node("PauseManager").set_paused(true)
    scene.toggle_captions()
    var longest := ""
    for block: Dictionary in scene.book.blocks:
        for cue: Dictionary in block.timed_captions:
            if cue.text.length() > longest.length(): longest = cue.text
    scene._caption.text = longest
    await capture("longest-caption")
    if scene._caption.get_global_rect().end.y > scene.size.y + 1:
        push_error("The longest subtitle extends below the screen")
        quit(1)
        return
    if scene._caption.get_line_count()>2:
        push_error("Optional subtitle grew beyond two lines")
        quit(1)
        return
    scene.complete(false)
    await capture("ending")
    current_scene = null
    scene.queue_free()
    await process_frame
    await create_timer(0.3).timeout
    root.get_node("AudioManager").shutdown()
    print("INTRO CAPTURE PASS / ",RenderingServer.get_video_adapter_name())
    await process_frame
    await process_frame
    quit()
