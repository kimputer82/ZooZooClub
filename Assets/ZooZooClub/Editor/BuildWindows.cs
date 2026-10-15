using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZooZooClub.EditorTools
{
    /// <summary>
    /// 학생 PC에 나눠 줄 윈도우용 게임을 만든다. 메뉴 ZooZooClub/윈도우 빌드 만들기.
    /// 결과는 프로젝트 폴더 아래 Builds/Windows/ZooZooClub/ 에 들어가며(저장소에는 올리지 않는다),
    /// 그 폴더를 통째로 복사해 ZooZooClub.exe 를 실행하면 된다. 설치는 필요 없다.
    /// </summary>
    public static class BuildWindows
    {
        public const string OutputFolder = "Builds/Windows/ZooZooClub";
        public const string ExeName = "ZooZooClub.exe";

        [MenuItem("ZooZooClub/윈도우 빌드 만들기")]
        public static void BuildFromMenu()
        {
            string result = Build();
            Debug.Log("[Zoo] " + result);
            EditorUtility.DisplayDialog("윈도우 빌드", result, "확인");
        }

        /// <summary>빌드하고 사람이 읽을 결과 한 줄을 돌려준다.</summary>
        public static string Build()
        {
            if (EditorApplication.isPlaying) return "실행 중에는 빌드할 수 없어요. 먼저 멈춰 주세요.";

            string root = Directory.GetParent(Application.dataPath).FullName;
            string folder = Path.Combine(root, OutputFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(folder);

            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled) scenes.Add(scene.path);
            }
            if (scenes.Count == 0) return "빌드할 장면이 없어요(File > Build Profiles 에서 장면을 넣어 주세요).";

            // 교실 모니터가 제각각이라 처음엔 화면 가득(테두리 없는 창)으로 띄우고, 창으로 바꾸면 크기를 조절할 수 있게 둔다.
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            // 켤 때 나오는 "Made with Unity" 화면은 끈다(Unity 6부터는 무료 라이선스에서도 끌 수 있다).
            PlayerSettings.SplashScreen.show = false;

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
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

            double megabytes = summary.totalSize / (1024.0 * 1024.0);
            return "빌드 완료: " + Path.Combine(folder, ExeName) + " (" + megabytes.ToString("0") + "MB, "
                + summary.totalTime.TotalSeconds.ToString("0") + "초)";
        }
    }
}
