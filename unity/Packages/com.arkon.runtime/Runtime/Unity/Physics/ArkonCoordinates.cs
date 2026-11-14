using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    public static class ArkonCoordinates
    {
        public const float PixelsPerUnit = 100;
        public static Vector2 ToUnity(Vec2 source) => new Vector2(source.X, -source.Y) / PixelsPerUnit;
        public static Vec2 ToSource(Vector2 world) => new Vec2(world.x * PixelsPerUnit, -world.y * PixelsPerUnit);
        public static float ToUnityAngle(float radians) => -radians * Mathf.Rad2Deg;
        public const int WallsLayer = 8, BodiesLayer = 9, EnemyHitboxLayer = 10, PlayerHitboxLayer = 11;
        public static int ToUnityMask(int source)
        {
            var mask = 0;
            if ((source & SourceCollisionMasks.Walls) != 0) mask |= 1 << WallsLayer;
            if ((source & SourceCollisionMasks.Bodies) != 0) mask |= 1 << BodiesLayer;
            if ((source & SourceCollisionMasks.EnemyHitboxes) != 0) mask |= 1 << EnemyHitboxLayer;
            if ((source & SourceCollisionMasks.PlayerHitboxes) != 0) mask |= 1 << PlayerHitboxLayer;
            return mask;
        }
    }
}
