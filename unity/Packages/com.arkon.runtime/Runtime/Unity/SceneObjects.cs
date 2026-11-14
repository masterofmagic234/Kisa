using UnityEngine;

namespace Arkon.Unity
{
    public static class SceneObjects
    {
        // Additive scene loading must not bind an actor to the outgoing scene's world.
        public static T Find<T>(Component owner) where T : Component
        {
            foreach (var root in owner.gameObject.scene.GetRootGameObjects())
            {
                var component = root.GetComponentInChildren<T>(true);
                if (component) return component;
            }
            return null;
        }
    }
}
