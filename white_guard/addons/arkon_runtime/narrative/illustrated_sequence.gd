extends Node
## Static panels advance by reader input; facts are committed by the owner.
signal panel_changed(panel: Dictionary, index: int, total: int)
signal finished(sequence_id: StringName, facts: Dictionary, skipped: bool)
var active := false
var sequence_id: StringName = &""
var index := -1
var _panels: Array = []
var _facts: Dictionary = {}
var _held := 0.0

func play(id: StringName, panels: Array, facts: Dictionary = {}) -> Error:
    if id == &"" or panels.is_empty(): return ERR_INVALID_DATA
    for panel in panels:
        if not panel is Dictionary or not panel.get("text", "") is String or panel.get("text", "").is_empty(): return ERR_INVALID_DATA
        if float(panel.get("minimum_hold", 0.0)) < 0: return ERR_INVALID_DATA
    sequence_id = id
    _panels = panels.duplicate(true)
    _facts = facts.duplicate(true)
    index = 0
    active = true
    _show()
    return OK

func _process(delta: float) -> void:
    if active: _held += delta

func advance() -> bool:
    if not active or _held < float(_panels[index].get("minimum_hold", 0.0)): return false
    index += 1
    if index >= _panels.size(): _complete(false)
    else: _show()
    return true

func skip() -> void:
    if active: _complete(true)

func _show() -> void:
    _held = 0.0
    panel_changed.emit(_panels[index].duplicate(true), index, _panels.size())

func _complete(skipped: bool) -> void:
    active = false
    finished.emit(sequence_id, _facts.duplicate(true), skipped)
