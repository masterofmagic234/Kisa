class_name ArkonJoystickMath
extends RefCounted

# Pure joystick geometry. Input handling and UI updates remain in game.gd.
static func clamped_delta(position: Vector2, center: Vector2, radius: float) -> Vector2:
    var delta := position - center
    if delta.length() > radius:
        delta = delta.normalized() * radius
    return delta

static func axis_from_delta(delta: Vector2, radius: float) -> Vector2:
    return Vector2(delta.x / radius, delta.y / radius)
