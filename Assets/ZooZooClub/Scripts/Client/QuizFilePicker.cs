using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>선생님이 고른 문제 엑셀 파일을 읽은 결과.</summary>
    public sealed class QuizFileResult
    {
        public bool cancelled;
        public string error;
        public string fileName = "";
        /// <summary>엑셀의 첫 번째 시트. rows[0]이 1행(제목 줄)이다.</summary>
        public List<string[]> rows;
    }

    /// <summary>
    /// 문제 엑셀 파일(.xlsx)을 고르게 하고 첫 번째 시트를 글자 표로 읽는다.
    /// 브라우저에서는 ZooWebFile.jslib가 파일 창을 열고 읽으며, 끝나면 Take()로 결과를 가져간다(매 프레임 불러 본다).
    /// 에디터에서는 파일 창으로 고르고 에디터 쪽 엑셀 읽기(QuizExcelImporter의 XlsxReader)로 읽는다.
    /// </summary>
    public static class QuizFilePicker
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ZooFilePick();
        [DllImport("__Internal")] static extern string ZooFileTake();
#endif

        /// <summary>에디터에서 엑셀을 읽는 방법. 에디터 스크립트가 넣어 준다.</summary>
        public static Func<string, List<string[]>> EditorReader;

        /// <summary>테스트용: 값이 있으면 파일 창 없이 이 경로의 파일을 고른 것으로 한다(한 번만).</summary>
        public static string TestPath;

        public static bool Waiting { get; private set; }
        static QuizFileResult ready;

        public static void Pick()
        {
            ready = null;
            Waiting = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            ZooFilePick();
#else
            string path = TestPath;
            TestPath = null;
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(path)) path = UnityEditor.EditorUtility.OpenFilePanel("문제 엑셀 고르기", "", "xlsx");
#endif
            if (string.IsNullOrEmpty(path)) { ready = new QuizFileResult { cancelled = true }; return; }
            try
            {
                if (EditorReader == null) throw new InvalidOperationException("이 화면에서는 엑셀을 읽지 못해요.");
                ready = new QuizFileResult { fileName = System.IO.Path.GetFileName(path), rows = EditorReader(path) };
            }
            catch (Exception e)
            {
                ready = new QuizFileResult { error = e.Message };
            }
#endif
        }

        /// <summary>파일을 다 읽었으면 결과를, 아직이면 null을 돌려준다.</summary>
        public static QuizFileResult Take()
        {
            if (!Waiting) return null;
#if UNITY_WEBGL && !UNITY_EDITOR
            string json = ZooFileTake();
            if (string.IsNullOrEmpty(json)) return null;
            ready = Parse(json);
#endif
            if (ready == null) return null;
            var result = ready;
            ready = null;
            Waiting = false;
            return result;
        }

        static QuizFileResult Parse(string json)
        {
            try
            {
                var map = MiniJson.AsObject(MiniJson.Parse(json));
                if (map == null) return new QuizFileResult { error = "파일을 읽지 못했어요." };
                if (MiniJson.AsBool(MiniJson.Get(map, "cancel"), false)) return new QuizFileResult { cancelled = true };
                string error = MiniJson.AsString(MiniJson.Get(map, "error"), null);
                if (!string.IsNullOrEmpty(error)) return new QuizFileResult { error = error };

                var result = new QuizFileResult { fileName = MiniJson.AsString(MiniJson.Get(map, "name")), rows = new List<string[]>() };
                var rows = MiniJson.Get(map, "rows") as List<object>;
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var cells = row as List<object>;
                        var line = new string[cells != null ? cells.Count : 0];
                        for (int i = 0; i < line.Length; i++) line[i] = MiniJson.AsString(cells[i]);
                        result.rows.Add(line);
                    }
                }
                return result;
            }
            catch (Exception e)
            {
                return new QuizFileResult { error = "파일을 읽지 못했어요: " + e.Message };
            }
        }
    }
}
