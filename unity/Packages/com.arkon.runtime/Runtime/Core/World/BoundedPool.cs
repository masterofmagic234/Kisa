using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    /// <summary>Scene-owned LIFO pool port of topdown/object_pool.gd; lifetime/disposal remains with its owner.</summary>
    public sealed class BoundedPool<T> where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _activate, _deactivate;
        private readonly Stack<T> _available = new Stack<T>();
        private readonly List<T> _active = new List<T>();
        private readonly int _maximum;
        public int Capacity { get; private set; }
        public int ActiveCount => _active.Count;
        public int AvailableCount => _available.Count;

        public BoundedPool(Func<T> factory, int initialCapacity = 24, int maxCapacity = 64, Action<T> activate = null, Action<T> deactivate = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _activate = activate; _deactivate = deactivate;
            Capacity = Math.Max(0, initialCapacity);
            _maximum = maxCapacity > 0 ? Math.Max(maxCapacity, Capacity) : 0;
            // As in the source, Capacity includes the requested warmup slots even if a factory returns null.
            for (var i = 0; i < Capacity; i++)
            {
                var item = _factory();
                if (item == null) continue;
                _deactivate?.Invoke(item); _available.Push(item);
            }
        }

        public T Acquire()
        {
            T item;
            if (_available.Count > 0) item = _available.Pop();
            else
            {
                if (_maximum > 0 && Capacity >= _maximum) return null;
                item = _factory();
                if (item != null) Capacity++;
            }
            if (item == null) return null;
            _activate?.Invoke(item); _active.Add(item); return item;
        }

        public bool Release(T item)
        {
            if (item == null) return false;
            var index = _active.FindIndex(value => ReferenceEquals(value, item));
            if (index < 0) return false;
            _active.RemoveAt(index); _deactivate?.Invoke(item); _available.Push(item); return true;
        }

        public void ReleaseAll()
        {
            foreach (var item in _active.ToArray()) Release(item);
        }
    }
}
