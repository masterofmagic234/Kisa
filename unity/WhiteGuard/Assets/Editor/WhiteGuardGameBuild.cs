using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Arkon.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using WhiteGuard.Gameplay;

namespace WhiteGuard.Editor
{
    /// <summary>Real Editor commands. Compilation of these methods is not a native validation result.</summary>
    public static class WhiteGuardGameBuild
    {
        private const string GameScene = "Assets/Scenes/WhiteGuard.unity";
        private const string OpeningScene = "Assets/Scenes/Opening.unity";
        private static readonly string[] RequiredLevels = { "p00", "m01", "m02", "m03", "m04", "m05", "m06", "m07", "e00" };

        [Serializable] private sealed class ArtCatalog { public int schema = 0; public Atlas[] atlases = Array.Empty<Atlas>(); }
        [Serializable] private sealed class Atlas
        { public string resource = "", sha256 = ""; public int width = 0, height = 0; public Region[] entries = Array.Empty<Region>(); }
        [Serializable] private sealed class Region { public string id = ""; public int x = 0, y = 0, w = 0, h = 0; }
        [Serializable] private sealed class VoiceCatalog
        {
            public int schema = 0, totalCards = 0, generatedCards = 0;
            public string status = "", campaignSHA256 = "";
            public VoiceRecording[] cards = Array.Empty<VoiceRecording>();
        }
        [Serializable] private sealed class VoiceRecording
        {
            public string cardId = "", levelId = "", textSHA256 = "", audioSHA256 = "", resource = "";
            public double durationSeconds = 0;
            public int sampleRate = 0, channels = 0;
            public long bytes = 0;
        }
        [Serializable] private sealed class DirectionCatalog { public int schema = 0; public DirectedShot[] shots = Array.Empty<DirectedShot>(); }
        [Serializable] private sealed class DirectedShot { public string cardId = ""; public DirectedBeat[] beats = Array.Empty<DirectedBeat>(); }
        [Serializable] private sealed class DirectedBeat
        {
            public float at = 0;
            public float[] view = Array.Empty<float>(), inset = Array.Empty<float>();
            public string art = "", insetArt = "";
            public int quadrant = 0;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new BuildFailedException(message); }

        [MenuItem("White Guard/Validate Campaign Data and Art Imports")]
        public static void ValidateData()
        {
            var campaignAsset = Resources.Load<TextAsset>("WhiteGuardCampaign/campaign");
            var sourceAsset = Resources.Load<TextAsset>("WhiteGuardCampaign/campaign-source");
            var artAsset = Resources.Load<TextAsset>("WhiteGuardArt/art-manifest");
            Require(campaignAsset && sourceAsset && artAsset, "Campaign, provenance or art manifest was not imported.");
            // Reject malformed JSON/duplicate keys before JsonUtility creates typed DTOs.
            PortableJson.Parse(campaignAsset.text); PortableJson.Parse(artAsset.text);
            var provenance = PortableJson.Parse(sourceAsset.text) as Dictionary<string, object>;
            Require(provenance != null && provenance.TryGetValue("schema", out var schema) && schema is double n && n == 1,
                "Unsupported campaign provenance schema.");
            Require(provenance.TryGetValue("full_source_committed", out var committed) && committed is bool b && !b,
                "The campaign provenance must identify an adaptation, without embedding the full source novel.");
            var campaign = JsonUtility.FromJson<CampaignData>(campaignAsset.text);
            Require(campaign != null && campaign.schema == 1 && !string.IsNullOrWhiteSpace(campaign.id)
                && campaign.levels != null, "Unsupported or incomplete campaign data.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var narrative = new Dictionary<string, NarrativeCard>(StringComparer.Ordinal);
            var cardLevels = new Dictionary<string, string>(StringComparer.Ordinal);
            var enemies = 0; var objectives = 0; var cards = 0;
            foreach (var level in campaign.levels)
            {
                Require(level != null && !string.IsNullOrWhiteSpace(level.id) && ids.Add(level.id), "Null or duplicate campaign level.");
                Require(Finite(level.width) && Finite(level.height) && level.width >= 200 && level.height >= 200,
                    "Invalid map bounds: " + level.id);
                Require(level.spawn != null && level.exit != null, "Missing spawn/exit: " + level.id);
                Point(level.spawn.x, level.spawn.y, level, "spawn");
                Rect(level.exit, level, "exit");
                foreach (var area in level.floors ?? Array.Empty<CampaignRect>()) Rect(area, level, "floor");
                foreach (var area in level.walls ?? Array.Empty<CampaignRect>()) Rect(area, level, "wall");
                foreach (var area in level.props ?? Array.Empty<CampaignRect>()) Rect(area, level, "prop");
                foreach (var area in level.regions ?? Array.Empty<CampaignRect>()) Rect(area, level, "region");
                foreach (var door in level.doors ?? Array.Empty<CampaignDoor>())
                {
                    Require(door != null && Finite(door.angle), "Invalid door: " + level.id);
                    Point(door.x, door.y, level, "door");
                }
                foreach (var enemy in level.enemies ?? Array.Empty<CampaignEnemy>())
                { Require(enemy != null, "Null enemy: " + level.id); Point(enemy.x, enemy.y, level, "enemy"); enemies++; }
                foreach (var pickup in level.pickups ?? Array.Empty<CampaignPickup>())
                { Require(pickup != null, "Null pickup: " + level.id); Point(pickup.x, pickup.y, level, "pickup"); }
                foreach (var actor in level.actors ?? Array.Empty<CampaignActor>())
                {
                    Require(actor != null && Finite(actor.speed) && actor.speed >= 0 && actor.health > 0,
                        "Invalid campaign actor: " + level.id);
                    Point(actor.x, actor.y, level, "actor");
                    foreach (var point in actor.route ?? Array.Empty<CampaignPoint>())
                    { Require(point != null, "Null actor route point: " + level.id); Point(point.x, point.y, level, "actor route"); }
                }
                foreach (var interaction in level.interactions ?? Array.Empty<CampaignInteraction>())
                {
                    Require(interaction != null && Finite(interaction.radius) && interaction.radius > 0,
                        "Invalid interaction radius: " + level.id);
                    Point(interaction.x, interaction.y, level, "interaction");
                }
                try { new CampaignProgress(level); }
                catch (Exception error) { throw new BuildFailedException("Campaign objectives are invalid in " + level.id + ": " + error.Message); }
                objectives += (level.objectives ?? Array.Empty<CampaignObjective>()).Length;
                Require(level.objectives != null && level.objectives.Length > 0, "Map has no authored objectives: " + level.id);
                cards += ValidateCards(level.before, level.id) + ValidateCards(level.after, level.id);
                foreach (var sequence in new[] { level.before, level.after })
                    foreach (var card in sequence ?? Array.Empty<NarrativeCard>())
                    {
                        Require(!narrative.ContainsKey(card.id), "Duplicate narrative card id: " + card.id);
                        narrative.Add(card.id, card); cardLevels.Add(card.id, level.id);
                    }
                if (level.id == "m02" || level.id == "e00") Require(!level.combat, "The ceremony and embassy epilogue must be noncombat: " + level.id);
            }
            foreach (var id in RequiredLevels) Require(ids.Contains(id), "Missing source episode: " + id);
            Require(ids.Contains(campaign.firstLevel), "The campaign entry point is not a defined level.");
            var atlasCount = ValidateArt(artAsset.text, out var artRegions);
            var recordings = ValidateVoice(campaignAsset, narrative, cardLevels);
            var cuts = ValidateDirection(narrative, recordings, artRegions);
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene), "Run unity/Tools/prepare_campaign.py to create the game scene.");
            Require(AssetDatabase.LoadAssetAtPath<SceneAsset>(OpeningScene), "The preserved opening scene is missing.");
            Require(Resources.Load<Font>("WhiteGuardIntro/fonts/Manrope-Variable")
                && Resources.Load<Font>("WhiteGuardIntro/fonts/Forum-Regular"), "Original game fonts were not imported.");
            Require(Shader.Find("Sprites/Default"), "The required Built-in Sprites/Default shader is unavailable.");
            Debug.Log("WHITE GUARD DATA IMPORT CHECK PASSED: " + ids.Count + " maps; " + objectives + " objectives; "
                + enemies + " enemies; " + cards + " narrative cards; " + recordings.Count + " checked voice clips; "
                + cuts + " directed cuts; " + atlasCount + " art atlases. This is not a PlayMode completion or listening test.");
        }

        private static int ValidateCards(NarrativeCard[] values, string levelId)
        {
            var count = 0;
            foreach (var card in values ?? Array.Empty<NarrativeCard>())
            {
                Require(card != null && SafeCardId(card.id) && !string.IsNullOrWhiteSpace(card.text) && Finite(card.hold) && card.hold >= 0,
                    "Invalid narrative card: " + levelId);
                Require(card.quadrant >= 0 && card.quadrant < 4, "Invalid narrative quadrant: " + levelId);
                if (!string.IsNullOrWhiteSpace(card.art))
                {
                    Require(card.art.StartsWith("WhiteGuardArt/", StringComparison.Ordinal)
                        || card.art.StartsWith("WhiteGuardIntro/", StringComparison.Ordinal), "Unexpected narrative resource path: " + card.art);
                    Require(Resources.Load<Texture2D>(card.art), "Missing narrative illustration: " + card.art);
                }
                count++;
            }
            return count;
        }

        private static int ValidateArt(string json, out HashSet<string> regions)
        {
            var art = JsonUtility.FromJson<ArtCatalog>(json);
            Require(art != null && art.schema == 1 && art.atlases != null && art.atlases.Length > 0, "Unsupported art manifest.");
            var resources = new HashSet<string>(StringComparer.Ordinal);
            regions = new HashSet<string>(StringComparer.Ordinal);
            var project = Directory.GetParent(Application.dataPath).FullName;
            foreach (var atlas in art.atlases)
            {
                Require(atlas != null && atlas.resource != null && atlas.resource.StartsWith("WhiteGuardArt/", StringComparison.Ordinal)
                    && resources.Add(atlas.resource), "Duplicate or invalid art resource.");
                var texture = Resources.Load<Texture2D>(atlas.resource);
                Require(texture && texture.width == atlas.width && texture.height == atlas.height,
                    "Art atlas is missing or resized: " + atlas.resource);
                var assetPath = AssetDatabase.GetAssetPath(texture);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
                Require(importer != null && importer.textureType == TextureImporterType.Default
                    && importer.textureShape == TextureImporterShape.Texture2D && importer.npotScale == TextureImporterNPOTScale.None
                    && !importer.mipmapEnabled && importer.sRGBTexture
                    && importer.textureCompression == TextureImporterCompression.Uncompressed,
                    "Campaign art must use unchanged Texture2D/sRGB/uncompressed/no-mipmap import: " + atlas.resource);
                importer.GetSourceTextureWidthAndHeight(out var width, out var height);
                Require(width == texture.width && height == texture.height, "Atlas source dimensions changed during import: " + atlas.resource);
                Require(!string.IsNullOrEmpty(atlas.sha256), "Art atlas has no source digest: " + atlas.resource);
                using (var algorithm = SHA256.Create())
                {
                    var digest = BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(Path.Combine(project, assetPath))))
                        .Replace("-", "").ToLowerInvariant();
                    Require(digest == atlas.sha256, "Art source SHA-256 mismatch: " + atlas.resource);
                }
                foreach (var region in atlas.entries ?? Array.Empty<Region>())
                    Require(region != null && !string.IsNullOrWhiteSpace(region.id) && regions.Add(region.id)
                        && region.x >= 0 && region.y >= 0 && region.w > 0 && region.h > 0
                        && region.x + region.w <= width && region.y + region.h <= height,
                        "Invalid or duplicate art region: " + atlas.resource);
            }
            Require(regions.Count > 0, "Art manifest contains no named regions.");
            return resources.Count;
        }

        private static Dictionary<string, VoiceRecording> ValidateVoice(TextAsset campaign,
            Dictionary<string, NarrativeCard> narrative, Dictionary<string, string> cardLevels)
        {
            var source = Resources.Load<TextAsset>("WhiteGuardCampaign/Voice/voice-manifest");
            Require(source, "The full campaign voice manifest is missing.");
            var root = PortableJson.Parse(source.text) as Dictionary<string, object>;
            Require(root != null && EmptyJsonArray(root, "pendingCardIds") && EmptyJsonArray(root, "failures"),
                "Campaign voice generation has pending cards/failures or an incomplete manifest.");
            Require(root.TryGetValue("introAudioChanged", out var changed) && changed is bool originalChanged && !originalChanged,
                "Campaign voice provenance must preserve the original opening audio.");
            var manifest = JsonUtility.FromJson<VoiceCatalog>(source.text);
            Require(manifest != null && manifest.schema == 1 && manifest.status == "complete" && manifest.cards != null,
                "Campaign voice manifest is not complete.");
            Require(narrative.Count == 88 && manifest.totalCards == narrative.Count && manifest.generatedCards == narrative.Count
                && manifest.cards.Length == narrative.Count, "The full authored 88-card voice set is incomplete or has extra entries.");
            Require(Sha256Text(campaign.text) == manifest.campaignSHA256 && Sha256File(AssetFile(campaign)) == manifest.campaignSHA256,
                "Campaign voice manifest SHA-256 differs from the imported/raw campaign JSON.");
            var entries = new Dictionary<string, VoiceRecording>(StringComparer.Ordinal);
            foreach (var entry in manifest.cards)
            {
                Require(entry != null && SafeCardId(entry.cardId) && !entries.ContainsKey(entry.cardId)
                    && narrative.ContainsKey(entry.cardId), "Duplicate, missing or extra voice card id.");
                var card = narrative[entry.cardId];
                Require(entry.levelId == cardLevels[entry.cardId] && entry.textSHA256 == Sha256Text(card.text),
                    "Voice text/episode digest is stale: " + entry.cardId);
                Require(entry.resource == "WhiteGuardCampaign/Voice/" + entry.cardId
                    && Finite(entry.durationSeconds) && entry.durationSeconds > .01 && entry.sampleRate > 0
                    && entry.channels > 0 && entry.channels <= 2 && entry.bytes > 0,
                    "Invalid voice resource/format metadata: " + entry.cardId);
                var clip = Resources.Load<AudioClip>(entry.resource);
                Require(clip && clip.samples > 0 && clip.frequency == entry.sampleRate && clip.channels == entry.channels,
                    "Voice clip import is missing or changed its sample format: " + entry.cardId);
                var path = AssetDatabase.GetAssetPath(clip);
                Require(path == "Assets/Resources/" + entry.resource + ".mp3", "The voice resource resolved to an unexpected asset: " + entry.cardId);
                var file = AssetFile(clip);
                Require(new FileInfo(file).Length == entry.bytes && Sha256File(file) == entry.audioSHA256,
                    "Voice MP3 byte count or SHA-256 changed: " + entry.cardId);
                Require(Math.Abs(clip.length - entry.durationSeconds) < .18,
                    "Unity decoded a different narration duration: " + entry.cardId);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Require(importer != null, "Missing voice AudioImporter: " + entry.cardId);
                var settings = importer.defaultSampleSettings;
                Require(settings.loadType == AudioClipLoadType.DecompressOnLoad && settings.compressionFormat == AudioCompressionFormat.PCM
                    && settings.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate && settings.preloadAudioData
                    && !importer.loadInBackground && !importer.forceToMono,
                    "Voice import must preserve samples as preloaded PCM: " + entry.cardId);
                Require(clip.loadState != AudioDataLoadState.Failed && (clip.loadState == AudioDataLoadState.Loaded || clip.LoadAudioData())
                    && clip.loadState == AudioDataLoadState.Loaded, "Unity could not preload/decode a narration clip: " + entry.cardId);
                entries.Add(entry.cardId, entry);
            }
            var directory = Path.Combine(Application.dataPath, "Resources", "WhiteGuardCampaign", "Voice");
            var files = Directory.GetFiles(directory, "*.mp3", SearchOption.TopDirectoryOnly);
            Require(files.Length == narrative.Count, "The voice directory has missing or unlisted MP3 assets.");
            foreach (var file in files) Require(entries.ContainsKey(Path.GetFileNameWithoutExtension(file)), "Unlisted voice MP3: " + Path.GetFileName(file));
            return entries;
        }

        private static int ValidateDirection(Dictionary<string, NarrativeCard> narrative,
            Dictionary<string, VoiceRecording> recordings, HashSet<string> artRegions)
        {
            var source = Resources.Load<TextAsset>("WhiteGuardCampaign/story-direction");
            Require(source, "Authored story-direction data is missing.");
            PortableJson.Parse(source.text);
            var direction = JsonUtility.FromJson<DirectionCatalog>(source.text);
            Require(direction != null && direction.schema == 1 && direction.shots != null && direction.shots.Length == narrative.Count,
                "Story direction does not cover every campaign narrative card.");
            var seen = new HashSet<string>(StringComparer.Ordinal); var count = 0;
            foreach (var shot in direction.shots)
            {
                Require(shot != null && !string.IsNullOrEmpty(shot.cardId) && narrative.ContainsKey(shot.cardId)
                    && seen.Add(shot.cardId) && shot.beats != null && shot.beats.Length > 0,
                    "Duplicate, missing or extra directed shot.");
                var previous = -1f;
                foreach (var beat in shot.beats)
                {
                    Require(beat != null && Finite(beat.at) && beat.at >= 0 && beat.at > previous
                        && beat.at < recordings[shot.cardId].durationSeconds,
                        "Story cut is unordered, repeated or beyond its spoken clip: " + shot.cardId);
                    if (previous < 0) Require(beat.at == 0, "A directed shot must start at zero: " + shot.cardId);
                    NormalizedRect(beat.view, "story crop", shot.cardId);
                    Require(beat.quadrant >= 0 && beat.quadrant < 4, "Invalid directed illustration quadrant: " + shot.cardId);
                    if (!string.IsNullOrEmpty(beat.art))
                    {
                        Require(beat.art.StartsWith("WhiteGuardArt/", StringComparison.Ordinal)
                            || beat.art.StartsWith("WhiteGuardIntro/", StringComparison.Ordinal), "Unexpected directed illustration resource: " + shot.cardId);
                        Require(Resources.Load<Texture2D>(beat.art), "Missing directed illustration resource: " + beat.art);
                    }
                    if (!string.IsNullOrEmpty(beat.insetArt))
                    {
                        Require(artRegions.Contains(beat.insetArt), "Missing directed detail region: " + beat.insetArt);
                        NormalizedRect(beat.inset, "story inset", shot.cardId);
                    }
                    previous = beat.at; count++;
                }
            }
            Require(seen.Count == narrative.Count, "Story direction is incomplete.");
            return count;
        }

        private static void NormalizedRect(float[] values, string purpose, string card)
        {
            Require(values != null && values.Length == 4, "Malformed " + purpose + ": " + card);
            foreach (var value in values) Require(Finite(value), "Non-finite " + purpose + ": " + card);
            Require(values[0] >= 0 && values[1] >= 0 && values[2] > 0 && values[3] > 0
                && values[0] + values[2] <= 1.00001f && values[1] + values[3] <= 1.00001f,
                "Out-of-frame " + purpose + ": " + card);
        }
        private static bool EmptyJsonArray(Dictionary<string, object> data, string key)
            => data.TryGetValue(key, out var value) && value is List<object> array && array.Count == 0;
        private static bool SafeCardId(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var c in id) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            return true;
        }
        private static string AssetFile(UnityEngine.Object asset)
            => Path.Combine(Directory.GetParent(Application.dataPath).FullName, AssetDatabase.GetAssetPath(asset));
        private static string Sha256File(string path)
        { using (var algorithm = SHA256.Create()) return Hex(algorithm.ComputeHash(File.ReadAllBytes(path))); }
        private static string Sha256Text(string value)
        { using (var algorithm = SHA256.Create()) return Hex(algorithm.ComputeHash(Encoding.UTF8.GetBytes(value ?? ""))); }
        private static string Hex(byte[] value) => BitConverter.ToString(value).Replace("-", "").ToLowerInvariant();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static void Point(float x, float y, CampaignLevel level, string purpose)
        { Require(Finite(x) && Finite(y) && x >= 0 && y >= 0 && x <= level.width && y <= level.height, "Invalid " + purpose + " point in " + level.id); }
        private static void Rect(CampaignRect area, CampaignLevel level, string purpose)
        {
            Require(area != null && Finite(area.x) && Finite(area.y) && Finite(area.w) && Finite(area.h)
                && area.x >= 0 && area.y >= 0 && area.w > 0 && area.h > 0
                && area.x + area.w <= level.width + .001f && area.y + area.h <= level.height + .001f,
                "Invalid " + purpose + " rectangle in " + level.id);
        }

        [MenuItem("White Guard/Build Linux Game")]
        public static void BuildLinuxGame() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/WhiteGuard");

        [MenuItem("White Guard/Build Windows Game")]
        public static void BuildWindowsGame() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/WhiteGuard.exe");

        private static void Build(BuildTarget target, string fallback)
        {
            ValidateData();
            Arkon.Unity.Editor.MigrationChecks.VerifyIntroImports();
            Require(BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target), "Required standalone build module is not installed: " + target);
            var configured = Environment.GetEnvironmentVariable("WHITEGUARD_BUILD");
            var project = Directory.GetParent(Application.dataPath).FullName;
            var output = Path.GetFullPath(Path.Combine(project, string.IsNullOrWhiteSpace(configured) ? fallback : configured));
            Require(!string.IsNullOrEmpty(Path.GetFileName(output)) && !Directory.Exists(output),
                "WHITEGUARD_BUILD must point to the output executable, not a directory.");
            if (target == BuildTarget.StandaloneWindows64) Require(output.EndsWith(".exe", StringComparison.OrdinalIgnoreCase), "Windows output must end with .exe.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var options = Environment.GetEnvironmentVariable("WHITEGUARD_DEVELOPMENT") == "1" ? BuildOptions.Development : BuildOptions.None;
            var report = BuildWithSpriteShader(output, target, options);
            Require(report != null && report.summary.result == BuildResult.Succeeded, "White Guard player build failed for " + target);
            Debug.Log("WHITE GUARD PLAYER BUILD SUCCEEDED: " + target + "; " + output + "; " + report.summary.totalSize
                + " bytes. Player launch and full campaign playthrough remain separate checks.");
        }

        private static BuildReport BuildWithSpriteShader(string output, BuildTarget target, BuildOptions options)
        {
            var required = Shader.Find("Sprites/Default");
            Require(required, "Sprites/Default is required by runtime SpriteRenderer/LineRenderer materials.");
            UnityEngine.Object graphics = null;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset"))
                if (asset) { graphics = asset; break; }
            if (!graphics)
            {
                var loaded = Resources.FindObjectsOfTypeAll<UnityEngine.Rendering.GraphicsSettings>();
                if (loaded.Length > 0) graphics = loaded[0];
            }
            Require(graphics, "Unity graphics settings must be initialized before building.");
            var serialized = new SerializedObject(graphics);
            var shaders = serialized.FindProperty("m_AlwaysIncludedShaders");
            Require(shaders != null && shaders.isArray, "Unity graphics settings do not expose the expected included-shader list.");
            var original = new UnityEngine.Object[shaders.arraySize]; var alreadyIncluded = false;
            for (var i = 0; i < original.Length; i++)
            { original[i] = shaders.GetArrayElementAtIndex(i).objectReferenceValue; alreadyIncluded |= original[i] == required; }
            try
            {
                if (!alreadyIncluded)
                {
                    shaders.arraySize = original.Length + 1;
                    shaders.GetArrayElementAtIndex(original.Length).objectReferenceValue = required;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                return BuildPipeline.BuildPlayer(new[] { GameScene, OpeningScene }, output, target, options);
            }
            finally
            {
                if (!alreadyIncluded && graphics)
                {
                    var restored = new SerializedObject(graphics); var list = restored.FindProperty("m_AlwaysIncludedShaders");
                    Require(list != null && list.isArray, "Could not restore Unity's included-shader list after build.");
                    list.arraySize = original.Length;
                    for (var i = 0; i < original.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = original[i];
                    restored.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
