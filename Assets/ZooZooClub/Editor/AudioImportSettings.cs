using UnityEditor;
using UnityEngine;

namespace ZooZooClub.EditorTools
{
    /// <summary>Resources/Audio 폴더 소리 파일의 임포트 설정. 배경음은 압축한 채로 두고, 짧은 효과음은 풀어서 바로 낸다.</summary>
    public sealed class AudioImportSettings : AssetPostprocessor
    {
        public const string AudioFolder = "Assets/ZooZooClub/Resources/Audio";

        void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(AudioFolder + "/")) return;

            var importer = (AudioImporter)assetImporter;
            bool music = System.IO.Path.GetFileName(path).StartsWith("bgm_");
            // 효과음은 짧고 작으므로 압축 없이(PCM) 둔다. 브라우저에서도 그대로 재생된다.
            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = music ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
            settings.quality = 0.5f;
            // 브라우저(WebGL)는 압축된 채로 두면 재생하지 못하므로 배경음도 읽을 때 풀어 둔다(용량을 위해 배경음 파일은 모노 32kHz).
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = false;
            importer.loadInBackground = false;
        }

        [MenuItem("ZooZooClub/소리 다시 불러오기")]
        public static void ReimportAudio()
        {
            AssetDatabase.ImportAsset(AudioFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }
    }
}
