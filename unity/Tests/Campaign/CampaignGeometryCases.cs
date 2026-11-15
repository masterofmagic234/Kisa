using System;
using System.Collections.Generic;
using Arkon.Core;
using WhiteGuard.Gameplay;

/// <summary>
/// Static authored-data checks using the actual 12px/radius8 navigation grid.
/// Analytic axis-aligned wall rectangles are a data witness, not a PhysX test:
/// dynamic actors, moving doors and their native recovery still need PlayMode.
/// </summary>
internal static class CampaignGeometryCases
{
    public static void Run(CampaignData campaign, Action<bool, string> check)
    {
        foreach (var level in campaign.levels)
        {
            var sampler = new Walls(level); var grid = new NavigationGrid();
            grid.Build(new Vec2(level.width, level.height), sampler);
            var start = grid.NearestCell(P(level.spawn)); var reachable = Flood(grid, start);
            check(!sampler.IsBlocked(P(level.spawn), 7) && !grid.IsSolid(start), level.id + ": player spawn has real body clearance");
            var knownActors = new Dictionary<string, CampaignActor>(StringComparer.Ordinal);
            foreach (var actor in level.actors) knownActors.Add(actor.id, actor);
            var knownGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var enemy in level.enemies) knownGroups.Add(enemy.group);
            var regions = new Dictionary<string, CampaignRect>(StringComparer.Ordinal);
            foreach (var region in level.regions) regions.Add(region.id, region);
            foreach (var actor in level.actors)
            {
                var radius = actor.role == "vehicle" ? 18 : 7;
                check(!sampler.IsBlocked(new Vec2(actor.x, actor.y), radius), level.id + ": actor is not spawned inside a solid wall: " + actor.id);
                check(reachable.Contains(grid.NearestCell(new Vec2(actor.x, actor.y))), level.id + ": actor navigation component reaches the player: " + actor.id);
                if (actor.role == "passenger")
                    check(knownActors.TryGetValue(actor.transport ?? "", out var transport) && transport.role == "vehicle",
                        level.id + ": passenger is attached to an existing vehicle: " + actor.id);
                if (actor.role != "vehicle") continue;
                var previous = new Vec2(actor.x, actor.y); var route = actor.route ?? Array.Empty<CampaignPoint>();
                check(route.Length > 0, level.id + ": escort vehicle has an authored route: " + actor.id);
                foreach (var point in route)
                {
                    var next = P(point);
                    // A 0.5px witness plus0.5px clearance reserve is conservative
                    // between samples for the moving 18px-radius vehicle centre.
                    var count = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(previous, next) * 2)); var clear = true;
                    for (var i = 0; i <= count; i++) if (sampler.IsBlocked(Vec2.Lerp(previous, next, (float)i / count), 18.5f)) { clear = false; break; }
                    check(clear, level.id + ": vehicle route segment clears its actual18px body: " + actor.id);
                    previous = next;
                }
            }
            foreach (var enemy in level.enemies)
            {
                check(!sampler.IsBlocked(new Vec2(enemy.x, enemy.y), 7) && reachable.Contains(grid.NearestCell(new Vec2(enemy.x, enemy.y))),
                    level.id + ": enemy has a free reachable spawn: " + enemy.id);
                if (!string.IsNullOrEmpty(enemy.activeAfterObjective))
                    check(Array.Exists(level.objectives, objective => objective.id == enemy.activeAfterObjective),
                        level.id + ": staged enemy activation references an actual objective: " + enemy.id);
            }
            foreach (var pickup in level.pickups)
                check(reachable.Contains(grid.NearestCell(new Vec2(pickup.x, pickup.y))), level.id + ": pickup belongs to the reachable map: " + pickup.id);
            foreach (var interaction in level.interactions)
            {
                var radius = interaction.radius > 0 ? interaction.radius : 38; var available = false;
                foreach (var cell in reachable)
                    if (Vec2.Distance(grid.CellCenter(cell), new Vec2(interaction.x, interaction.y)) <= radius) { available = true; break; }
                check(available, level.id + ": interaction has a reachable activation position: " + interaction.id);
                if (interaction.action == "rescue" || interaction.action == "carry")
                    check(knownActors.ContainsKey(interaction.target ?? ""), level.id + ": rescue/carry references an existing actor: " + interaction.id);
                if (interaction.action == "stand_down")
                    check(knownGroups.Contains(interaction.target ?? "") && regions.ContainsKey(interaction.region ?? ""),
                        level.id + ": stand-down references an existing enemy group and destination: " + interaction.id);
                if (interaction.action == "carry")
                {
                    // The player is manually steered, not following the AI12px
                    // grid. Refine this particular static witness to3px so a
                    // narrow but physically valid doorway is not a false failure.
                    const float refinement = 4;
                    var carrierGrid = new NavigationGrid();
                    carrierGrid.Build(new Vec2(level.width, level.height) * refinement,
                        new BodyRadius(sampler, CampaignActorDimensions.CarriedBodyRadius + .5f, refinement));
                    GridCell? activation = null; var nearest = float.PositiveInfinity;
                    for (var y = carrierGrid.MinY; y < carrierGrid.MinY + carrierGrid.Height; y++)
                        for (var x = carrierGrid.MinX; x < carrierGrid.MinX + carrierGrid.Width; x++)
                        {
                            var cell = new GridCell(x, y); if (carrierGrid.IsSolid(cell)) continue;
                            var at = carrierGrid.CellCenter(cell) / refinement;
                            if (!reachable.Contains(grid.NearestCell(at))) continue;
                            var distance = Vec2.Distance(at, new Vec2(interaction.x, interaction.y));
                            if (distance <= radius && distance < nearest) { activation = cell; nearest = distance; }
                        }
                    check(activation.HasValue, level.id + ": carry interaction has room for the enlarged carrier body");
                    if (activation.HasValue)
                    {
                        var carryingReachable = Flood(carrierGrid, activation.Value);
                        foreach (var objective in level.objectives)
                            if (objective.type == "escort" && objective.target == interaction.target)
                                check(regions.TryGetValue(objective.region, out var goal) && RegionReachable(goal, carrierGrid, carryingReachable, refinement),
                                    level.id + ": enlarged carrier can reach the patient's escort region: " + objective.id);
                    }
                }
            }
            foreach (var region in level.regions)
                check(RegionReachable(region, grid, reachable), level.id + ": region contains reachable navigation space: " + region.id);
            check(level.exit != null && RegionReachable(level.exit, grid, reachable), level.id + ": final exit is reachable");
            foreach (var objective in level.objectives)
            {
                if (objective.type != "escort") continue;
                check(!string.IsNullOrEmpty(objective.region), level.id + ": escort requires actual arrival rather than merely freeing: " + objective.id);
                if (!knownActors.TryGetValue(objective.target, out var actor) || actor.role != "vehicle" || actor.route == null || actor.route.Length == 0) continue;
                var last = actor.route[actor.route.Length - 1];
                check(regions.TryGetValue(objective.region, out var destination) && destination.Contains(last.x, last.y),
                    level.id + ": vehicle route really ends inside its escort region: " + objective.id);
            }
        }
    }

    private static bool RegionReachable(CampaignRect region, NavigationGrid grid, HashSet<GridCell> reachable, float coordinateScale = 1)
    {
        foreach (var cell in reachable)
        { var center = grid.CellCenter(cell) / coordinateScale; if (region.Contains(center.X, center.Y)) return true; }
        return false;
    }
    private static HashSet<GridCell> Flood(NavigationGrid grid, GridCell start)
    {
        var seen = new HashSet<GridCell>(); var pending = new Queue<GridCell>();
        if (grid.IsSolid(start)) return seen;
        seen.Add(start); pending.Enqueue(start);
        var dx = new[] { 1, 0, -1, 0 }; var dy = new[] { 0, 1, 0, -1 };
        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();
            for (var i = 0; i < 4; i++)
            {
                var next = new GridCell(cell.X + dx[i], cell.Y + dy[i]);
                if (!grid.IsSolid(next) && seen.Add(next)) pending.Enqueue(next);
            }
        }
        return seen;
    }
    private static Vec2 P(CampaignPoint point) => new Vec2(point.x, point.y);

    private sealed class Walls : INavigationSampler
    {
        private readonly List<CampaignRect> rectangles = new List<CampaignRect>();
        public Walls(CampaignLevel level)
        {
            rectangles.AddRange(level.walls);
            foreach (var prop in level.props) if (prop.solid) rectangles.Add(prop);
            rectangles.Add(new CampaignRect { x = -18, y = -18, w = 18, h = level.height + 36 });
            rectangles.Add(new CampaignRect { x = level.width, y = -18, w = 18, h = level.height + 36 });
            rectangles.Add(new CampaignRect { x = 0, y = -18, w = level.width, h = 18 });
            rectangles.Add(new CampaignRect { x = 0, y = level.height, w = level.width, h = 18 });
        }
        public bool IsBlocked(Vec2 center, float radius)
        {
            foreach (var rectangle in rectangles)
            {
                var dx = Math.Max(rectangle.x - center.X, Math.Max(0, center.X - (rectangle.x + rectangle.w)));
                var dy = Math.Max(rectangle.y - center.Y, Math.Max(0, center.Y - (rectangle.y + rectangle.h)));
                if (dx * dx + dy * dy <= radius * radius) return true;
            }
            return false;
        }
    }

    private sealed class BodyRadius : INavigationSampler
    {
        private readonly Walls walls;
        private readonly float radius;
        private readonly float coordinateScale;
        public BodyRadius(Walls walls, float radius, float coordinateScale = 1)
        { this.walls = walls; this.radius = radius; this.coordinateScale = coordinateScale; }
        public bool IsBlocked(Vec2 center, float ignored) => walls.IsBlocked(center / coordinateScale, radius);
    }
}
