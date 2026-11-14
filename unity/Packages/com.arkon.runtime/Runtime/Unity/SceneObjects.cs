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
                foreach (var component in root.GetComponentsInChildren<T>(false))
                {
                    if (!component || !component.gameObject.activeInHierarchy) continue;
                    if (component is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
                    return component;
                }
            }
            return null;
        }
    }
}
