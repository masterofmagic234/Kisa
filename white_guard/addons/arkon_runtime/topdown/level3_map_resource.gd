extends Resource
class_name ArkonLevel3MapResource

const FLOOR := 0
const WALL := 1
const DOOR := 2

@export var width: int = 0
@export var height: int = 0
@export var tile_size: int = 16
@export var cells: PackedByteArray = PackedByteArray()

func map_size() -> Vector2i:
    return Vector2i(width, height)

func is_inside(cell: Vector2i) -> bool:
    return cell.x >= 0 and cell.y >= 0 and cell.x < width and cell.y < height

func tile_at(cell: Vector2i) -> int:
    if not is_inside(cell):
        return WALL
    var index := cell.y * width + cell.x
    if index < 0 or index >= cells.size():
        return WALL
    return int(cells[index])

func is_walkable(cell: Vector2i) -> bool:
    return tile_at(cell) != WALL

func to_rows() -> PackedStringArray:
    var result := PackedStringArray()
    for y in range(height):
        var row := ""
        for x in range(width):
            match tile_at(Vector2i(x, y)):
                DOOR:
                    row += "D"
                WALL:
                    row += "#"
                _:
                    row += "."
        result.append(row)
    return result
