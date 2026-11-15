using Arkon.Unity;
using WhiteGuard.Gameplay;
using UnityEngine;

namespace WhiteGuard.Presentation
{
    [RequireComponent(typeof(Camera))]
    public sealed class CampaignCamera : MonoBehaviour
    {
        public CampaignGameLoop Loop;
        private Camera view;
        private string levelId;
        private Vector3 velocity;
        private void Awake()
        {
            view = GetComponent<Camera>(); view.orthographic = true; view.orthographicSize = 3.5f;
            view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = new Color32(18, 23, 20, 255);
            transform.position = new Vector3(0, 0, -10);
        }
        private void LateUpdate()
        {
            if (!Loop || !Loop.Player || Loop.Level == null) return;
            var position = Loop.Player.transform.position;
            if (Loop.State == CampaignGameState.Playing)
            {
                var mouse = view.ScreenToWorldPoint(Input.mousePosition);
                var look = Vector2.ClampMagnitude((Vector2)(mouse - position) * .12f, .52f);
                position += new Vector3(look.x, look.y, 0);
            }
            var height = Mathf.Max(2.5f, Mathf.Min(3.5f, Loop.Level.height / 200));
            view.orthographicSize = height;
            var width = height * Mathf.Max(.1f, view.aspect);
            var mapWidth = Loop.Level.width / ArkonCoordinates.PixelsPerUnit;
            var mapHeight = Loop.Level.height / ArkonCoordinates.PixelsPerUnit;
            position.x = mapWidth <= width * 2 ? mapWidth * .5f : Mathf.Clamp(position.x, width, mapWidth - width);
            position.y = mapHeight <= height * 2 ? -mapHeight * .5f : Mathf.Clamp(position.y, -mapHeight + height, -height);
            position.z = -10;
            if (levelId != Loop.Level.id) { levelId = Loop.Level.id; transform.position = position; velocity = Vector3.zero; }
            else transform.position = Vector3.SmoothDamp(transform.position, position, ref velocity, .13f, 100, Time.unscaledDeltaTime);
        }
    }
}
