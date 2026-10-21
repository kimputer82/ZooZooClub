using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.EditorTools
{
    /// <summary>
    /// 프로젝트의 Quiz 폴더(Assets 바깥, 프로젝트 맨 위)에 있는 엑셀 파일을 읽어 문제 은행
    /// (Assets/ZooZooClub/Resources/quiz.json)을 다시 만든다.
    ///
    /// 엑셀 파일을 저장하면 에디터가 몇 초 안에 알아채고 자동으로 읽는다. 폴더 안의 .xlsx 파일을
    /// 이름 순서로 모두 읽어 합치며, 파일마다 첫 번째 시트만 읽는다. 읽은 결과(등록한 문제 수와
    /// 등록하지 못한 줄)는 같은 폴더의 '등록결과.txt'에 남긴다. 표의 형식은 Core의 QuizSheet 참고.
    ///
    /// 게임에는 quiz.json만 들어가므로, 엑셀을 고친 뒤에는 게임을 다시 빌드해야 배포판에 반영된다.
    /// </summary>
    [InitializeOnLoad]
    public static class QuizExcelImporter
    {
        public const string FolderName = "Quiz";
        public const string ResultFileName = "등록결과.txt";
        const string QuizJsonAssetPath = "Assets/ZooZooClub/Resources/quiz.json";
        const double PollSeconds = 2.0;

        static double nextPoll;

        static string Folder
        {
            get { return Path.Combine(Directory.GetCurrentDirectory(), FolderName); }
        }

        /// <summary>마지막으로 읽은 파일들의 이름·크기·저장 시각. 프로젝트의 Library 폴더에 둔다(컴퓨터마다 따로).</summary>
        static string StampPath
        {
            get { return Path.Combine(Directory.GetCurrentDirectory(), "Library", "ZooQuizSheets.stamp"); }
        }

        static QuizExcelImporter()
        {
            EditorApplication.update += Poll;
            // 선생님 화면의 '문제 올리기'를 에디터에서 눌렀을 때 쓰는 엑셀 읽기.
            ZooZooClub.Client.QuizFilePicker.EditorReader = XlsxReader.ReadFirstSheet;
        }

        static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + PollSeconds;

            // 플레이 중이거나 컴파일 중에는 건드리지 않고, 끝난 뒤에 읽는다.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling
                || EditorApplication.isUpdating) return;

            try
            {
                var files = FindSheets();
                if (files.Count == 0) return;

                string stamp = StampOf(files);
                if (File.Exists(StampPath) && File.ReadAllText(StampPath) == stamp) return;

                // 읽다가 문제가 생겨도 같은 파일을 몇 초마다 되풀이해 읽지 않도록, 읽기 전에 적어 둔다.
                File.WriteAllText(StampPath, stamp);
                Import(files);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 문제 엑셀을 확인하지 못했습니다: " + e.Message);
            }
        }

        [MenuItem("ZooZooClub/문제 엑셀 다시 읽기")]
        public static void ImportNowMenu()
        {
            Debug.Log("[Zoo] " + ImportNow());
        }

        /// <summary>엑셀이 바뀌지 않았어도 지금 다시 읽는다. 한 줄 요약을 돌려준다.</summary>
        public static string ImportNow()
        {
            var files = FindSheets();
            if (files.Count == 0) return FolderName + " 폴더에 엑셀 파일(.xlsx)이 없어요.";

            string summary = Import(files);
            File.WriteAllText(StampPath, StampOf(files));
            return summary;
        }

        static List<string> FindSheets()
        {
            var files = new List<string>();
            if (!Directory.Exists(Folder)) return files;

            foreach (string path in Directory.GetFiles(Folder, "*.xlsx"))
            {
                // 엑셀이 파일을 열어 둔 동안 만드는 잠금 파일(~$이름.xlsx)은 건너뛴다.
                if (Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal)) continue;
                files.Add(path);
            }
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        static string StampOf(List<string> files)
        {
            var builder = new StringBuilder();
            foreach (string path in files)
            {
                var info = new FileInfo(path);
                builder.Append(info.Name).Append('|').Append(info.Length).Append('|')
                    .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
            }
            return builder.ToString();
        }

        static string Import(List<string> files)
        {
            var questions = new List<QuizDef>();
            var report = new StringBuilder();
            int problemCount = 0;

            foreach (string path in files)
            {
                string name = Path.GetFileName(path);
                report.Append('\n').Append("■ ").Append(name).Append('\n');

                QuizSheetResult sheet;
                try
                {
                    sheet = QuizSheet.Read(XlsxReader.ReadFirstSheet(path));
                }
                catch (Exception e)
                {
                    problemCount++;
                    report.Append("  읽지 못했어요: ").Append(e.Message).Append('\n')
                        .Append("  엑셀 통합 문서(.xlsx)로 저장했는지 확인해 주세요.\n");
                    continue;
                }

                questions.AddRange(sheet.questions);
                report.Append("  등록한 문제 ").Append(sheet.questions.Count).Append("개\n");

                if (sheet.problems.Count > 0)
                {
                    problemCount += sheet.problems.Count;
                    report.Append("  등록하지 못한 줄 ").Append(sheet.problems.Count).Append("개 (고친 뒤 저장하면 다시 읽어요)\n");
                    foreach (string problem in sheet.problems) report.Append("    - ").Append(problem).Append('\n');
                }
                if (sheet.notices.Count > 0)
                {
                    report.Append("  확인해 보면 좋은 줄 ").Append(sheet.notices.Count).Append("개 (등록은 했어요)\n");
                    foreach (string notice in sheet.notices) report.Append("    - ").Append(notice).Append('\n');
                }
            }

            string summary;
            if (questions.Count == 0)
            {
                // 읽을 수 있는 문제가 하나도 없으면 실수로 문제 은행을 비우지 않도록 그대로 둔다.
                summary = "등록할 수 있는 문제가 없어서 문제 은행은 그대로 두었어요.";
            }
            else
            {
                for (int i = 0; i < questions.Count; i++) questions[i].id = "q" + (i + 1).ToString("000");
                bool changed = WriteBank(questions);
                summary = "문제 " + questions.Count + "개를 등록했어요"
                    + (problemCount > 0 ? ", 등록하지 못한 줄 " + problemCount + "개." : ".")
                    + (changed ? "" : " (문제 은행은 전과 같아요)");
            }

            var text = new StringBuilder();
            text.Append("문제 엑셀 등록 결과\n")
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n')
                .Append(summary).Append('\n')
                .Append(report)
                .Append("\n게임을 플레이 중이었다면 멈췄다가 다시 시작해야 새 문제가 나와요.\n")
                .Append("배포한 게임에 반영하려면 게임을 다시 빌드해야 해요.\n");
            File.WriteAllText(Path.Combine(Folder, ResultFileName), text.ToString().Replace("\n", "\r\n"),
                new UTF8Encoding(true));

            if (problemCount > 0) Debug.LogWarning("[Zoo] 문제 엑셀: " + summary + " 자세한 내용은 Quiz/" + ResultFileName);
            else Debug.Log("[Zoo] 문제 엑셀: " + summary);
            return summary;
        }

        /// <summary>
        /// 문제 은행 파일을 다시 쓴다. 기본 보상과 틀린 뒤 기다리는 시간은 원래 파일의 값을 그대로 둔다.
        /// 내용이 전과 같으면 쓰지 않고 false를 돌려준다.
        /// </summary>
        static bool WriteBank(List<QuizDef> questions)
        {
            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), QuizJsonAssetPath);
            var settings = new QuizBank();
            string before = null;
            if (File.Exists(fullPath))
            {
                before = File.ReadAllText(fullPath);
                try
                {
                    var old = JsonUtility.FromJson<QuizBank>(before);
                    if (old != null)
                    {
                        settings.rewardCoins = old.rewardCoins;
                        settings.wrongCooldownSeconds = old.wrongCooldownSeconds;
                    }
                }
                catch (Exception)
                {
                    // 원래 파일이 깨져 있으면 기본값으로 새로 쓴다.
                }
            }

            var json = new StringBuilder();
            json.Append("{\n")
                .Append("  \"rewardCoins\": ").Append(settings.rewardCoins).Append(",\n")
                .Append("  \"wrongCooldownSeconds\": ").Append(settings.wrongCooldownSeconds).Append(",\n")
                .Append("  \"questions\": [\n");
            for (int i = 0; i < questions.Count; i++)
            {
                var quiz = questions[i];
                json.Append("    {\"id\": ").Append(Quote(quiz.id))
                    .Append(", \"question\": ").Append(Quote(quiz.question))
                    .Append(", \"choices\": [");
                for (int c = 0; c < quiz.choices.Count; c++)
                {
                    if (c > 0) json.Append(", ");
                    json.Append(Quote(quiz.choices[c]));
                }
                json.Append("], \"answer\": ").Append(quiz.answer)
                    .Append(", \"reward\": ").Append(quiz.reward).Append('}')
                    .Append(i < questions.Count - 1 ? ",\n" : "\n");
            }
            json.Append("  ]\n}\n");

            string after = json.ToString();
            if (before != null && before.Replace("\r\n", "\n") == after) return false;

            File.WriteAllText(fullPath, after, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(QuizJsonAssetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        static string Quote(string text)
        {
            var builder = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': break;
                    case '\t': builder.Append(' '); break;
                    default:
                        if (c < ' ') builder.Append("\\u").Append(((int)c).ToString("x4"));
                        else builder.Append(c);
                        break;
                }
            }
            return builder.Append('"').ToString();
        }
    }

    /// <summary>
    /// 엑셀 통합 문서(.xlsx)의 첫 번째 시트를 글자 표로 읽는다. 엑셀이 설치되어 있지 않아도 된다.
    /// .xlsx는 XML 파일 몇 개를 묶은 압축 파일이라, 그 가운데 필요한 것만 꺼내 읽는다.
    /// </summary>
    public static class XlsxReader
    {
        const int MaxRows = 5000;
        const int MaxColumns = 60;

        /// <summary>줄 번호가 엑셀과 같도록, 비어 있는 줄도 빈 줄로 채워서 돌려준다(rows[0]이 1행).</summary>
        public static List<string[]> ReadFirstSheet(string path)
        {
            // 엑셀이 파일을 열어 둔 채여도 읽을 수 있게 한다.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var shared = ReadSharedStrings(archive);
                var sheetEntry = archive.GetEntry(FirstSheetPath(archive));
                if (sheetEntry == null) throw new InvalidDataException("시트를 찾지 못했어요.");

                var rows = new List<string[]>();
                XDocument sheet = Load(sheetEntry);
                foreach (var rowElement in Descendants(sheet.Root, "row"))
                {
                    int rowNumber = rows.Count + 1;
                    int parsedRow;
                    string rowAttribute = (string)rowElement.Attribute("r");
                    if (rowAttribute != null && int.TryParse(rowAttribute, out parsedRow) && parsedRow >= rowNumber)
                    {
                        rowNumber = parsedRow;
                    }
                    if (rowNumber > MaxRows) break;
                    while (rows.Count < rowNumber - 1) rows.Add(new string[0]);

                    var cells = new List<string>();
                    foreach (var cell in Children(rowElement, "c"))
                    {
                        int column = ColumnOf((string)cell.Attribute("r"), cells.Count);
                        if (column >= MaxColumns) continue;
                        while (cells.Count <= column) cells.Add("");
                        cells[column] = ValueOf(cell, shared);
                    }
                    rows.Add(cells.ToArray());
                }
                return rows;
            }
        }

        static string FirstSheetPath(ZipArchive archive)
        {
            const string fallback = "xl/worksheets/sheet1.xml";
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry == null || relsEntry == null) return fallback;

            string relationId = null;
            foreach (var sheet in Descendants(Load(workbookEntry).Root, "sheet"))
            {
                foreach (var attribute in sheet.Attributes())
                {
                    if (attribute.Name.LocalName == "id" && attribute.Name.Namespace != XNamespace.None)
                    {
                        relationId = attribute.Value;
                    }
                }
                break;
            }
            if (relationId == null) return fallback;

            foreach (var relation in Descendants(Load(relsEntry).Root, "Relationship"))
            {
                if ((string)relation.Attribute("Id") != relationId) continue;
                string target = (string)relation.Attribute("Target") ?? "";
                if (target.StartsWith("/", StringComparison.Ordinal)) return target.TrimStart('/');
                return "xl/" + target;
            }
            return fallback;
        }

        static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var strings = new List<string>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return strings;

            foreach (var item in Children(Load(entry).Root, "si")) strings.Add(TextOf(item));
            return strings;
        }

        /// <summary>
        /// 글자 칸의 내용. 글자 일부만 꾸민 칸은 여러 조각(r)으로 나뉘어 있으므로 이어 붙인다.
        /// 한글·일본어 엑셀이 덧붙이는 읽는 법 정보(rPh)는 내용이 아니므로 뺀다.
        /// </summary>
        static string TextOf(XElement item)
        {
            var builder = new StringBuilder();
            foreach (var child in item.Elements())
            {
                string name = child.Name.LocalName;
                if (name == "t") builder.Append(child.Value);
                else if (name == "r")
                {
                    foreach (var text in Children(child, "t")) builder.Append(text.Value);
                }
            }
            return builder.ToString();
        }

        static string ValueOf(XElement cell, List<string> shared)
        {
            string type = (string)cell.Attribute("t") ?? "n";
            string value = null;
            foreach (var child in Children(cell, "v"))
            {
                value = child.Value;
                break;
            }

            switch (type)
            {
                case "s":
                {
                    int index;
                    if (value != null && int.TryParse(value, out index) && index >= 0 && index < shared.Count)
                    {
                        return shared[index];
                    }
                    return "";
                }
                case "inlineStr":
                    foreach (var inline in Children(cell, "is")) return TextOf(inline);
                    return "";
                case "b":
                    return value == "1" ? "TRUE" : "FALSE";
                case "str":
                case "e":
                    return value ?? "";
                default:
                    return CleanNumber(value);
            }
        }

        /// <summary>숫자 칸의 값. 15나 0.5처럼 사람이 적은 모양으로 돌려준다(15.0이나 0.30000000000000004가 되지 않게).</summary>
        static string CleanNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            double number;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return value;
            if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
            {
                return ((long)number).ToString(CultureInfo.InvariantCulture);
            }
            return number.ToString("G15", CultureInfo.InvariantCulture);
        }

        /// <summary>"C7" 같은 칸 이름에서 열 번호(A가 0)를 얻는다. 이름이 없으면 순서대로 센다.</summary>
        static int ColumnOf(string reference, int fallback)
        {
            if (string.IsNullOrEmpty(reference)) return fallback;
            int column = 0;
            int letters = 0;
            foreach (char c in reference)
            {
                char upper = char.ToUpperInvariant(c);
                if (upper < 'A' || upper > 'Z') break;
                column = column * 26 + (upper - 'A' + 1);
                letters++;
            }
            return letters > 0 ? column - 1 : fallback;
        }

        static XDocument Load(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            {
                return XDocument.Load(stream);
            }
        }

        // 엑셀 파일의 XML은 저장한 프로그램에 따라 이름 공간이 다를 수 있어서, 이름만으로 찾는다.
        static IEnumerable<XElement> Children(XElement parent, string name)
        {
            foreach (var child in parent.Elements())
            {
                if (child.Name.LocalName == name) yield return child;
            }
        }

        static IEnumerable<XElement> Descendants(XElement parent, string name)
        {
            foreach (var child in parent.Descendants())
            {
                if (child.Name.LocalName == name) yield return child;
            }
        }
    }
}
