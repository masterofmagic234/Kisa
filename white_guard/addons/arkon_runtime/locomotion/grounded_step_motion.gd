extends RefCounted
## A short stair is crossed by sweeping the real body up, forward and down.
## Every part of the path must be clear, and the landing must be a floor.
## This deliberately has no terrain-height lookup or transform assignment.
const HEIGHT := .20
const LANDING_MARGIN := .012

static func move(body: CharacterBody3D, delta: float) -> bool:
    var stepped := false
    var horizontal := Vector3(body.velocity.x,0,body.velocity.z)*delta
    if body.is_on_floor() and body.velocity.y <= 0 and horizontal.length_squared() > .000001:
        stepped = _try_step(body,horizontal)
    if stepped:
        # The horizontal displacement was consumed by the three body sweeps.
        # Refresh grounded contacts without moving a second horizontal frame.
        var wanted := body.velocity
        body.velocity = Vector3(0,-.5,0)
        body.move_and_slide()
        body.velocity.x=wanted.x;body.velocity.z=wanted.z
    else:
        body.move_and_slide()
    return stepped

static func _try_step(body: CharacterBody3D, horizontal: Vector3) -> bool:
    var parameters := PhysicsTestMotionParameters3D.new()
    parameters.from=body.global_transform
    parameters.motion=horizontal
    parameters.margin=body.safe_margin
    parameters.max_collisions=4
    var result := PhysicsTestMotionResult3D.new()
    if not PhysicsServer3D.body_test_motion(body.get_rid(),parameters,result):return false
    var floor_dot := cos(body.floor_max_angle)
    var blocked := false
    for index in range(result.get_collision_count()):
        if result.get_collision_normal(index).dot(body.up_direction)<floor_dot:blocked=true
    if not blocked:return false
    var ordinary_progress := result.get_travel().dot(horizontal.normalized())
    parameters.motion=body.up_direction*HEIGHT
    if PhysicsServer3D.body_test_motion(body.get_rid(),parameters,result):return false
    var up := parameters.motion
    parameters.from.origin+=up
    parameters.motion=horizontal
    PhysicsServer3D.body_test_motion(body.get_rid(),parameters,result)
    var forward := result.get_travel()
    if forward.dot(horizontal.normalized())<ordinary_progress+.0005:return false
    parameters.from.origin+=forward
    parameters.motion=-body.up_direction*(HEIGHT+LANDING_MARGIN)
    if not PhysicsServer3D.body_test_motion(body.get_rid(),parameters,result):return false
    var walkable := false
    for index in range(result.get_collision_count()):
        if result.get_collision_normal(index).dot(body.up_direction)>=floor_dot:walkable=true
    if not walkable:return false
    var down := result.get_travel()
    var rise := (up+down).dot(body.up_direction)
    if rise<.002 or rise>HEIGHT+.001:return false
    # Keep collision recovery on every executed movement, including if another
    # actor moves into this path between the prediction and execution.
    body.move_and_collide(up)
    body.move_and_collide(forward)
    body.move_and_collide(down)
    body.apply_floor_snap()
    return true
