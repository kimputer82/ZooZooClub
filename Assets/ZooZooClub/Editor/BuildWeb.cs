using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZooZooClub.EditorTools
{
    /// <summary>
    /// 브라우저에서 하는 WebGL 빌드. 메뉴 ZooZooClub/웹 빌드 만들기.
    /// 결과는 프로젝트 폴더 아래 Web/ 에 들어가며, 저장소에 올려 두면 GitHub Pages가 그대로 공개한다
    /// (.github/workflows/pages.yml). 학생은 주소만 열면 된다.
    /// </summary>
    public static class BuildWeb
    {
        public const string OutputFolder = "Web";

        [MenuItem("ZooZooClub/웹 빌드 만들기")]
        public static void BuildFromMenu()
        {
            string result = Build();
            Debug.Log("[Zoo] " + result);
            EditorUtility.DisplayDialog("웹 빌드", result, "확인");
        }

        /// <summary>빌드하고 사람이 읽을 결과 한 줄을 돌려준다.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlaying) return "실행 중에는 빌드할 수 없어요. 먼저 멈춰 주세요.";

            string root = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(root, OutputFolder);
            Directory.CreateDirectory(folder);

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) scenes.Add(scene.path);
            }
            if (scenes.Count == 0) return "빌드할 장면이 없어요(File > Build Profiles 에서 장면을 넣어 주세요).";

            // GitHub Pages 같은 보통 웹 서버는 압축 헤더를 붙여 주지 않으므로,
            // gzip으로 줄이되 브라우저 쪽에서 풀도록(decompressionFallback) 한다.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:ZooZooClub";
            PlayerSettings.runInBackground = true;

            // 켤 때 나오는 "Made with Unity" 화면은 끈다(Unity 6부터는 무료 라이선스에서도 끌 수 있다).
            PlayerSettings.SplashScreen.show = false;

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = folder,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                return "빌드 실패: " + e.Message;
            }

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                return "빌드 실패(" + summary.result + "): 오류 " + summary.totalErrors + "개. Console 창을 확인해 주세요.";
            }

            // GitHub Pages가 밑줄로 시작하는 파일을 건너뛰지 않도록 Jekyll을 끈다.
            File.WriteAllText(Path.Combine(folder, ".nojekyll"), "");
            // 언제 만든 빌드인지 적어 둔다(배포된 것이 새 빌드인지 확인할 때 주소 뒤에 /version.txt 를 붙여 본다).
            File.WriteAllText(Path.Combine(folder, "version.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n");

            double megabytes = summary.totalSize / (1024.0 * 1024.0);
            return "빌드 완료: " + folder + " (" + megabytes.ToString("0") + "MB, "
                + summary.totalTime.TotalSeconds.ToString("0") + "초)";
        }
    }
}
