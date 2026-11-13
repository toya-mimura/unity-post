using System;
using System.IO;
using UnityEngine;

namespace PetPost
{
    /// <summary>
    /// 秘密情報（Botトークン・アプリパスワード）の読み込み。
    /// ファイルは Assets の外（Application.persistentDataPath）に置くので、
    /// ビルドにもGitにも入らない。
    ///
    /// Loads secrets from a JSON file outside Assets, so they never end up in a build or repo.
    /// Windows: %USERPROFILE%\AppData\LocalLow\&lt;CompanyName&gt;\&lt;ProductName&gt;\petpost_config.json
    ///
    /// シーンに PetPostRemoteConfig があり、ローカルのファイルが無い場合は、
    /// cf-unity-hub（Cloudflare Workers）から取得した設定を使う（ファイルには書き出さない）。
    /// When a PetPostRemoteConfig is in the scene and no local file exists, the config is fetched
    /// from cf-unity-hub and kept in memory only.
    /// </summary>
    [Serializable]
    public class PetPostConfig
    {
        public const string FileName = "petpost_config.json";

        [Serializable]
        public class DiscordSection
        {
            public bool enabled = true;
            public string botToken = "";
            [Tooltip("あなた自身のDiscordユーザーID（メンション判定に使う）")]
            public string myUserId = "";
            [Tooltip("反応するサーバーIDの一覧。空なら Bot がいる全サーバー。")]
            public string[] guildIds = new string[0];
        }

        [Serializable]
        public class BlueskySection
        {
            public bool enabled = true;
            [Tooltip("ハンドル（example.bsky.social）またはメールアドレス")]
            public string identifier = "";
            [Tooltip("設定 → プライバシーとセキュリティ → アプリパスワード で作ったもの。普段のパスワードは入れない。")]
            public string appPassword = "";
            public string service = "https://bsky.social";
        }

        /// <summary>cf-unity-hub から取得したときだけ入る情報。</summary>
        [Serializable]
        public class HubSection
        {
            [Tooltip("Bluesky の通知を cf-unity-hub 側で監視している（HubPetEventSource で受け取る）")]
            public bool blueskyViaHub;
        }

        public DiscordSection discord = new DiscordSection();
        public BlueskySection bluesky = new BlueskySection();
        public HubSection hub = new HubSection();

        // ================= どこから読んだか =================

        public enum ConfigOrigin
        {
            /// <summary>まだ読んでいない / 見つからない</summary>
            None,
            /// <summary>ローカルの petpost_config.json</summary>
            Local,
            /// <summary>cf-unity-hub（Workers）から取得待ち</summary>
            RemotePending,
            /// <summary>cf-unity-hub（Workers）から取得済み（メモリのみ）</summary>
            Remote,
        }

        public static ConfigOrigin Origin { get; private set; } = ConfigOrigin.None;

        public static bool LocalFileExists => File.Exists(DefaultPath);

        /// <summary>Workers からの取得を待っている（受信元はこの間「取得中」で待つ）</summary>
        public static bool IsRemotePending => Origin == ConfigOrigin.RemotePending;

        static bool s_forceRemote;
        static Action s_onRemoteReady;

        /// <summary>
        /// PetPostRemoteConfig が Awake で呼ぶ。「設定は Workers から来る」と宣言する。
        /// forceRemote が true ならローカルのファイルがあっても使わない。
        /// </summary>
        public static void ExpectRemote(bool forceRemote)
        {
            s_forceRemote = forceRemote;
            if (Origin != ConfigOrigin.Remote)
            {
                s_cached = null;
                Origin = ConfigOrigin.RemotePending;
            }
        }

        /// <summary>Workers から取得した設定を登録し、待っていた受信元を起動する。</summary>
        public static void SetRemote(PetPostConfig cfg)
        {
            Normalize(cfg);
            s_cached = cfg;
            Origin = ConfigOrigin.Remote;
            var callbacks = s_onRemoteReady;
            s_onRemoteReady = null;
            callbacks?.Invoke();
        }

        /// <summary>Workers からの設定が届いたら呼ぶ（届いていれば即呼ぶ）。</summary>
        public static void WhenRemoteReady(Action callback)
        {
            if (callback == null) return;
            if (Origin == ConfigOrigin.Remote) callback();
            else s_onRemoteReady += callback;
        }

        public static void CancelWhenRemoteReady(Action callback)
        {
            if (callback != null) s_onRemoteReady -= callback;
        }

        static void Normalize(PetPostConfig cfg)
        {
            cfg.discord ??= new DiscordSection();
            cfg.bluesky ??= new BlueskySection();
            cfg.hub ??= new HubSection();
            cfg.discord.guildIds ??= new string[0];
        }

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        static PetPostConfig s_cached;

        /// <summary>
        /// 設定を読む。ファイルがなければ雛形を書き出して null を返す。
        /// Returns null (and writes a template) when the file does not exist yet.
        /// </summary>
        public static PetPostConfig Load(string overridePath = null, bool forceReload = false)
        {
            if (s_cached != null && !forceReload && string.IsNullOrEmpty(overridePath)) return s_cached;

            bool useDefault = string.IsNullOrEmpty(overridePath);

            // Workers から取得する設定のとき（ローカルを使わない / ローカルが無い）は、取得結果を待つ
            if (useDefault && (Origin == ConfigOrigin.RemotePending || Origin == ConfigOrigin.Remote))
            {
                if (s_forceRemote || !LocalFileExists) return s_cached; // 取得前は null
            }

            string path = useDefault ? DefaultPath : overridePath;
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, JsonUtility.ToJson(new PetPostConfig(), true));
                    Debug.LogWarning($"[UnityPost] 設定ファイルの雛形を作りました。値を入れて再生し直してください:\n{path}");
                    return null;
                }

                var cfg = JsonUtility.FromJson<PetPostConfig>(File.ReadAllText(path)) ?? new PetPostConfig();
                Normalize(cfg);
                if (useDefault)
                {
                    s_cached = cfg;
                    Origin = ConfigOrigin.Local;
                }
                return cfg;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityPost] 設定ファイルを読めませんでした: {path}\n{ex.Message}");
                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_cached = null;
            s_forceRemote = false;
            s_onRemoteReady = null;
            Origin = ConfigOrigin.None;
        }
    }
}
