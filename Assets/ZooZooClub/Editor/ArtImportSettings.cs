using UnityEditor;
using UnityEngine;

namespace ZooZooClub.EditorTools
{
    /// <summary>
    /// Assets/ZooZooClub/Resources/Art 폴더에 들어오는 그림의 임포트 설정을 자동으로 맞춘다.
    /// 그림 한 장을 통째로 스프라이트 하나로 쓰도록 해서, 크기가 다른 그림으로 덮어써도 그대로 동작한다.
    /// </summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        public const string ArtFolder = "Assets/ZooZooClub/Resources/Art";

        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(ArtFolder + "/")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
        }

        [MenuItem("ZooZooClub/그림 다시 불러오기")]
        public static void ReimportArt()
        {
            AssetDatabase.ImportAsset(ArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        }
    }
}
