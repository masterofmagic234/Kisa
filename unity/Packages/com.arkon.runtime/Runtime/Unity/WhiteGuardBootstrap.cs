using Arkon.Unity.Intro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arkon.Unity
{
    public sealed class WhiteGuardBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            Application.targetFrameRate = 60;
            var sceneCamera = SceneObjects.Find<Camera>(this);
            if (!sceneCamera)
            {
                var cameraObject = new GameObject("Intro Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, gameObject.scene);
                cameraObject.transform.SetParent(transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
                camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, 0, -10);
                sceneCamera = camera;
            }
            if (!SceneObjects.Find<AudioListener>(this)) sceneCamera.gameObject.AddComponent<AudioListener>();
            if (!GetComponent<IntroPlayer>()) gameObject.AddComponent<IntroPlayer>();
        }
    }
}
