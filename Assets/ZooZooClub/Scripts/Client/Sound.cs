using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ZooZooClub.Client
{
    /// <summary>
    /// 배경음과 효과음. 소리 파일은 Resources/Audio 에 두고 이름으로 부른다(같은 이름의 파일로 바꿔 끼우면 된다).
    ///   bgm_main      마을 배경음(반복)
    ///   sfx_click     버튼 누름
    ///   sfx_harvest   수확
    ///   sfx_coin      코인을 받음(주문 납품, 판매)
    ///   sfx_correct   문제 정답
    ///   sfx_wrong     문제 오답
    ///   sfx_raid      침략 시작 경보
    ///   sfx_build     건물·장식 놓기
    ///   sfx_win       침략 방어 성공, 레벨 업
    /// 파일이 없으면 그 소리만 조용히 건너뛴다. 소리 끄기 설정은 PlayerPrefs에 남는다.
    /// 브라우저는 사용자가 한 번 클릭하기 전에는 소리를 못 내므로, 배경음은 로그인(클릭) 뒤에 시작한다.
    /// </summary>
    public sealed class Sound : MonoBehaviour
    {
        public const string Click = "sfx_click";
        public const string Harvest = "sfx_harvest";
        public const string Coin = "sfx_coin";
        public const string Correct = "sfx_correct";
        public const string Wrong = "sfx_wrong";
        public const string Raid = "sfx_raid";
        public const string Build = "sfx_build";
        public const string Win = "sfx_win";
        const string MainMusic = "bgm_main";

        const string MutedKey = "zoo.muted";
        const float MusicVolume = 0.45f;
        const float EffectVolume = 0.8f;

#if UNITY_WEBGL && !UNITY_EDITOR
        // 브라우저에서는 배경음을 페이지 옆의 파일로 직접 튼다(Plugins/WebGL/ZooWebMusic.jslib). 웹 빌드가 그 파일을 복사해 둔다.
        [DllImport("__Internal")] static extern void ZooWebMusicPlay(string url, float volume);
        [DllImport("__Internal")] static extern void ZooWebMusicStop();
        [DllImport("__Internal")] static extern void ZooWebMusicMute(int muted);
        const bool WebMusic = true;
#else
        static void ZooWebMusicPlay(string url, float volume) { }
        static void ZooWebMusicStop() { }
        static void ZooWebMusicMute(int muted) { }
        const bool WebMusic = false;
#endif
        public const string WebMusicFile = "bgm_main.ogg";

        static Sound instance;
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        AudioSource music;
        AudioSource effects;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
            Clips.Clear();
        }

        /// <summary>소리를 끄면 배경음이 멈추고 효과음도 나지 않는다. 설정은 이 컴퓨터에 남는다.</summary>
        public static bool Muted
        {
            get { return PlayerPrefs.GetInt(MutedKey, 0) != 0; }
            set
            {
                PlayerPrefs.SetInt(MutedKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (instance != null) instance.ApplyMute();
            }
        }

        /// <summary>없으면 만든다. parent 아래에 두어 장면과 함께 산다.</summary>
        public static Sound Ensure(Transform parent)
        {
            if (instance != null) return instance;
            var go = new GameObject("Sound");
            go.transform.SetParent(parent, false);
            instance = go.AddComponent<Sound>();
            instance.music = go.AddComponent<AudioSource>();
            instance.music.loop = true;
            instance.music.playOnAwake = false;
            instance.music.volume = MusicVolume;
            instance.effects = go.AddComponent<AudioSource>();
            instance.effects.playOnAwake = false;
            instance.effects.volume = EffectVolume;
            instance.ApplyMute();

            // 브라우저에서는 소리 데이터가 뒤늦게 읽히므로, 미리 읽어 두어야 눌렀을 때 바로 난다(안 읽힌 채 틀면 오류가 난다).
            foreach (string name in new[] { Click, Harvest, Coin, Correct, Wrong, Raid, Build, Win, MainMusic })
            {
                if (WebMusic && name == MainMusic) continue;
                var clip = Load(name);
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            }
            return instance;
        }

        /// <summary>효과음 하나를 낸다. 소리가 꺼져 있거나 파일이 없으면 아무 일도 없다.</summary>
        public static void Play(string name)
        {
            if (instance == null || Muted) return;
            var clip = Load(name);
            if (clip == null) return;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                // 아직 읽는 중이면 이번 소리는 건너뛴다(다음에는 난다).
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                return;
            }
            instance.effects.PlayOneShot(clip);
        }

        /// <summary>마을 배경음을 튼다. 이미 나오고 있으면 그대로 둔다.</summary>
        public static void PlayMusic()
        {
            if (instance == null) return;
            if (WebMusic)
            {
                ZooWebMusicPlay(WebMusicFile, MusicVolume);
                instance.ApplyMute();
                return;
            }
            var clip = Load(MainMusic);
            if (clip == null) return;
            instance.wantMusic = true;
            instance.ApplyMute();
        }

        bool wantMusic;

        void Update()
        {
            if (!wantMusic || music.isPlaying) return;
            var clip = Load(MainMusic);
            if (clip == null) { wantMusic = false; return; }
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            if (clip.loadState != AudioDataLoadState.Loaded) return;
            music.clip = clip;
            music.Play();
        }

        public static void StopMusic()
        {
            if (instance == null) return;
            instance.wantMusic = false;
            instance.music.Stop();
            ZooWebMusicStop();
        }

        void ApplyMute()
        {
            music.mute = Muted;
            effects.mute = Muted;
            ZooWebMusicMute(Muted ? 1 : 0);
        }

        static AudioClip Load(string name)
        {
            AudioClip clip;
            if (Clips.TryGetValue(name, out clip)) return clip;
            clip = Resources.Load<AudioClip>("Audio/" + name);
            Clips[name] = clip;
            return clip;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
