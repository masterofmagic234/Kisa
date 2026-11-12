extends RefCounted

# Запросы к RaceState.

static func can_control(state) -> bool:
    return state.race_started and not state.race_finished \
        and not state.mission_complete and not state.mission_failed

static func is_active(state) -> bool:
    return can_control(state)

static func is_finished(state) -> bool:
    return state.race_finished or state.mission_complete or state.mission_failed

static func is_last_lap(state) -> bool:
    return state.lap >= state.total_laps - 1
