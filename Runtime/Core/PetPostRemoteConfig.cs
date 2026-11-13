using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace PetPost
{
    /// <summary>
    /// ローカルに petpost_config.json が無いとき、cf-unity-hub（Cloudflare Workers）から設定を取得する。
    ///
    /// ・優先順位：① ローカルの petpost_config.json → ② Workers（このコンポーネント）
    /// ・認証は Cloudflare Access の Service Token（端末ごとに発行・個別に無効化できる）
    /// ・取得した設定はメモリにだけ持ち、ファイルには書き出さない
    /// ・Bluesky のアプリパスワードは受け取らない（cf-unity-hub 側で監視し、HubPetEventSource で届く）
    ///
    /// UnityPost オブジェクト（PetEventHub と同じ GameObject など）に付けておく。
    /// 他の受信元より先に動く必要があるので、実行順を早めてある。
    ///
    /// Fetches the config from cf-unity-hub when no local petpost_config.json exists.
    /// Authenticates with a per-device Cloudflare Access Service Token. Memory only.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PetPostRemoteConfig : PetEventSource
    {
        public const string EnvClientId = "PETPOST_ACCESS_CLIENT_ID";
        public const string EnvClientSecret = "PETPOST_ACCESS_CLIENT_SECRET";

        [Header("cf-unity-hub")]
        [Tooltip("例: https://cf-unity-hub.xxxx.workers.dev（末尾スラッシュなし）")]
        public string hubBaseUrl = "";

        [Header("Cloudflare Access の Service Token（この端末用）")]
        [Tooltip("空なら環境変数 " + EnvClientId + " を使う")]
        public string accessClientId = "";
        [Tooltip("空なら環境変数 " + EnvClientSecret + " を使う。公開リポジトリにコミットしないこと")]
        public string accessClientSecret = "";

        [Header("動作")]
        [Tooltip("ローカルの petpost_config.json があっても Workers の設定を使う（動作確認用）")]
        public bool ignoreLocalFile = false;

        public override string DisplayName => "設定（Workers）";

        bool _useRemote;

        void Awake()
        {
            _useRemote = ignoreLocalFile || !PetPostConfig.LocalFileExists;
            if (_useRemote) PetPostConfig.ExpectRemote(ignoreLocalFile);
        }

        protected override void StartSource()
        {
            if (!_useRemote)
            {
                SetStatus(SourceStatus.Stopped, "ローカルの petpost_config.json を使用中");
                return;
            }
            if (PetPostConfig.Origin == PetPostConfig.ConfigOrigin.Remote)
            {
                SetStatus(SourceStatus.Connected, "Workers から取得済み");
                return;
            }
            StartCoroutine(FetchLoop());
        }

        protected override void StopSource()
        {
            StopAllCoroutines();
        }

        IEnumerator FetchLoop()
        {
            string id = FirstNonEmpty(accessClientId, Environment.GetEnvironmentVariable(EnvClientId));
            string secret = FirstNonEmpty(accessClientSecret, Environment.GetEnvironmentVariable(EnvClientSecret));

            if (string.IsNullOrWhiteSpace(hubBaseUrl)) { Fail("hubBaseUrl が空です"); yield break; }
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret))
            {
                Fail($"Service Token がありません（インスペクターか環境変数 {EnvClientId} / {EnvClientSecret} に設定）");
                yield break;
            }

            string url = hubBaseUrl.Trim().TrimEnd('/') + "/device/petpost-config";
            float backoff = 5f;

            while (true)
            {
                SetStatus(SourceStatus.Connecting, "Workers から設定を取得中");
                using (var req = UnityWebRequest.Get(url))
                {
                    req.timeout = 15;
                    req.redirectLimit = 0; // Access に拒否されるとログイン画面へリダイレクトされるので、追わずに失敗とみなす
                    req.SetRequestHeader("CF-Access-Client-Id", id.Trim());
                    req.SetRequestHeader("CF-Access-Client-Secret", secret.Trim());
                    req.SetRequestHeader("Accept", "application/json");
                    yield return req.SendWebRequest();

                    string error = Validate(req, out PetPostConfig cfg);
                    if (error == null)
                    {
                        PetPostConfig.SetRemote(cfg);
                        SetStatus(SourceStatus.Connected,
                            $"Workers から取得済み（Discord: {(cfg.discord.enabled ? "有効" : "無効")}" +
                            $" / Bluesky: {(cfg.hub.blueskyViaHub ? "Hub 経由" : "なし")}）");
                        Debug.Log("[UnityPost] cf-unity-hub から設定を取得しました（メモリのみ）");
                        yield break;
                    }

                    // 認証エラーは設定を直すまで直らないので、間隔を長めにとる
                    bool authError = req.responseCode == 401 || req.responseCode == 403 ||
                                     (req.responseCode >= 300 && req.responseCode < 400);
                    float wait = authError ? 120f : backoff;
                    Debug.LogWarning($"[UnityPost] 設定の取得に失敗: {error}（{wait:0}秒後に再試行）");
                    SetStatus(authError ? SourceStatus.Error : SourceStatus.Reconnecting, $"{error}（{wait:0}秒後に再試行）");
                    yield return new WaitForSecondsRealtime(wait);
                    backoff = Mathf.Min(backoff * 2f, 60f);
                }
            }
        }

        static string Validate(UnityWebRequest req, out PetPostConfig cfg)
        {
            cfg = null;
            if (req.result == UnityWebRequest.Result.ConnectionError) return "接続できません: " + req.error;
            long code = req.responseCode;
            if (code >= 300 && code < 400) return "Service Token が拒否されました（Access のログイン画面へリダイレクト）";
            if (code == 401 || code == 403) return $"Service Token が拒否されました（HTTP {code}）";
            if (code != 200) return $"HTTP {code}";

            string body = req.downloadHandler?.text ?? "";
            if (!body.TrimStart().StartsWith("{")) return "JSON ではない応答が返りました（Access の設定を確認）";
            try
            {
                cfg = JsonUtility.FromJson<PetPostConfig>(body);
            }
            catch (Exception e)
            {
                return "JSON を読めませんでした: " + e.Message;
            }
            return cfg == null ? "空の応答" : null;
        }

        void Fail(string message)
        {
            Debug.LogError("[UnityPost] " + message);
            SetStatus(SourceStatus.Error, message);
        }

        static string FirstNonEmpty(string a, string b) => string.IsNullOrWhiteSpace(a) ? b : a;
    }
}
