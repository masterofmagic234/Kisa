extends RefCounted
## Extracted policy from SceneFlow 1.68: only foreground, unpaused time counts.
## A blocked resource/GPU call must not be counted as many rendered blank frames.
var last_usec := 0
var elapsed_usec := 0
var generation := 0

func reset(now_usec: int) -> void:
    last_usec = now_usec
    elapsed_usec = 0
    generation += 1

func observe(now_usec: int, active: bool, timeout_seconds: float) -> bool:
    var step := clampi(now_usec - last_usec, 0, 250000)
    last_usec = now_usec
    if active: elapsed_usec += step
    return elapsed_usec > int(timeout_seconds * 1000000.0)
