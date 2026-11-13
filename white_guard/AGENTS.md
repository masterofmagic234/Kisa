# White Guard foundation

This folder is a separate Godot project. Existing `../ps5-hfr1/` and the PS5
report are unrelated user work and must not be moved or modified for this game.

The user asked for engine extraction, a book adaptation plan and an implementation
report, then a pause for their next goal. The user subsequently chose Level 3 top-down 2D gameplay as the main format
and simple illustrated lore scenes inspired by This Is the Police. Preserve
the released Level 3 mechanics; do not redesign the project as a main FPS.
Two gameplay exceptions are requested but not assigned. Story production beyond
the authorized intro is pending discussion. Do not silently mark campaign entries approved or playable.

Latest user scope: leave the wider game alone. Produce ONLY the illustrated
opening now; convoy gameplay is next and is not authorized for implementation
in this change. Narration is Mazur's first-person interior monologue from the
book, shortened only slightly. Intro production is authorized; other campaign
entries remain proposals. Do not substitute character dialogue for his thoughts.

Latest correction: visual storytelling must carry the opening. Keep the current
slightly shortened monologue/voice intact unless the user authorizes another
text edit. Stage concrete actions, cut inside paragraphs and use independent
layers; do not revert to paragraph-long slides or a large default text panel.

Latest explicit correction: the user says the ORIGINAL PNG DRAWING STYLE WAS
ALREADY IDEAL. The objection was ONLY direction and the bad car movement.
The procedural/vector redesign was a misunderstanding and is rejected. Keep
all original PNG illustrations and the established character/colour design.
The active PNG runtime/timeline is restored from commit
8207adbe7fddb7f6166f68a0300783e1f00b62c7. This restores the drawing, not an
approved direction: the old sliding-car animation still needs replacement.
Do not regenerate or replace the whole art style, restore the rejected vector
renderer, shorten the monologue, or call test passes artistic approval.

Current assignment ends with restoration and handoff. Read
`docs/HANDOFF_INTRO_DIRECTION.md` and `docs/NEXT_CHAT_PROMPT.txt` before further
production. Reference sources, exact status and remaining directing work are
recorded there. `docs/archive/` contains rejected historical proposals only.

Use `docs/ADAPTATION_PLAN.md` and `provenance/book-source.json` as the narrative
baseline. The novel is Alexander Bushkov's Piranha book, not Bulgakov's work.
Natalie is the president's niece. Preserve the late Tatiana reveal and the
embassy ending; keep uncertain political conclusions uncertain.

Runtime extraction is pinned to Arkon v1.68. Re-extract only through the tool and
keep source/target hashes and transformation records consistent. Adapted files
have a separate manifest. Never carry private archives, family media, signing
keys, full book text or old third-party music into this public repository.

Validation commands are in README. Changes to transition ownership, combat,
save/load or NPC interactions require meaningful behavioural checks. A headless
pass is not GPU, phone or PS5 performance evidence. Keep production claims
separate from the engineering fixture.
