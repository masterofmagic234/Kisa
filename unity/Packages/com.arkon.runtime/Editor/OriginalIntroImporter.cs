using UnityEditor;

namespace Arkon.Unity.Editor
{
    // Preserve the decoded artwork and voice, rather than changing their design.
    public sealed class OriginalIntroImporter : AssetPostprocessor
    {
        private bool IsOriginalIntro => assetPath.StartsWith("Assets/Resources/WhiteGuardIntro/");

        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!IsOriginalIntro) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.filterMode = UnityEngine.FilterMode.Bilinear;
        }
        private void OnPreprocessAudio()
        {
            if (!IsOriginalIntro) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = UnityEngine.AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = UnityEngine.AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.forceToMono = false;
        }
    }
}
