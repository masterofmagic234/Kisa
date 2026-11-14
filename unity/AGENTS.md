# Unity migration scope

User explicitly requested BOTH the complete Acorn Hunter game and the White
Guard foundation on Unity. This authorizes migration work beyond the earlier
intro-only Godot assignment, not inventing new White Guard campaign levels.

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
