#!/usr/bin/env python3
"""Extract the portable runtime from the *released* Git tree, never a dirty checkout.

Usage: python tools/extract_arkon_runtime.py /path/to/Arkon [--verify]
The novel, personal media, signing keys, and game campaigns are not inputs.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

REVISION = "a1fd971558fcfe8d8fb99e944cc532edee5c32ba"
ROOT = Path(__file__).resolve().parents[1]
PREFIX = "addons/arkon_runtime/"
GROUPS = {
    "core": ["signal_bus", "pause_manager", "callable_state"],
    "math": ["ammo_math", "health_math", "movement_math", "turn_math", "joystick_math", "fire_query", "pickup_query", "death_query", "mission_state_query", "mission_progress_query", "cone_query"],
    "combat": ["components/health_component", "components/hitbox_3d_component", "components/hitbox_component", "weapon_data"],
    "locomotion": ["grounded_step_motion"],
    "vehicle": ["race_math", "race_car_controller", "race_powertrain", "race_level_data", "race_track_data", "race_projection", "race_projection_frame", "race_canvas_batch", "race_road_mesh", "race_camera_state", "race_vehicle_contacts", "race_skid_marks", "race_ai_controller", "race_ai_assist", "race_queries", "race_barriers", "race_state"],
    "graphics": ["graphics_settings", "graphics_policy156", "graphics_store156", "graphics_monitor156"],
    "audio": ["audio_manager", "woodland_audio_surfaces"],
    "presentation": ["presentation_style163"],
    "topdown": ["level3_player", "level3_enemy", "level3_projectile", "level3_navigation", "level3_door", "level3_pickup", "level3_map_resource", "level3_touch_joystick", "object_pool", "components/weapon_component"],
}
MAP = {f"scripts/{name}.gd": f"{PREFIX}{group}/{name.split('/')[-1]}.gd" for group, names in GROUPS.items() for name in names}
MAP["resources/race/race_track_default.tres"] = PREFIX + "vehicle/fixtures/race_track_default.tres"
for name in ["player", "enemy", "door", "projectile", "pickup"]:
    MAP[f"scenes/level3_{name}.tscn"] = PREFIX + f"topdown/scenes/{name}.tscn"
for name in ["pistol", "shotgun", "bat"]:
    MAP[f"resources/weapons/level3_{name}.tres"] = PREFIX + f"topdown/weapons/{name}.tres"
MAP["shaders/race_ground.gdshader"] = PREFIX + "shaders/race_ground.gdshader"
for name in ["bark.gdshader", "fabricated_metal163.gdshader", "ground.gdshader", "foliage.gdshader", "water.gdshader", "garden_triplanar.gdshader", "garden_color.gdshaderinc", "GODOT_MATERIAL_LICENSE.txt"]:
    MAP["shaders/woodland/" + name] = PREFIX + "shaders/" + name
for name in ["Forum-Regular.ttf", "Manrope-Variable.ttf", "Forum-OFL.txt", "Manrope-OFL.txt", "FONT_NOTICE.md"]:
    MAP["assets/presentation163/fonts/" + name] = "assets/reused/fonts/" + name


def read_blob(repo: Path, path: str) -> bytes:
    return subprocess.check_output(["git", "-C", str(repo), "show", f"{REVISION}:{path}"])


def transform(path: str, data: bytes) -> tuple[bytes, list[str]]:
    if path.endswith(".ttf"):
        return data, []
    text = data.decode("utf-8")
    changes = []
    for old, new in MAP.items():
        if "res://" + old in text:
            text = text.replace("res://" + old, "res://" + new)
            changes.append("relocate_resource_paths")
    # Portable class names must coexist with a consumer's own generic classes.
    classes = ["HealthComponent", "Hitbox3DComponent", "HitboxComponent", "WeaponData", "WeaponComponent", "Level3Player", "Level3Enemy", "Level3Projectile", "Level3Navigation", "Level3Door", "Level3Pickup", "Level3MapResource", "Level3TouchJoystick", "Level3AssetVisual", "ObjectPool", "RaceTrackData", "RaceCameraState", "RaceAiAssist", "AmmoMath", "HealthMath", "MovementMath", "TurnMath", "JoystickMath", "FireQuery", "PickupQuery", "DeathQuery", "MissionStateQuery", "MissionProgressQuery", "ConeQuery"]
    for name in classes:
        text, n = re.subn(r"\b" + name + r"\b", "Arkon" + name, text)
        if n:
            changes.append("namespace_classes")
    if "graphics" in path:
        for old, new in [("level1", "foot"), ("level2", "vehicle"), ("level3", "interior")]:
            text = text.replace(old, new)
        changes.append("replace_campaign_ids_with_render_contexts")
    if path == "scripts/audio_manager.gd":
        start = text.index("const SFX_PATHS: Dictionary = {")
        end = text.index("\n}", start) + 2
        text = text[:start] + "var SFX_PATHS: Dictionary = {}\n\nfunc configure_sfx(paths: Dictionary) -> void:\n    SFX_PATHS = paths.duplicate()\n    _load_sfx_streams()" + text[end:]
        text = text.replace('"level1_audio"', '"scene_audio_owner"')
        text = text.replace('&"squirrel_hit"', '&"impact_soft"')
        changes.append("inject_audio_catalog_remove_game_sfx")
    if path == "scripts/woodland_audio_surfaces.gd":
        text = text.replace('"level1_enemy"', '"runtime_hostile"').replace('"woodland_target"', '"runtime_target"')
        changes.append("replace_game_actor_groups")
    if path == "scripts/weapon_data.gd":
        text = re.sub(r'("res://assets/[^"\n]+")', '""', text)
        changes.append("remove_campaign_sprite_defaults")
    if "level3_" in path or path == "scripts/components/weapon_component.gd":
        text = text.replace("ArkonWeaponComponent", "ArkonLevel3WeaponComponent")
        visual_paths = {
            "scripts/level3_hero_visual.gd": PREFIX + "topdown/fixtures/hero_visual.gd",
            "scripts/level3_squirrel_animator.gd": PREFIX + "topdown/fixtures/actor_animator.gd",
            "scripts/level3_asset_visual.gd": PREFIX + "topdown/fixtures/asset_visual.gd",
        }
        for old, new in visual_paths.items():
            text = text.replace("res://" + old, "res://" + new)
        # Only presentation changes: do not transplant personal hero/squirrel art.
        text = text.replace('    var sling := preload("res://scripts/level3_conegun_sling.gd").new()\n    add_child(sling)\n', '')
        text = re.sub(r'res://assets/level3/[^"\n]+', "res://assets/fixtures/door.svg" if "door" in path else "res://assets/fixtures/projectile.svg", text)
        text = text.replace('display_name = "ШИШКОЛЁТ"', 'display_name = "ПИСТОЛЕТ"').replace('display_name = "ШИШКОВИК"', 'display_name = "ДРОБОВИК"')
        text = text.replace('Шишковик.', 'Дробовик.')
        changes.append("substitute_neutral_fixture_visuals_only")
        if path == "scenes/level3_enemy.tscn":
            text = text.replace('[sub_resource type="CircleShape2D" id="EnemyShape"]', '[ext_resource type="Script" path="res://addons/arkon_runtime/topdown/fixtures/enemy_visual.gd" id="4_fixture"]\n\n[sub_resource type="CircleShape2D" id="EnemyShape"]')
            text = text.replace('[node name="WeaponVisual"', 'script = ExtResource("4_fixture")\n\n[node name="WeaponVisual"')
    if path == "scripts/mission_progress_query.gd":
        text = text.replace("acorn_count", "goal_count")
        changes.append("neutral_objective_parameter")
    if path == "scripts/race_level_data.gd":
        text = text.replace('# Level 2 — squirrel pursuit.', '# Release vehicle calibration fixture; NOT the White Guard campaign.')
        text = text.replace('"ЛЕСНАЯ ОКРАИНА", "ЗОЛОТЫЕ ПОЛЯ", "ЛУННЫЙ МОСТ", "ЛЕСНОЙ СПУСК", "ПОДЗЕМНЫЙ ТОННЕЛЬ", "ОКРАИНЫ ГОРОДА", "ОГНИ ГОРОДА"', '"FIXTURE 0", "FIXTURE 1", "FIXTURE 2", "FIXTURE 3", "FIXTURE 4", "FIXTURE 5", "FIXTURE 6"')
        changes.append("mark_release_calibration_fixture")
    return text.encode(), sorted(set(changes))


WORLD_METHODS = ["has_line_of_sight", "_trace_weapon_shot", "_acquire_projectile", "_spawn_projectile_visual", "_on_player_fire_requested", "_perform_bat_attack", "_find_nearest_stunned_enemy", "_find_nearest_closed_door", "_find_throw_target", "_find_throw_endpoint", "_spawn_bottle_projectile", "_notify_noise", "_on_enemy_shot_requested", "_spawn_enemy_projectile", "is_body_path_clear", "safe_muzzle", "_on_bus_item_collected", "_on_pickup_collected", "_find_nearest_pickup"]


def extract_world(data: bytes) -> tuple[bytes, list[str]]:
    """Keep release combat methods; the consumer supplies map, UI and story."""
    source = data.decode()
    methods = dict(re.findall(r"^func (\w+)(\([^\n]*(?:\n(?!func ).*)*)", source, re.M))
    selected = "\n".join("func " + name + methods[name].rstrip() + "\n" for name in WORLD_METHODS)
    output, changes = transform("scripts/level3_store_selected.gd", selected.encode())
    # These fields/methods are the host interface, not replacement combat logic.
    wrapper = (ROOT / "tools/templates/topdown_world.gd.txt").read_text()
    return (wrapper + "\n" + output.decode()).encode(), sorted(set(changes + ["select_release_combat_methods", "inject_map_story_feedback_host"]))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    actual = subprocess.check_output(["git", "-C", str(args.source), "rev-parse", "v1.68"], text=True).strip()
    if actual != REVISION:
        raise SystemExit("The v1.68 tag does not match the pinned release commit")
    records = []
    entries = [(old, new, None) for old, new in sorted(MAP.items())]
    entries.append(("scripts/level3_store.gd", PREFIX + "topdown/world.gd", extract_world))
    for old, new, special in entries:
        original = read_blob(args.source, old)
        output, changes = special(original) if special else transform(old, original)
        destination = ROOT / new
        if args.verify:
            if not destination.is_file() or destination.read_bytes() != output:
                raise SystemExit(f"Extraction drift: {new}")
        else:
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_bytes(output)
        records.append({"source": old, "target": new, "source_sha256": hashlib.sha256(original).hexdigest(), "target_sha256": hashlib.sha256(output).hexdigest(), "bytes": len(output), "transformations": changes, "origin": "Arkon release v1.68"})
    manifest = {"schema": 1, "repository": "https://github.com/masterofmagic234/Arkon", "release": "v1.68", "source_commit": REVISION, "selection": "Portable systems plus Level 3 top-down logic with neutral fixture visuals; personal media and old story excluded", "files": records}
    manifest_path = ROOT / "provenance/runtime-extraction.json"
    if args.verify:
        if json.loads(manifest_path.read_text()) != manifest:
            raise SystemExit("Provenance manifest drift")
    else:
        manifest_path.parent.mkdir(parents=True, exist_ok=True)
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    print(f"{'VERIFIED' if args.verify else 'EXTRACTED'} {len(records)} files from {REVISION}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
