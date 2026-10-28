using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ZooZooClub.Core;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 학생 이름마다 따로 두는 저장 파일의 자리.
    ///
    /// 기본 자리는 이 컴퓨터의 사용자 폴더 아래(Application.persistentDataPath/saves)다.
    /// 다시 켜면 처음 상태로 돌아가는 컴퓨터실 PC처럼 그 자리가 지워지는 곳에서는, 게임 실행 파일 옆에
    /// save_folder.txt 를 만들고 저장할 폴더의 경로를 한 줄 적어 두면 그 폴더를 쓴다
    /// (예: D:\ZooZooSaves, 지워지지 않는 드라이브나 공유 폴더).
    /// </summary>
    public static class SaveFiles
    {
        public const string FolderFileName = "save_folder.txt";
        const string Prefix = "p_";
        const string Extension = ".json";
        const string LastNameKey = "zoo.lastPlayer";

        [Serializable]
        class NameOnly
        {
            public string playerName = "";
            public string roomCode = "";
        }

        /// <summary>그 이름의 저장에 적힌 방 코드. 저장이 없거나 방 없이 하던 중이면 빈 문자열.</summary>
        public static string RoomCodeOf(string name)
        {
            try
            {
                string path = PathFor(name);
                if (path == null || !File.Exists(path)) return "";
                var saved = JsonUtility.FromJson<NameOnly>(File.ReadAllText(path));
                return saved != null && saved.roomCode != null ? saved.roomCode : "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>이름별 저장 파일을 두는 폴더. 없으면 만든다.</summary>
        public static string Folder
        {
            get
            {
                string custom = CustomFolder();
                if (custom != null)
                {
                    try
                    {
                        Directory.CreateDirectory(custom);
                        return custom;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[Zoo] " + FolderFileName + "에 적힌 폴더를 쓸 수 없어 기본 자리에 저장합니다: " + e.Message);
                    }
                }

                string folder = Path.Combine(Application.persistentDataPath, "saves");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        /// <summary>로그인 방식이 생기기 전에 쓰던 저장 파일.</summary>
        public static string LegacyPath
        {
            get { return Path.Combine(Application.persistentDataPath, "save.json"); }
        }

        /// <summary>마지막으로 들어온 이름. 개발 도구가 어느 저장 파일을 다룰지 정할 때 쓴다.</summary>
        public static string LastName
        {
            get { return PlayerPrefs.GetString(LastNameKey, ""); }
            set
            {
                PlayerPrefs.SetString(LastNameKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        /// <summary>그 이름의 저장 파일 경로. 쓸 수 없는 이름이면 null.</summary>
        public static string PathFor(string name)
        {
            string key = PlayerName.Key(name);
            return key == null ? null : Path.Combine(Folder, Prefix + key + Extension);
        }

        /// <summary>이 컴퓨터에 있는 그 이름의 저장을 지운다(서버의 저장은 그대로). 지웠으면 true.</summary>
        public static bool DeleteProfile(string name)
        {
            try
            {
                string path = PathFor(name);
                if (path == null || !File.Exists(path)) return false;
                File.Delete(path);
                string key = PlayerName.Key(name);
                if (key != null && PlayerName.Key(LastName) == key) LastName = "";
                WebSave.Flush();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장을 지우지 못했습니다: " + e.Message);
                return false;
            }
        }

        public static bool AnyProfileExists()
        {
            return Directory.GetFiles(Folder, Prefix + "*" + Extension).Length > 0;
        }

        /// <summary>이 컴퓨터에서 하던 이름들. 가장 최근에 한 이름이 먼저 온다.</summary>
        public static List<string> RecentNames(int max)
        {
            var names = new List<string>();
            try
            {
                var files = new List<FileInfo>();
                foreach (string path in Directory.GetFiles(Folder, Prefix + "*" + Extension)) files.Add(new FileInfo(path));
                files.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

                foreach (var file in files)
                {
                    if (names.Count >= max) break;
                    string name = null;
                    try
                    {
                        var saved = JsonUtility.FromJson<NameOnly>(File.ReadAllText(file.FullName));
                        if (saved != null) name = saved.playerName;
                    }
                    catch (Exception)
                    {
                        // 읽지 못하는 파일은 파일 이름으로 대신한다.
                    }
                    if (string.IsNullOrEmpty(name))
                    {
                        name = Path.GetFileNameWithoutExtension(file.Name).Substring(Prefix.Length);
                    }
                    if (PlayerName.Key(name) != null && !names.Contains(name)) names.Add(name);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] 저장된 이름을 읽지 못했습니다: " + e.Message);
            }
            return names;
        }

        static string CustomFolder()
        {
            // 브라우저에서는 게임 파일 옆이라는 것이 없다.
            if (WebSave.IsWeb) return null;
            try
            {
                // 빌드한 게임에서는 실행 파일이 있는 폴더, 에디터에서는 프로젝트 맨 위 폴더.
                string root = Directory.GetParent(Application.dataPath).FullName;
                string file = Path.Combine(root, FolderFileName);
                if (!File.Exists(file)) return null;

                foreach (string line in File.ReadAllLines(file))
                {
                    string path = line.Trim().Trim('"');
                    if (path.Length == 0 || path.StartsWith("#", StringComparison.Ordinal)) continue;
                    return Path.IsPathRooted(path) ? path : Path.Combine(root, path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Zoo] " + FolderFileName + "을 읽지 못했습니다: " + e.Message);
            }
            return null;
        }
    }
}
