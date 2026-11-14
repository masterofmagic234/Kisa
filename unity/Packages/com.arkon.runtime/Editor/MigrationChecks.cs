using System;
using System.IO;
using System.Collections.Generic;
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
            var stage = IntroStageJson.Parse(stageFile.text);
            Require(IntroDataValidation.Validate(book, stage, out var error), error);
            Require(book.blocks.Length == 12, "Narration paragraph count changed.");
            Require(Resources.Load<Font>("WhiteGuardIntro/fonts/Manrope-Variable"), "The preserved Manrope font was not imported.");
            Require(Resources.Load<Font>("WhiteGuardIntro/fonts/Forum-Regular"), "The preserved Forum font was not imported.");
            foreach (var block in book.blocks)
            {
                var relative = block.audio.Substring("res://assets/intro/".Length);
                var clip = Resources.Load<AudioClip>("WhiteGuardIntro/" + relative.Substring(0, relative.LastIndexOf('.')));
                Require(clip, "Missing MP3 import: " + relative);
                Require(Math.Abs(clip.length - block.duration) < .18, "Decoded duration changed: " + relative);
            }
            if (stage.schema == 3)
            {
                var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
                foreach (var art in stage.Stage3.Art.Values)
                {
                    if (!textures.TryGetValue(art.Asset, out var texture))
                    { texture = VerifyIllustrationTexture(art.Asset); textures.Add(art.Asset, texture); }
                    Require(art.Source.X >= 0 && art.Source.Y >= 0 && art.Source.Width > 0 && art.Source.Height > 0 &&
                        art.Source.X + art.Source.Width <= texture.width + .001 &&
                        art.Source.Y + art.Source.Height <= texture.height + .001,
                        "Authored atlas region exceeds its unchanged texture: " + art.Id);
                }
                Require(stage.Stage3.Art.Count > 0 && textures.Count > 0, "No schema3 illustration regions were checked.");
            }
            else
            {
                var paths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var shot in stage.shots)
                {
                    paths.Add(shot.asset);
                    foreach (var layer in shot.layers ?? Array.Empty<IntroLayerData>()) paths.Add(layer.asset);
                }
                foreach (var path in paths) VerifyIllustrationTexture(path);
            }
            Debug.Log("UNITY INTRO IMPORT CHECK PASSED. Playback and visual parity still need PlayMode review.");
        }

        private static Texture2D VerifyIllustrationTexture(string sourcePath)
        {
            const string prefix = "res://assets/intro/";
            Require(sourcePath != null && sourcePath.StartsWith(prefix, StringComparison.Ordinal),
                "Illustration reference is outside the preserved intro directory.");
            var relative = sourcePath.Substring(prefix.Length);
            var key = "WhiteGuardIntro/" + relative.Substring(0, relative.LastIndexOf('.'));
            var texture = Resources.Load<Texture2D>(key);
            Require(texture, "Missing illustration texture: " + relative);
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            Require(importer != null, "The illustration does not have a TextureImporter: " + relative);
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            Require(texture.width == width && texture.height == height,
                "An illustration was resized during import: " + relative);
            Require(!importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed && importer.sRGBTexture,
                "Illustration import must preserve sRGB pixels without mipmaps or lossy compression: " + relative);
            return texture;
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
