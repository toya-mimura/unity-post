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

        public DiscordSection discord = new DiscordSection();
        public BlueskySection bluesky = new BlueskySection();

        public static string DefaultPath => Path.Combine(Application.persistentDataPath, FileName);

        static PetPostConfig s_cached;

        /// <summary>
        /// 設定を読む。ファイルがなければ雛形を書き出して null を返す。
        /// Returns null (and writes a template) when the file does not exist yet.
        /// </summary>
        public static PetPostConfig Load(string overridePath = null, bool forceReload = false)
        {
            if (s_cached != null && !forceReload && string.IsNullOrEmpty(overridePath)) return s_cached;

            string path = string.IsNullOrEmpty(overridePath) ? DefaultPath : overridePath;
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
                cfg.discord ??= new DiscordSection();
                cfg.bluesky ??= new BlueskySection();
                cfg.discord.guildIds ??= new string[0];
                if (string.IsNullOrEmpty(overridePath)) s_cached = cfg;
                return cfg;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UnityPost] 設定ファイルを読めませんでした: {path}\n{ex.Message}");
                return null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_cached = null;
    }
}
