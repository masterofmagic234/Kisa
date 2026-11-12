extends RefCounted
## Wall-clock intervals from active gameplay only; no GPU timing on Android GLES.

var _warm_until := 0
var _cool_until := 0
var _window_start := 0
var _samples: Array[float] = []
var _slow_windows := 0
var _median := 0.0
var _p95 := 0.0
var _slow_fraction := 0.0
var _windows := 0
var _invalid := 0

func reset(now_usec: int, warmup_sec: float = 3.0) -> void:
    _warm_until = now_usec + int(warmup_sec * 1000000.0)
    _window_start = 0
    _samples.clear()
    _slow_windows = 0

func note_invalid(now_usec: int) -> void:
    _invalid += 1
    reset(now_usec)

func observe(now_usec: int, frame_ms: float, active: bool, target_fps: int) -> bool:
    if not active or not is_finite(frame_ms) or frame_ms <= 0.0 or frame_ms > 2000.0:
        note_invalid(now_usec)
        return false
    if now_usec < _warm_until or now_usec < _cool_until: return false
    if _window_start == 0: _window_start = now_usec
    _samples.append(frame_ms)
    if now_usec - _window_start < 3000000: return false
    # On a genuinely overloaded phone, wait longer to collect evidence.
    # Resetting every3seconds would prevent any fallback below8FPS.
    if _samples.size() < 24: return false
    _samples.sort()
    _median = _samples[_samples.size() / 2]
    _p95 = _samples[mini(_samples.size()-1, int(float(_samples.size()) * .95))]
    _windows += 1
    var budget := 1000.0 / maxi(1, target_fps)
    # A few compilation/OS spikes cannot turn a steady capped30 into overload.
    var slow_frames := 0
    for sample in _samples:
        if sample > budget * 1.12: slow_frames += 1
    _slow_fraction = float(slow_frames) / float(_samples.size())
    # Round the quarter-window count down: a periodic missed frame must not
    # alternate24.7%/25.9% forever and evade the two-window confirmation.
    var repeated_stutter := slow_frames >= maxi(1,floori(float(_samples.size()) * .25))
    var overloaded := _median > budget * 1.12 or (_p95 > budget * 1.45 and repeated_stutter)
    _slow_windows = _slow_windows + 1 if overloaded else 0
    _samples.clear()
    _window_start = now_usec
    if _slow_windows < 2: return false
    _cool_until = now_usec + 10000000
    reset(now_usec)
    return true

func diagnostics() -> Dictionary:
    return {"samples":_samples.size(), "median_ms":_median, "p95_ms":_p95, "slow_fraction":_slow_fraction, "windows":_windows, "slow_windows":_slow_windows, "invalid_frames":_invalid, "warm_until_usec":_warm_until, "cool_until_usec":_cool_until}
