using UnityEngine;

namespace Arkon.Unity
{
    public sealed class ActorIdentity2D : MonoBehaviour
    {
        // Runtime identity only; save games never persist instance IDs.
        public int Id => GetInstanceID();
    }
}
