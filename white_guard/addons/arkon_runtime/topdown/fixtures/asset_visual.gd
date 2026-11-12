extends RefCounted
class_name ArkonLevel3AssetVisual
## Static fixture textures replace the original animated game art.
static func first_frame_texture(path: String) -> Texture2D:
    return load(path) as Texture2D if not path.is_empty() else null
static func animated_strip(path: String, _fps: float, size_: Vector2) -> Node2D:
    var sprite := Sprite2D.new()
    sprite.texture = first_frame_texture(path)
    sprite.scale = size_
    return sprite
static func frame_texture(path: String, _frame: int = 0) -> Texture2D:
    return first_frame_texture(path)
static func sprite_frames_from_strip(path: String, fps: float, looping: bool) -> SpriteFrames:
    var frames := SpriteFrames.new()
    frames.set_animation_speed(&"default", fps)
    frames.set_animation_loop(&"default", looping)
    frames.add_frame(&"default", first_frame_texture(path))
    return frames
