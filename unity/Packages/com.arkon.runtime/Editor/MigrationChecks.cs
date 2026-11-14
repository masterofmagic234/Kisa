using System;
using System.IO;
using Arkon.Core;
using Arkon.Unity.Intro;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Arkon.Unity.Editor
{
    /// <summary>Commands to run in the real, activated Editor. Not run by dotnet parity tests.</summary>
    public static class MigrationChecks
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new BuildFailedException(message);
        }

        [MenuItem("Arkon/Migration/Verify White Guard Imports")]
        public static void VerifyIntroImports()
        {
            var bookFile = Resources.Load<TextAsset>("WhiteGuardIntro/intro");
            var stageFile = Resources.Load<TextAsset>("WhiteGuardIntro/intro_shots");
            Require(bookFile && stageFile, "The unchanged intro JSON was not imported.");
            var book = JsonUtility.FromJson<IntroBookData>(bookFile.text);
            var stage = JsonUtility.FromJson<IntroStageData>(IntroJsonDefaults.WithLayerAlpha(stageFile.text));
            Require(IntroDataValidation.Validate(book, stage, out var error), error);
            Require(book.blocks.Length == 12, "Narration paragraph count changed.");
            foreach (var block in book.blocks)
            {
                var relative = block.audio.Substring("res://assets/intro/".Length);
                var clip = Resources.Load<AudioClip>("WhiteGuardIntro/" + relative.Substring(0, relative.LastIndexOf('.')));
                Require(clip, "Missing MP3 import: " + relative);
                Require(Math.Abs(clip.length - block.duration) < .18, "Decoded duration changed: " + relative);
            }
            foreach (var name in new[] { "opening-atlas-a", "opening-atlas-b", "opening-atlas-c", "opening-atlas-d", "opening-atlas-e", "opening-atlas-f", "stages", "actors" })
            {
                var texture = Resources.Load<Texture2D>("WhiteGuardIntro/" + name);
                Require(texture && texture.width >= 1024, "Missing or downscaled original atlas: " + name);
            }
            Debug.Log("UNITY INTRO IMPORT CHECK PASSED. Playback and visual parity still need PlayMode review.");
        }

        [MenuItem("Arkon/Migration/Verify Native 2D Projectile Queries")]
        public static void VerifyNativePhysics()
        {
            var previous = Physics2D.simulationMode;
            GameObject owner = null, wall = null, hitbox = null, portObject = null;
            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                portObject = new GameObject("Migration physics query test");
                var world = portObject.AddComponent<Physics2DPort>();
                owner = new GameObject("Shooter"); var identity = owner.AddComponent<ActorIdentity2D>();
                owner.layer = ArkonCoordinates.PlayerHitboxLayer;
                owner.AddComponent<CircleCollider2D>().radius = .07f;
                hitbox = new GameObject("Child hitbox"); hitbox.transform.SetParent(owner.transform, false);
                hitbox.layer = ArkonCoordinates.PlayerHitboxLayer;
                hitbox.AddComponent<CircleCollider2D>().radius = .08f;
                wall = new GameObject("Thin wall"); wall.transform.position = new Vector3(.10f, 0, 0);
                wall.layer = ArkonCoordinates.WallsLayer;
                wall.AddComponent<BoxCollider2D>().size = new Vector2(.01f, .3f);
                Physics2D.SyncTransforms();
                Require(world.Sweep(new Vec2(0, 0), new Vec2(30, 0), SourceCollisionMasks.Walls | SourceCollisionMasks.PlayerHitboxes,
                    identity.Id, out var hit), "A projectile crossed a thin wall.");
                Require(hit.ColliderId == wall.GetComponent<Collider2D>().GetInstanceID(), "Shooter or child hitbox was not excluded.");
                Require(!hit.DamageableHitbox && hit.Position.X < 11, "Wall collision mapping is incorrect.");
                var impacts = 0; var recycles = 0; var bullet = new ProjectileModel();
                bullet.Setup(Vec2.Zero, new Vec2(100, 0), 650, _ => impacts++, true, SourceCollisionMasks.Walls,
                    identity.Id, 0, _ => recycles++);
                bullet.Tick(.1f, world); bullet.Tick(.1f, world);
                Require(impacts == 1 && recycles == 1, "Native swept impact was duplicated.");
                Debug.Log("UNITY NATIVE PHYSICS QUERY CHECK PASSED. Full movement/AI/door PlayMode parity remains separate.");
            }
            finally
            {
                if (hitbox) UnityEngine.Object.DestroyImmediate(hitbox);
                if (owner) UnityEngine.Object.DestroyImmediate(owner);
                if (wall) UnityEngine.Object.DestroyImmediate(wall);
                if (portObject) UnityEngine.Object.DestroyImmediate(portObject);
                Physics2D.simulationMode = previous;
            }
        }

        public static void BuildLinuxIntro()
        {
            VerifyIntroImports(); VerifyNativePhysics();
            var target = Environment.GetEnvironmentVariable("ARKON_UNITY_BUILD") ?? "Builds/Linux/WhiteGuard";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target)));
            var result = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Opening.unity" }, target,
                BuildTarget.StandaloneLinux64, BuildOptions.Development);
            Require(result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, "Unity build failed.");
        }
    }
}
