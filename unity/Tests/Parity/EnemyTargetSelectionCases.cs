using System;
using System.Collections.Generic;
using Arkon.Core;

namespace Arkon.Parity
{
    /// <summary>New encounter selection policy, separate from the original single-player Godot parity oracle.</summary>
    public static class EnemyTargetSelectionCases
    {
        public static void Run(Action<bool, string> check)
        {
            var player = new EnemyTargetCandidate(11, new Vec2(80, 0));
            var escort = new EnemyTargetCandidate(22, new Vec2(30, 0));
            var candidates = new[] { player, escort, new EnemyTargetCandidate(33, new Vec2(1, 0), false) };
            check(EnemyTargetSelection.Select(Vec2.Zero, candidates, (_, __) => true, 11)?.ActorId == 22,
                "Enemy acquisition chooses a nearer live escort instead of always choosing the player");
            check(EnemyTargetSelection.Select(Vec2.Zero, candidates, (_, to) => to.X > 50, 11)?.ActorId == 11,
                "A visible player outranks a nearer escort behind a wall");
            check(EnemyTargetSelection.Select(Vec2.Zero, candidates, (_, __) => false, 11)?.ActorId == 11,
                "With no visible threats the explicit living player fallback remains the pursuit target");
            check(EnemyTargetSelection.Select(Vec2.Zero, candidates, (_, __) => false)?.ActorId == 22,
                "Without a living fallback the nearest living target remains selectable for pursuit");
            check(EnemyTargetSelection.Select(Vec2.Zero, new[] { new EnemyTargetCandidate(11, player.Position, false), escort },
                (_, __) => false, 11)?.ActorId == 22, "A dead fallback never shields a living escort from selection");
            check(EnemyTargetSelection.Select(Vec2.Zero, new[] { new EnemyTargetCandidate(0, Vec2.Zero),
                new EnemyTargetCandidate(44, new Vec2(float.NaN, 0)), new EnemyTargetCandidate(55, Vec2.Zero, false) }, null) == null,
                "Invalid identities, nonfinite positions and dead actors are not valid targets");
            check(EnemyTargetSelection.Select(Vec2.Zero, new[] { new EnemyTargetCandidate(11, new Vec2(-30, 0)), escort }, null)?.ActorId == 11,
                "Equal-distance targets retain deterministic encounter input order");
            check(EnemyTargetSelection.Select(Vec2.Zero, Array.Empty<EnemyTargetCandidate>(), null) == null,
                "An empty encounter has no phantom player target");

            var world = new TargetWorld { TargetPosition = new Vec2(20, 0) };
            var enemy = new EnemyModel(7, Vec2.Zero, EnemyKind.Melee); var hits = 0;
            enemy.MeleeDamageRequested += _ => hits++;
            enemy.Tick(.5f, world);
            check(enemy.Windup > 0 && enemy.SeesTarget, "A close selected target starts the original melee windup");
            world.TargetPosition = new Vec2(0, 20);
            enemy.Tick(.35f, world);
            check(hits == 0 && Math.Abs(enemy.RotationRadians) < .0001f,
                "Moving a windup victim around the enemy does not redirect its locked source attack cone");

            var retargeted = new EnemyModel(8, Vec2.Zero, EnemyKind.Melee);
            world.TargetPosition = new Vec2(20, 0); retargeted.Tick(.5f, world);
            var cooldown = retargeted.AttackCooldown;
            retargeted.ResetTargetTracking();
            check(retargeted.Windup == 0 && !retargeted.SeesTarget && retargeted.AttackCooldown == cooldown,
                "Changing victim cancels the former windup and cached visibility without granting an instant replacement attack");
            world.TargetPosition = new Vec2(0, 20); retargeted.Tick(.001f, world);
            check(retargeted.SeesTarget && retargeted.LastKnownPosition.Equals(world.TargetPosition) && retargeted.Windup == 0,
                "A replacement victim is sensed immediately instead of inheriting the old victim's cached position");
            retargeted.ResetTargetTracking(); world.Visible = false; world.TargetPosition = new Vec2(100, 100);
            retargeted.Tick(.001f, world);
            check(!retargeted.SeesTarget && retargeted.LastKnownPosition.Equals(retargeted.Position) && retargeted.State == EnemyState.Idle,
                "An unseen replacement victim cannot inherit the previous victim's pursuit memory or reveal its new position");
        }

        private sealed class TargetWorld : IEnemyWorld
        {
            public bool Visible = true;
            public bool Transitioning => false;
            public bool CombatPaused => false;
            public bool NavigationReady => true;
            public bool TargetAlive => true;
            public Vec2 TargetPosition { get; set; }
            public bool HasLineOfSight(Vec2 from, Vec2 to) => Visible;
            public bool IsBodyPathClear(Vec2 from, Vec2 to) => true;
            public IReadOnlyList<Vec2> FindPath(Vec2 from, Vec2 to) => new[] { from, to };
            public Vec2 NearestNavigationPoint(Vec2 point) => point;
            public void OpenNearbyDoors(Vec2 at, float radius) { }
            public IReadOnlyList<EnemyNeighbour> Neighbours => Array.Empty<EnemyNeighbour>();
        }
    }
}
