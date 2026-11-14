# Level 3 Unity adapter wiring

These components use real Unity 2D APIs and the source-space core models.
They do not generate levels, replace artwork, own campaign facts, or implement
native SceneFlow/loading. The level owner supplies authored geometry and the
`Transitioning`, `CombatPaused`, and `LevelComplete` signals.

## Scene and player

Add `TopdownWorld2D` to one active object in the authored scene. Its required
`Physics2DPort` and `ProjectilePool2D` are scene-owned. Assign the active
`TopdownActor2D` target, or call `BindTarget` after the actor's Awake. Set the
actual map size in source pixels, then call `BuildNavigation`; alternatively set
`BuildNavigationOnStart` with those bounds. No sample map is made automatically.

World binding supplies live transition and shot-spawn predicates, not a cached
Update flag. A transition stops player physics before timers/movement/fire;
shot admission is checked after weapon consumption, as in the source host.
The world also binds action/throw/melee and the player's post-move collision
contacts. Running into the first collided door at at least 70% of walking speed
requests a slam. Ordinary E-door interaction remains available while unarmed.
The source gameplay fixed tick is 1/60 second; configure the Unity project to
that rate when comparing behaviour.

Source positions are pixels, y down; Unity positions are converted by
`ArkonCoordinates` at 100 pixels/unit. Keep level roots at the imported origin
with unit scale. Queries filter out other loaded scenes. They currently use
Unity's default PhysicsScene2D; separate LocalPhysicsMode.Physics2D scenes need
an explicit query-provider change.

The player keeps its root body collider on Bodies layer9. Binding creates a
missing child PlayerHitbox: trigger circle radius7 pixels, PlayerHitbox layer11,
with the parent's existing DamageReceiver. It reuses an already authored
PlayerHitbox collider rather than duplicating it.

## Enemy

Attach `EnemyPort2D`; it requires `KinematicBodyPort2D` and `ActorIdentity2D`.
Assign its scene world, source enemy kind and exported source phase slots.
Default speeds remain77/100/93 pixels/s according to kind. Vision/distance/range
fields retain source defaults340/170/39/48 and can be authored on the prefab.
Use `ConfigureRaidDuty`, `Stage`, and `BeginAssault` from the level owner;
duty points wait for navigation readiness before snapping.

Without an assigned hitbox receiver the component creates an INACTIVE child,
adds the trigger circle and DamageReceiver, sets1HP/0invulnerability, and only
then activates it. This avoids Awake constructing the generic100HP/.24s model.
A supplied receiver must already be a correctly configured child and have a
circle collider. Root body remains layer9, hitbox layer10. Death disables both
colliders once; `Killed` and `Stunned` callbacks let the original sprite animator
supply the authored reactions. The adapter does not fabricate animations.

The model receives actual body position before each decision tick and the
collision-projected velocity after movement. Enemy bullets have source speed380,
range900, direct damage8 and an empty damage callback. Player bullets retain
callback-owned damage and a volley cache in the existing actor adapter.

## Door

Attach `DoorPort2D`. An unassigned collision hierarchy is created from the source
prefab: leaf hinge at(-16,0) pixels, solid rectangle32×5 centred16 pixels beyond
the hinge, separate trigger rectangle36×14 at the same centre. Put the original
leaf sprite under that pivot; the adapter does not create artwork. A supplied
solid collider must be on the pivot, and a supplied slam area on a separate
child. The slam area has no source raycast layer and is excluded from wall rays.

The leaf follows DoorModel's12/30/7.5rad/s rates and3s auto-close. Re-enabling
solidity queries the AUTHORED CLOSED rectangle against actor bodies, not last
tick's moving pose. A blocked doorway reopens. Slam overlap queries affect
living enemies once each:2.6s stun and170px/s knockback. Interaction-side choice
uses the hinge, while knockback uses the door root, matching the source.

## Pickup and common combat

Attach `PickupPort2D`, assign the source kind and same scene world. Geometry is
only the source9px trigger. Weapons and bottle are manual; `ammo` becomes a
medkit. Automatic medkits check both enter and repeated overlaps every.12s,
refuse full health/dead/noncombat actors and require wall LOS. Consumption
applies35 healing without re-equipping a weapon or resetting its cooldown.

World action priority matches the fixture host: visible stunned enemy execution,
manual pickup, then door. Throw consumes the host's one bottle slot; a bottle
pickup restores it. The level/checkpoint owner must persist or restore that slot
and the player's inventory, rather than treating this component as a save game.
Weapon switching UI, mission waves, original sprite animation, audio/VFX binding,
checkpoint ownership and level orchestration remain separate migration work.

## Lifetime and verification boundary

Disabling the world cancels the sliced navigation coroutine, closes live player
queries and releases scene projectiles. Re-enabling rebinds the target and
restarts a previously requested navigation build. Enemy/door/pickup components
register and unregister with their active same-scene world; event subscriptions
are removed during teardown. No global singleton or cross-scene actor lookup is
used. All native collider/sweep/import behaviour still needs Unity PlayMode.

Before considering this gameplay verified in Unity, exercise actual wall/door
movement, the84px/s slam threshold, an occupied auto-closing doorway, the medkit
standing-underfoot case, enemy windup/LOS at a wall corner, disable/enable during
navigation, and two additive scenes with different actors/worlds. Compiling
against the official6000.3.28f1 managed DLLs checks API use, not those behaviours.
