extends RefCounted
class_name ArkonLevel3Navigation
# Sample the real authored bodies after synchronization. Moving actors do not
# enter the static grid; closed doors are traversable plans and opened locally.
const STEP := 12.0
var grid := AStarGrid2D.new()
var ready := false
var samples := 0
var walkable := 0
var _build_generation := 0

func build(world: Node2D, size_: Vector2, doors: Array[ArkonLevel3Door]) -> void:
    var params := _prepare(size_,doors)
    for y in range(grid.region.position.y,grid.region.end.y):
        _sample_row(world,params,y)
    ready = true

func build_sliced(world: Node2D, size_: Vector2, doors: Array[ArkonLevel3Door], rows_per_frame: int = 8) -> void:
    var params := _prepare(size_,doors)
    var generation := _build_generation
    var rows := 0
    for y in range(grid.region.position.y,grid.region.end.y):
        if not is_instance_valid(world) or not world.is_inside_tree() or generation!=_build_generation: return
        _sample_row(world,params,y)
        rows += 1
        if rows>=maxi(1,rows_per_frame) and y<grid.region.end.y-1:
            rows = 0
            # Yield through a rendered frame too: a 30 FPS device can run two
            # physics ticks per draw, which must not recombine the whole burst.
            await world.get_tree().process_frame
            if not is_instance_valid(world) or not world.is_inside_tree(): return
            await world.get_tree().physics_frame
            if not is_instance_valid(world) or not world.is_inside_tree(): return
            while world.get_tree().paused:
                await world.get_tree().process_frame
                if not is_instance_valid(world) or not world.is_inside_tree(): return
    if generation==_build_generation: ready = true

func _prepare(size_: Vector2, doors: Array[ArkonLevel3Door]) -> PhysicsShapeQueryParameters2D:
    _build_generation += 1
    ready = false
    samples = 0
    walkable = 0
    grid.region = Rect2i(Vector2i(-12,-12),Vector2i((size_/STEP).ceil())+Vector2i(24,32))
    grid.cell_size = Vector2.ONE*STEP
    grid.offset = Vector2.ONE*STEP*0.5
    grid.diagonal_mode = AStarGrid2D.DIAGONAL_MODE_NEVER
    grid.update()
    var shape := CircleShape2D.new()
    shape.radius = 8.0
    var params := PhysicsShapeQueryParameters2D.new()
    params.shape = shape
    params.collision_mask = 1
    params.collide_with_areas = false
    var excluded: Array[RID] = []
    for door in doors: excluded.append(door.pivot.get_rid())
    params.exclude = excluded
    return params

func _sample_row(world: Node2D, params: PhysicsShapeQueryParameters2D, y: int) -> void:
    for x in range(grid.region.position.x,grid.region.end.x):
        var id := Vector2i(x,y)
        params.transform = Transform2D(0.0,grid.get_point_position(id))
        var solid := not world.get_world_2d().direct_space_state.intersect_shape(params,1).is_empty()
        grid.set_point_solid(id,solid)
        samples += 1
        if not solid: walkable += 1

func nearest_id(point: Vector2) -> Vector2i:
    var id := Vector2i((point/STEP).floor())
    id.x = clampi(id.x,grid.region.position.x,grid.region.end.x-1)
    id.y = clampi(id.y,grid.region.position.y,grid.region.end.y-1)
    if not grid.is_point_solid(id): return id
    for radius in range(1,7):
        for y in range(-radius,radius+1):
            for x in range(-radius,radius+1):
                var test := id+Vector2i(x,y)
                if grid.region.has_point(test) and not grid.is_point_solid(test): return test
    return id

func path(from: Vector2, to: Vector2) -> PackedVector2Array:
    if not ready: return PackedVector2Array()
    var start := nearest_id(from)
    var goal := nearest_id(to)
    if grid.is_point_solid(start) or grid.is_point_solid(goal): return PackedVector2Array()
    return grid.get_point_path(start,goal,false)
