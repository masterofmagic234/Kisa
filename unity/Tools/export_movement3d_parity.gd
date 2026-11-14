extends SceneTree
const Motion = preload("res://addons/arkon_runtime/math/movement_math.gd")
const Step = preload("res://addons/arkon_runtime/locomotion/grounded_step_motion.gd")
func _initialize() -> void: call_deferred("run")
func arr(v: Vector3) -> Array:return [v.x,v.y,v.z]
func box(size_:Vector3,position_:Vector3,parent:Node3D)->void:
    var body:=StaticBody3D.new();var collision:=CollisionShape3D.new();var shape:=BoxShape3D.new();shape.size=size_;collision.shape=shape;body.add_child(collision);parent.add_child(body);body.position=position_
func probe(actor:CharacterBody3D, pose:Transform3D, motion:Vector3)->Dictionary:
    var p:=PhysicsTestMotionParameters3D.new();p.from=pose;p.motion=motion;p.margin=actor.safe_margin;p.max_collisions=4
    var r:=PhysicsTestMotionResult3D.new();var collided:=PhysicsServer3D.body_test_motion(actor.get_rid(),p,r)
    var normals:=[]
    for i in range(r.get_collision_count()):normals.append(arr(r.get_collision_normal(i)))
    return {"from":arr(pose.origin),"motion":arr(motion),"collided":collided,"travel":arr(r.get_travel()),"normals":normals}
func trace(actor:CharacterBody3D)->Array:
    var pose:=actor.global_transform;var a:=probe(actor,pose,Vector3(1,0,0));var b:=probe(actor,pose,Vector3(0,.2,0))
    pose.origin+=Vector3(0,.2,0);var c:=probe(actor,pose,Vector3(1,0,0));pose.origin+=Vector3(c.travel[0],c.travel[1],c.travel[2]);var d:=probe(actor,pose,Vector3(0,-.212,0))
    return [a,b,c,d]
func stair(height:float)->Dictionary:
    var world:=Node3D.new();root.add_child(world)
    box(Vector3(10,.2,10),Vector3(0,-.1,0),world)
    box(Vector3(2,height,2),Vector3(1,height*.5,0),world)
    var actor:=CharacterBody3D.new();actor.floor_max_angle=deg_to_rad(52);actor.floor_snap_length=.4;actor.safe_margin=.001
    var collision:=CollisionShape3D.new();var shape:=CapsuleShape3D.new();shape.height=1.8;shape.radius=.25;collision.shape=shape;actor.add_child(collision);world.add_child(actor);actor.position=Vector3(-.6,.9,0)
    await physics_frame;await physics_frame
    actor.velocity=Vector3(0,-.5,0);actor.move_and_slide()
    var before:=actor.global_position;var grounded:=actor.is_on_floor()
    var queries:=trace(actor)
    var stepped:bool=Step._try_step(actor,Vector3(1,0,0))
    var answer:={"height":height,"grounded":grounded,"stepped":stepped,"displacement":arr(actor.global_position-before),"origin":arr(before),"queries":queries,"predicted_rise":.2+float(queries[3].travel[1])}
    world.queue_free();await physics_frame;return answer
func source_hash(path:String)->String:
    var context:=HashingContext.new();context.start(HashingContext.HASH_SHA256);context.update(FileAccess.get_file_as_bytes(path));return context.finish().hex_encode()
func run()->void:
    var result:={"forward":arr(Motion.velocity_for_input(Basis.IDENTITY,Vector2(0,-1),4)),"diagonal":arr(Motion.velocity_for_input(Basis.IDENTITY,Vector2(1,-1).normalized(),4)),"yaw90":arr(Motion.velocity_for_input(Basis(Vector3.UP,PI*.5),Vector2(0,-1),4)),"tilted":arr(Motion.velocity_for_input(Basis(Vector3.RIGHT,.5),Vector2(0,-1),4)),"oblique":arr(Motion.velocity_for_input(Basis(Vector3.UP,-.25),Vector2(.2,-.8),4))}
    result.low_step=await stair(.15);result.high_step=await stair(.25)
    result.schema=1;result.godot=Engine.get_version_info().string
    result.validation_scope="Public Godot movement values and native Godot query traces; not Unity native-physics verification."
    result.source_files={}
    for source in ["addons/arkon_runtime/math/movement_math.gd","addons/arkon_runtime/locomotion/first_person_actor.gd","addons/arkon_runtime/locomotion/grounded_step_motion.gd"]:result.source_files[source]=source_hash("res://"+source)
    result.fixture={"capsule_height":1.8,"capsule_radius":.25,"step_forward_motion":1.0,"floor_max_degrees":52,"safe_margin":.001}
    var args:=OS.get_cmdline_user_args()
    var destination:String=args[0] if not args.is_empty() else ProjectSettings.globalize_path("res://").path_join("../unity/Tests/Fixtures/godot168-movement3d.json").simplify_path()
    DirAccess.make_dir_recursive_absolute(destination.get_base_dir())
    var file:=FileAccess.open(destination,FileAccess.WRITE)
    if file==null:push_error("Could not write movement oracle "+destination);quit(1);return
    file.store_string(JSON.stringify(result,"  ",true,true));file.close()
    print("MOVEMENT3D ORACLE ",destination)
    root.get_node("AudioManager").shutdown();await process_frame;quit()
