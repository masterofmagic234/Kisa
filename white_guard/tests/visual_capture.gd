extends SceneTree
## Run with a real X11/GLES display. Headless screenshots are not visual evidence.
func _initialize() -> void:
    call_deferred("_run")

func _capture(name_: String) -> void:
    for i in range(8):await process_frame
    await RenderingServer.frame_post_draw
    var image := root.get_texture().get_image()
    if image==null or image.is_empty():
        push_error("Visual capture requires a rendering display")
        quit(1)
        return
    image.save_png("res://test-results/"+name_+".png")

func _run() -> void:
    DirAccess.make_dir_recursive_absolute("res://test-results")
    var scene: Node = (load("res://scenes/planning_room.tscn") as PackedScene).instantiate()
    root.add_child(scene)
    current_scene=scene
    await _capture("planning-room")
    scene.call("_show","dialogues")
    scene.call("_select",10)
    await _capture("dialogue-review")
    root.get_node("SceneFlow").go_to(&"lab")
    for i in range(300):
        await process_frame
        if not root.get_node("SceneFlow").is_loading_gameplay():break
    if current_scene==null or current_scene.name!="RuntimeLab":
        push_error("Visual scene transition failed")
        quit(1)
        return
    await _capture("runtime-lab")
    root.get_node("SceneFlow").go_to(&"illustrations")
    for i in range(300):
        await process_frame
        if not root.get_node("SceneFlow").is_loading_gameplay():break
    if current_scene==null or current_scene.name!="IllustrationLab":
        push_error("Illustrated scene transition failed")
        quit(1)
        return
    await _capture("illustration-lab")
    root.get_node("AudioManager").shutdown()
    print("VISUAL CAPTURE PASS / renderer=",RenderingServer.get_video_adapter_name())
    quit()
