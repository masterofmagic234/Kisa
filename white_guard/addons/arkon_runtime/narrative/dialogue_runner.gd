extends Node
## New adapter: book adaptation needs an authored dialogue layer absent in Arkon.
signal line_changed(speaker: String, text: String)
signal dialogue_finished(id: StringName)
var lines: Array = []
var cursor := -1
var active_id: StringName = &""
var active := false

func start(id: StringName, records: Array) -> Error:
    for row in records:
        if not row is Dictionary or not row.get("speaker") is String or not row.get("text") is String:
            return ERR_INVALID_DATA
    active_id = id
    lines = records.duplicate(true)
    cursor = -1
    active = true
    advance()
    return OK

func advance() -> void:
    if not active: return
    cursor += 1
    if cursor >= lines.size():
        active = false
        dialogue_finished.emit(active_id)
        return
    line_changed.emit(lines[cursor].speaker, lines[cursor].text)

func cancel() -> void:
    active = false
    lines.clear()
