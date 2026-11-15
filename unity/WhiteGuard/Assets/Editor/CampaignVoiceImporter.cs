using UnityEditor;
using UnityEngine;

namespace WhiteGuard.Editor
{
    public sealed class CampaignVoiceImporter : AssetPostprocessor
    {
        private void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Resources/WhiteGuardCampaign/Voice/", System.StringComparison.Ordinal)) return;
            var importer = (AudioImporter)assetImporter;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.forceToMono = false;
        }
    }
}
