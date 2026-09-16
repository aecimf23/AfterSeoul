using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AfterSeoul.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace AfterSeoul.Unity
{
    /// <summary>
    /// 앱 수명 = <see cref="GameSession"/> 수명. 이 MonoBehaviour 하나가 세션을 만들어 들고 있다
    /// (ARCHITECTURE §2). Unity 에 닿는 일 — 파일 경로, StreamingAssets, Resources, 앱 수명 이벤트 —
    /// 은 전부 여기서 끝내고, 게임 코드에는 텍스트와 경로만 넘긴다.
    ///
    /// <para><b>씬에 배치하지 않는다.</b> <c>RuntimeInitializeOnLoadMethod</c> 로 스스로 생긴다.
    /// 씬 파일을 손으로 고칠 일이 없고, 어느 씬에서 Play 를 눌러도 같은 경로로 부팅된다.</para>
    /// </summary>
    public sealed class Bootstrap : MonoBehaviour
    {
        /// <summary>UI 가 세션을 찾는 곳. 부팅이 끝나기 전에는 null.</summary>
        public static GameSession Session { get; private set; }

        /// <summary>부팅 실패 사유. UI 가 띄운다. 성공하면 null.</summary>
        public static string BootError { get; private set; }

        /// <summary>부팅 완료. 이미 끝났으면 <see cref="Session"/> 을 바로 쓰면 된다.</summary>
        public static event Action<GameSession> Ready;

        /// <summary>
        /// 앱이 켜져 있는 동안의 정산 주기. 파견 복귀·제작 완료·날짜 경계를 이 간격 안에 반영한다.
        /// 정산은 사건이 없으면 저장도 안 하므로 짧아도 비용이 거의 없다.
        /// </summary>
        private const float TickIntervalSeconds = 5f;

        private float _nextTick;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Create()
        {
            // 에디터에서 도메인 리로드를 끈 경우 이전 Play 의 static 이 남아 있다.
            Session = null;
            BootError = null;

            var go = new GameObject("[Bootstrap]");
            DontDestroyOnLoad(go);
            go.AddComponent<Bootstrap>();

            // 효과음은 파일이 아니라 코드로 만든 파형이다 (Sfx). 여기서 한 번 구워 둔다.
            Sfx.Attach(go);
        }

        private IEnumerator Start()
        {
            var texts = new Dictionary<string, string>();
            foreach (var name in JsonDataRegistry.FileNames)
            {
                string text = null;
                yield return ReadStreamingAsset(Path.Combine("Data", name), t => text = t);
                texts[name] = text;
            }

            try
            {
                LoadLocale(Application.systemLanguage);

                var data = JsonDataRegistry.Load(n => texts.TryGetValue(n, out var t) ? t : null);
                var clock = new SystemClock();
                var saves = new SaveService(
                    new FileStore(SaveDirectory()), new NewtonsoftJsonCodec(), clock);

                var session = new GameSession(saves, data, clock);

                // 결제·광고는 게임 코드 밖이다 (GDD §11, IStore). 스토어 SDK 가 붙기 전까지
                // 기본값은 항상 거절하는 NullStore 이고, 에디터에서만 가짜 상점을 꽂는다 —
                // "일단 되는 척"을 기본값에 두면 그게 스토어 빌드까지 따라간다.
#if UNITY_EDITOR
                session.Store = new DebugStore();

#endif

                session.Boot();
                session.MailLink = new AfterSeoul.Unity.MobileLink.AccountMailLink(session, SeoulLink.UnityMailClient.Instance);
                if (saves.LastLoadError != null)
                    Debug.LogWarning($"[Bootstrap] 세이브가 깨져 새로 시작했다 (save.json.corrupt 로 보관): {saves.LastLoadError}");

                Session = session;
                _nextTick = Time.unscaledTime + TickIntervalSeconds;
                Ready?.Invoke(session);
            }
            catch (Exception e)
            {
                BootError = e.Message;
                Debug.LogException(e);
            }
        }

        private static string SaveDirectory()
        {
#if UNITY_EDITOR
            // Explicit editor-only isolated manual QA session; normal player saves are untouched.
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-afterSeoulPlaytestSave")
                {
                    var path = Path.GetFullPath(args[i + 1]);
                    Directory.CreateDirectory(path);
                    Debug.Log("[Playtest] Isolated save: " + path);
                    return path;
                }
#endif
            return Application.persistentDataPath;
        }
        private void Update()
        {
            if (Session == null || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + TickIntervalSeconds;
            Session.Tick();
        }

        private void OnApplicationPause(bool paused)
        {
            if (Session == null) return;

            if (paused)
            {
                Session.Suspend();
                // 나가는 길에 알림을 건다. 앱이 켜져 있는 동안 거는 건 의미가 없고,
                // 여기서 걸어야 "지금 상태"가 반영된다.
                AndroidNotifications.Reschedule(Session);
            }
            else
            {
                // 돌아왔으면 예약을 지운다. 방금 눈으로 본 것을 다시 울리면 잔소리가 된다.
                AndroidNotifications.CancelAll();
                Session.Resume();
                if (Session.Save.Mail.Linked)
                    (Session.MailLink as AfterSeoul.Mail.IAccountMailLink)?.Sync((ok, message) => { });
            }
        }

        private void OnApplicationQuit()
        {
            if (Session == null) return;
            Session.Suspend();
            AndroidNotifications.Reschedule(Session);
        }

        // ── Unity 에 닿는 읽기 ────────────────────────────────────

        private static IEnumerator ReadStreamingAsset(string relativePath, Action<string> done)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath);

            // Android 는 StreamingAssets 가 apk(jar:file://) 안에 있어 File 로 못 읽는다.
            if (path.Contains("://"))
            {
                using (var req = UnityWebRequest.Get(path))
                {
                    yield return req.SendWebRequest();
                    done(req.result == UnityWebRequest.Result.Success ? req.downloadHandler.text : null);
                }
            }
            else
            {
                done(File.Exists(path) ? File.ReadAllText(path) : null);
            }
        }

        public static readonly string[] Languages = { "ko", "en", "jp", "zh", "ru" };
        public static readonly string[] LanguageNames = { "한국어", "English", "日本語", "简体中文", "Русский" };

        public static void SelectLanguage(string code)
        {
            if (Array.IndexOf(Languages, code) < 0) return;
            PlayerPrefs.SetString("AfterSeoul.Language", code);
            PlayerPrefs.Save();
            LoadLanguage(code);
            UI.Theme.ReloadFont();
        }

        private static void LoadLocale(SystemLanguage system)
        {
            LoadLanguage(PlayerPrefs.GetString("AfterSeoul.Language", LanguageCode(system)));
        }

        public static void LoadLanguage(string language)
        {
            string lang = Array.IndexOf(Languages, language) >= 0 ? language : Loc.FallbackLanguage;
            var table = Resources.Load<TextAsset>("Locales/" + lang);
            if (table == null)
            {
                lang = Loc.FallbackLanguage;
                table = Resources.Load<TextAsset>("Locales/" + lang);
            }
            var fallback = Resources.Load<TextAsset>("Locales/" + Loc.FallbackLanguage);

            // 모바일 전용 문구는 따로 있다. 본편 추출이 Locales/*.json 을 통째로 덮어쓰기 때문에
            // 같은 파일에 적으면 데이터를 다시 뽑는 순간 대사가 조용히 사라진다.
            var mobile = Resources.Load<TextAsset>("Locales/mobile/" + lang);
            var mobileFallback = Resources.Load<TextAsset>("Locales/mobile/" + Loc.FallbackLanguage);

            Loc.Load(lang,
                table != null ? table.text : null,
                fallback != null ? fallback.text : null,
                mobile != null ? mobile.text : null,
                mobileFallback != null ? mobileFallback.text : null);
            UI.Theme.ReloadFont();
        }

        /// <summary>본편 로케일 파일명 규칙(ko/en/jp/zh/ru)에 맞춘다. 일본어가 ja 가 아니라 jp 다.</summary>
        private static string LanguageCode(SystemLanguage system)
        {
            switch (system)
            {
                case SystemLanguage.Korean: return "ko";
                case SystemLanguage.Japanese: return "jp";
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return "zh";
                case SystemLanguage.Russian: return "ru";
                default: return Loc.FallbackLanguage;
            }
        }
    }
}
