using System;
using System.Collections.Generic;

namespace Arkon.Core
{
    // Ownership policy extracted from SceneFlow; loading/readiness are engine ports.
    public sealed class TransitionGate
    {
        private readonly HashSet<string> routes;
        private int generation;
        public bool IsLoading { get; private set; }
        public string Current { get; private set; } = "";
        public string Pending { get; private set; } = "";
        public event Action<string> Completed;
        public TransitionGate(IEnumerable<string> knownRoutes) { routes = new HashSet<string>(knownRoutes); }
        public bool TryBegin(string route, out int token)
        {
            token = generation;
            if (IsLoading || route == null || !routes.Contains(route)) return false;
            IsLoading = true; Pending = route; token = ++generation; return true;
        }
        public bool Complete(int token)
        {
            if (!IsLoading || token != generation) return false;
            Current = Pending; Pending = ""; IsLoading = false; Completed?.Invoke(Current); return true;
        }
        public bool Cancel(int token)
        {
            if (!IsLoading || token != generation) return false;
            Pending = ""; IsLoading = false; ++generation; return true;
        }
    }
}
