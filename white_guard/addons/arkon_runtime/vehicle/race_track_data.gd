extends Resource
class_name ArkonRaceTrackData

@export var section_types: PackedInt32Array = PackedInt32Array()
@export var section_counts: PackedInt32Array = PackedInt32Array()

func build_pattern() -> Array:
    var pattern: Array = []
    var count := mini(section_types.size(), section_counts.size())
    for index in range(count):
        var segment_type := int(section_types[index])
        var repetitions := maxi(int(section_counts[index]), 0)
        for _i in range(repetitions):
            pattern.append(segment_type)
    return pattern

func total_segments() -> int:
    var total := 0
    for count in section_counts:
        total += maxi(int(count), 0)
    return total
