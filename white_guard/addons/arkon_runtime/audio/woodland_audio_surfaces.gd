extends RefCounted
## Physical hit material and authored walking surfaces, without a second map.
## Garden path metadata comes directly from the mesh-building route/width.

static func step_kind(position_: Vector3, world: Node = null) -> StringName:
    return StringName("step_" + String(surface_at(position_, world)))

static func surface_at(position_: Vector3, world: Node = null) -> StringName:
    var point := Vector2(position_.x, position_.z)
    if is_instance_valid(world):
        for zone in world.get_meta("audio_surface_zones", []):
            if not zone is Dictionary: continue
            var center: Vector2 = zone.get("center", Vector2.ZERO)
            if point.distance_squared_to(center) <= pow(float(zone.get("radius", 0.0)), 2.0):
                return _walking_surface(StringName(zone.get("surface", &"stone")))
        for path in world.get_meta("audio_paths", []):
            if not path is Dictionary: continue
            var points = path.get("points", [])
            var half_width := maxf(0.0, float(path.get("half_width", 0.0)))
            for index in range(maxi(0, points.size() - 1)):
                var a: Vector2 = points[index]
                var b: Vector2 = points[index + 1]
                var closest := Geometry2D.get_closest_point_to_segment(point, a, b)
                if point.distance_squared_to(closest) <= half_width * half_width:
                    return _walking_surface(StringName(path.get("surface", &"stone")))
    return &"grass"

static func impact_kind(hit: Dictionary, world: Node = null) -> StringName:
    var collider = hit.get("collider")
    if collider is Node:
        var node: Node = collider
        while is_instance_valid(node):
            if node.is_in_group("runtime_hostile") or node.is_in_group("runtime_target"):
                return &"impact_soft"
            if node.has_meta("audio_surface"):
                var material := StringName(node.get_meta("audio_surface"))
                # Paving and stairs are render meshes over the terrain's one
                # physical floor. An upward ground hit must use that authored
                # surface instead of blindly returning the terrain grass tag.
                var normal: Vector3 = hit.get("normal", Vector3.ZERO)
                if material in [&"grass", &"soil", &"dirt"] and normal.y > .65 and hit.get("position") is Vector3:
                    material = surface_at(hit.position, world)
                return _impact_for_surface(material)
            var title := String(node.name).to_lower()
            if "tree" in title or "bench" in title or "wood" in title:
                return &"impact_wood"
            if "gate" in title or "iron" in title or "pipe" in title or "rail" in title:
                return &"impact_metal"
            if node == world: break
            node = node.get_parent()
    return &"impact_stone"

static func _walking_surface(surface: StringName) -> StringName:
    if surface in [&"stone", &"paving", &"trim", &"metal"]: return &"stone"
    if surface in [&"soil", &"dirt", &"gravel", &"wood"]: return &"soil"
    return &"grass"

static func _impact_for_surface(surface: StringName) -> StringName:
    if surface in [&"wood", &"bark"]: return &"impact_wood"
    if surface in [&"metal", &"iron"]: return &"impact_metal"
    if surface in [&"soil", &"dirt", &"grass", &"gravel"]: return &"impact_soil"
    if surface in [&"soft", &"flesh", &"fur", &"cloth"]: return &"impact_soft"
    return &"impact_stone"
