extends RefCounted

# Runtime state Level 2.

var countdown: float = 3.0
var race_started: bool = false
var race_finished: bool = false
var mission_complete: bool = false
var mission_failed: bool = false

var lap: int = 0
var total_laps: int = 0
var lap_time: float = 0.0
var best_lap: float = -1.0
var race_time: float = 0.0
var last_lap_time: float = 0.0

var position: int = 1
var racer_count: int = 0

var player_progress: float = 0.0
var ai_progress: Array = []

var message_time: float = 0.0

var hp: int = 100
var ammo: int = 0
var acorns: int = 0
var acorns_total: int = 0

func setup(laps: int, racers: int) -> void:
    total_laps = laps
    racer_count = racers
    ai_progress.clear()
    ai_progress.resize(max(0, racers - 1))
    for i in ai_progress.size():
        ai_progress[i] = 0.0

func reset_race() -> void:
    countdown = 3.0
    race_started = false
    race_finished = false
    mission_complete = false
    mission_failed = false
    lap = 0
    lap_time = 0.0
    best_lap = -1.0
    race_time = 0.0
    last_lap_time = 0.0
    position = 1
    player_progress = 0.0
    for i in ai_progress.size():
        ai_progress[i] = 0.0
    message_time = 0.0
    hp = 100
    ammo = 0
    acorns = 0
