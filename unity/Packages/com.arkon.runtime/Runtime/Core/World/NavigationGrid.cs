using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    public readonly struct GridCell : IEquatable<GridCell>
    {
        public readonly int X;
        public readonly int Y;
        public GridCell(int x, int y) { X = x; Y = y; }
        public bool Equals(GridCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCell other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
    }

    /// <summary>The engine must sample only static wall bodies, excluding doors and actors.</summary>
    public interface INavigationSampler
    {
        bool IsBlocked(Vec2 center, float radius);
    }

    /// <summary>
    /// Policy port of topdown/level3_navigation.gd. Units remain Godot pixels.
    /// Unity owns frame/physics yielding and lifetime cancellation between SampleRows calls.
    /// A* uses stable cardinal neighbours; equal-cost tie paths need not match Godot internals.
    /// </summary>
    public sealed class NavigationGrid
    {
        public const float Step = 12f;
        public const float SamplingRadius = 8f;
        private bool[] _solid = Array.Empty<bool>();
        private int _nextRow;
        public int MinX { get; private set; }
        public int MinY { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Generation { get; private set; }
        public int Samples { get; private set; }
        public int Walkable { get; private set; }
        public bool Ready { get; private set; }

        public int BeginBuild(Vec2 worldSize)
        {
            if (!Scalar.IsFinite(worldSize.X) || !Scalar.IsFinite(worldSize.Y) || worldSize.X < 0 || worldSize.Y < 0)
                throw new ArgumentOutOfRangeException(nameof(worldSize));
            return BeginBuild(-12, -12, checked((int)Math.Ceiling(worldSize.X / Step) + 24), checked((int)Math.Ceiling(worldSize.Y / Step) + 32));
        }

        public int BeginBuild(int minX, int minY, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            var solid = new bool[checked(width * height)];
            MinX = minX; MinY = minY; Width = width; Height = height;
            _solid = solid; _nextRow = minY; Samples = 0; Walkable = 0; Ready = false;
            Generation++;
            return Generation;
        }

        /// <returns>True only when this generation has finished sampling.</returns>
        public bool SampleRows(int generation, int rowsPerSlice, INavigationSampler sampler)
        {
            if (generation != Generation) return false;
            if (Ready) return true;
            if (sampler == null) throw new ArgumentNullException(nameof(sampler));
            var end = Math.Min(MinY + Height, _nextRow + Math.Max(1, rowsPerSlice));
            for (; _nextRow < end; _nextRow++)
                for (var x = MinX; x < MinX + Width; x++)
                {
                    var cell = new GridCell(x, _nextRow);
                    var blocked = sampler.IsBlocked(CellCenter(cell), SamplingRadius);
                    _solid[Index(cell)] = blocked; Samples++;
                    if (!blocked) Walkable++;
                }
            Ready = _nextRow == MinY + Height;
            return Ready;
        }

        public void Build(Vec2 worldSize, INavigationSampler sampler)
        {
            var generation = BeginBuild(worldSize);
            SampleRows(generation, Height, sampler);
        }

        public bool Contains(GridCell cell) => cell.X >= MinX && cell.Y >= MinY && cell.X < MinX + Width && cell.Y < MinY + Height;
        public bool IsSolid(GridCell cell) => !Contains(cell) || _solid[Index(cell)];
        public Vec2 CellCenter(GridCell cell) => new Vec2(cell.X * Step + Step * .5f, cell.Y * Step + Step * .5f);

        public GridCell NearestCell(Vec2 point)
        {
            if (Width == 0 || Height == 0) throw new InvalidOperationException("Navigation has not been prepared.");
            var id = new GridCell(Math.Max(MinX, Math.Min(MinX + Width - 1, (int)Math.Floor(point.X / Step))),
                                  Math.Max(MinY, Math.Min(MinY + Height - 1, (int)Math.Floor(point.Y / Step))));
            if (!IsSolid(id)) return id;
            // Preserve source order: square scan, y then x, including inner cells.
            for (var radius = 1; radius <= 6; radius++)
                for (var y = -radius; y <= radius; y++)
                    for (var x = -radius; x <= radius; x++)
                    {
                        var test = new GridCell(id.X + x, id.Y + y);
                        if (Contains(test) && !IsSolid(test)) return test;
                    }
            return id;
        }

        public IReadOnlyList<Vec2> FindPath(Vec2 from, Vec2 to)
        {
            if (!Ready) return Array.Empty<Vec2>();
            var start = NearestCell(from); var goal = NearestCell(to);
            if (IsSolid(start) || IsSolid(goal)) return Array.Empty<Vec2>();
            var count = _solid.Length; var cost = new float[count]; var parent = new int[count]; var closed = new bool[count];
            for (var i = 0; i < count; i++) { cost[i] = float.PositiveInfinity; parent[i] = -1; }
            var first = Index(start); var last = Index(goal); cost[first] = 0;
            var queue = new MinHeap(); queue.Push(first, Heuristic(start, goal));
            var dx = new[] { 1, 0, -1, 0 }; var dy = new[] { 0, 1, 0, -1 };
            while (queue.Count > 0)
            {
                var index = queue.Pop();
                if (closed[index]) continue;
                if (index == last)
                {
                    var route = new List<Vec2>();
                    for (var at = last; at >= 0; at = parent[at]) route.Add(CellCenter(Cell(at)));
                    route.Reverse(); return route;
                }
                closed[index] = true; var cell = Cell(index);
                for (var direction = 0; direction < 4; direction++)
                {
                    var next = new GridCell(cell.X + dx[direction], cell.Y + dy[direction]);
                    if (IsSolid(next)) continue;
                    var nextIndex = Index(next); var candidate = cost[index] + Step;
                    if (closed[nextIndex] || candidate >= cost[nextIndex]) continue;
                    cost[nextIndex] = candidate; parent[nextIndex] = index;
                    queue.Push(nextIndex, candidate + Heuristic(next, goal));
                }
            }
            return Array.Empty<Vec2>();
        }

        private int Index(GridCell cell) => (cell.Y - MinY) * Width + cell.X - MinX;
        private GridCell Cell(int index) => new GridCell(MinX + index % Width, MinY + index / Width);
        private static float Heuristic(GridCell a, GridCell b)
        {
            var dx = (float)a.X - b.X; var dy = (float)a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy) * Step;
        }

        private sealed class MinHeap
        {
            private readonly List<Entry> _items = new List<Entry>();
            private int _sequence;
            public int Count => _items.Count;
            private readonly struct Entry
            {
                public readonly int Index, Order;
                public readonly float Priority;
                public Entry(int index, float priority, int order) { Index = index; Priority = priority; Order = order; }
            }
            private static bool Before(Entry a, Entry b) => a.Priority < b.Priority || (a.Priority == b.Priority && a.Order < b.Order);
            public void Push(int index, float priority)
            {
                var entry = new Entry(index, priority, _sequence++); _items.Add(entry); var at = _items.Count - 1;
                while (at > 0)
                {
                    var parent = (at - 1) / 2;
                    if (!Before(entry, _items[parent])) break;
                    _items[at] = _items[parent]; at = parent;
                }
                _items[at] = entry;
            }
            public int Pop()
            {
                var result = _items[0].Index; var tail = _items[_items.Count - 1]; _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return result;
                var at = 0;
                while (at * 2 + 1 < _items.Count)
                {
                    var child = at * 2 + 1;
                    if (child + 1 < _items.Count && Before(_items[child + 1], _items[child])) child++;
                    if (!Before(_items[child], tail)) break;
                    _items[at] = _items[child]; at = child;
                }
                _items[at] = tail; return result;
            }
        }
    }
}
