using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Arkon.Core
{
    public readonly struct PoolLease<T> where T : class
    {
        public readonly T Item;
        public readonly long Token;
        internal PoolLease(T item, long token) { Item = item; Token = token; }
        public bool Valid => Item != null;
    }

    // Scene disable/re-enable may happen inside a projectile impact callback.
    // Reusing the same object does not transfer an old callback to its new lease.
    public sealed class LeasePool<T> where T : class
    {
        private sealed class IdentityComparer : IEqualityComparer<T>
        {
            public bool Equals(T a, T b) => ReferenceEquals(a, b);
            public int GetHashCode(T item) => RuntimeHelpers.GetHashCode(item);
        }
        private readonly BoundedPool<T> pool;
        private readonly Dictionary<T, long> current = new Dictionary<T, long>(new IdentityComparer());
        private long generation;
        public int ActiveCount => current.Count;
        public LeasePool(Func<T> factory, int initialCapacity = 24, int maxCapacity = 64)
        { pool = new BoundedPool<T>(factory, initialCapacity, maxCapacity); }
        public PoolLease<T> Acquire()
        {
            var item = pool.Acquire();
            if (item == null) return default;
            var lease = new PoolLease<T>(item, ++generation); current[item] = lease.Token; return lease;
        }
        public bool IsCurrent(PoolLease<T> lease)
            => lease.Item != null && current.TryGetValue(lease.Item, out var token) && token == lease.Token;
        public bool Release(PoolLease<T> lease)
        {
            if (!IsCurrent(lease)) return false;
            current.Remove(lease.Item); return pool.Release(lease.Item);
        }
        public void ReleaseAll() { current.Clear(); pool.ReleaseAll(); }
        public void Snapshot(List<PoolLease<T>> buffer)
        {
            buffer.Clear();
            foreach (var pair in current) buffer.Add(new PoolLease<T>(pair.Key, pair.Value));
        }
    }
}
