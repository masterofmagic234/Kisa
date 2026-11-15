# Unity migration scope

Current goal (2026-11-15): the user explicitly requests the COMPLETE White Guard
top-down Unity book adaptation, beginning to ending, and authorizes quality new
assets. This supersedes the earlier intro-only/foundation scope. Build the
existing nine-episode narrative arc against the actual Bushkov novel; keep the
approved opening artwork and voice. Prior Acorn migration work is separate:
preserve it and reuse the shared core, without moving private game content here.

Current implementation branch is feat/white-guard-game, based on the newer
Unity migration commit 8a29893 (including native PC fixes). Never reset it to
the older handoff snapshot. Full novel source remains outside Git; original
upload SHA-256 d0667077fee4e2ffccc54f46c70348a9ae7f78971d1140dd495901eb6900c13d.

Keep the Godot games and published builds intact. Preserve the approved original
PNG drawings, 1018-word monologue and 12 MP3 files. The rejected vector art must
not be revived. An engine port does not approve or fix the existing direction.

Core source: Acorn v1.68 a1fd971558fcfe8d8fb99e944cc532edee5c32ba, publicly
extracted Godot addon in ../white_guard. Full Acorn content belongs in its PRIVATE
repository, never in public Kisa. No personal archives, VPN files, signing keys
or full private novel copies may enter this tree.

Use Unity 6000.3.28f1 and C# 9/.NET Standard2.1 for runtime models. Core must have
no UnityEngine references. Models use source pixels, y-down and radians; convert
only at explicit Unity ports. Layers are indices 8-11, source masks are bits 1/2/4/8.

Portable tests, official-API compilation, Editor import, PlayMode/native physics,
graphics and platform build are separate evidence. Do not call a .NET pass a
verified Unity game. Do not write mock UnityEngine classes to make builds pass.
Track unported original levels and unsupported save/AI details explicitly.
