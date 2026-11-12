extends RefCounted
## Ported clock contract from intro_cinematic_v2: subtitles follow decoded time.
## It never advances narrative time using render delta or starts a second voice.
var cues: Array = []

func configure(records: Array) -> Error:
    var previous := -1.0
    for row in records:
        if not row is Dictionary or not row.has("start") or not row.has("end"): return ERR_INVALID_DATA
        if float(row.start) < previous or float(row.end) < float(row.start): return ERR_INVALID_DATA
        previous = float(row.start)
    cues = records.duplicate(true)
    return OK

func at(decoded_position: float) -> Dictionary:
    for cue in cues:
        if decoded_position >= float(cue.start) and decoded_position < float(cue.end): return cue
    return {}
