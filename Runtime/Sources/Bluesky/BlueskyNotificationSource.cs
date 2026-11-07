using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace PetPost.Sources
{
    /// <summary>
    /// Bluesky（AT Protocol）の通知を定期的に取得して、いいね・リポスト・フォロー・
    /// メンション・リプライ・引用を PetEvent にする。
    /// 認証はアプリパスワード。通知の「既読」は付けないので、スマホのバッジはそのまま残る。
    ///
    /// Polls app.bsky.notification.listNotifications with an app password and turns new
    /// notifications into PetEvents. Never marks notifications as read.
    /// </summary>
    public class BlueskyNotificationSource : PetEventSource
    {
        [Header("取得 / Polling")]
        [Tooltip("通知を確認する間隔（秒）。短くしすぎない。")]
        [Min(10f)] public float pollSeconds = 30f;

        [Tooltip("反応する通知の種類（複数選択可）")]
        public PetEventKind acceptKinds =
            PetEventKind.Like | PetEventKind.Repost | PetEventKind.Follow |
            PetEventKind.Mention | PetEventKind.Reply | PetEventKind.Quote;

        [Tooltip("設定ファイルの代わりに使うパス（空なら既定の場所）")]
        public string configPathOverride = "";

        public override string DisplayName => "Bluesky";

        PetPostConfig.BlueskySection _cfg;
        string _pds;
        string _accessJwt;
        string _refreshJwt;
        bool _baselineTaken;
        readonly HashSet<string> _seen = new HashSet<string>();
        readonly Queue<string> _seenOrder = new Queue<string>();

        sealed class HttpResult
        {
            public long code;
            public string body;
            public string networkError;
            public bool Ok => networkError == null && code >= 200 && code < 300;

            public string XrpcError
            {
                get
                {
                    if (string.IsNullOrEmpty(body)) return null;
                    try { return PetJson.Str(PetJson.Parse(body), "error"); }
                    catch { return null; }
                }
            }
        }

        protected override void StartSource()
        {
            var cfg = PetPostConfig.Load(configPathOverride);
            if (cfg == null) { SetStatus(SourceStatus.Error, "設定ファイルがありません"); return; }

            _cfg = cfg.bluesky;
            if (!_cfg.enabled) { SetStatus(SourceStatus.Stopped, "設定で無効"); return; }
            if (string.IsNullOrWhiteSpace(_cfg.identifier) || string.IsNullOrWhiteSpace(_cfg.appPassword))
            {
                SetStatus(SourceStatus.Error, "identifier / appPassword が空です");
                return;
            }

            _accessJwt = _refreshJwt = null;
            _pds = (_cfg.service ?? "https://bsky.social").TrimEnd('/');
            _baselineTaken = false;
            _seen.Clear(); _seenOrder.Clear();
            StartCoroutine(RunLoop());
        }

        protected override void StopSource()
        {
            StopAllCoroutines();
        }

        // ================= メインループ =================

        IEnumerator RunLoop()
        {
            float backoff = 15f;
            var r = new HttpResult();

            while (true)
            {
                // --- ログイン ---
                if (_accessJwt == null)
                {
                    SetStatus(SourceStatus.Connecting, "ログイン中");
                    yield return CreateSession(r);
                    if (!r.Ok)
                    {
                        if (r.XrpcError == "AuthFactorTokenRequired")
                        {
                            Fail("2段階認証のコードを求められました。普段のパスワードではなく「アプリパスワード」を設定してください。");
                            yield break;
                        }
                        if (r.code == 401)
                        {
                            Fail("ログインに失敗しました。identifier とアプリパスワードを確認してください。");
                            yield break; // 誤ったパスワードで連打しない（ログインAPIは回数制限が厳しい）
                        }
                        SetStatus(SourceStatus.Reconnecting, $"ログイン再試行まで {backoff:0}秒（{Describe(r)}）");
                        yield return new WaitForSecondsRealtime(backoff);
                        backoff = Mathf.Min(backoff * 2f, 600f);
                        continue;
                    }
                }

                // --- 通知取得 ---
                yield return ListNotifications(r);

                if (!r.Ok && IsExpired(r))
                {
                    yield return RefreshSession(r);
                    if (!r.Ok) _accessJwt = null; // 次の周回でログインし直す
                    continue;
                }

                if (!r.Ok)
                {
                    SetStatus(SourceStatus.Reconnecting, $"取得失敗、{backoff:0}秒後に再試行（{Describe(r)}）");
                    yield return new WaitForSecondsRealtime(backoff);
                    backoff = Mathf.Min(backoff * 2f, 600f);
                    continue;
                }

                backoff = 15f;
                ProcessNotifications(r.body);
                SetStatus(SourceStatus.Connected, $"{pollSeconds:0}秒ごとに確認中");
                yield return new WaitForSecondsRealtime(pollSeconds);
            }
        }

        static bool IsExpired(HttpResult r)
        {
            if (r.code == 401) return true;
            var err = r.XrpcError;
            return err == "ExpiredToken" || err == "InvalidToken";
        }

        static string Describe(HttpResult r) =>
            r.networkError ?? $"HTTP {r.code} {r.XrpcError}";

        void Fail(string message)
        {
            Debug.LogError("[PetPost/Bluesky] " + message);
            SetStatus(SourceStatus.Error, message);
        }

        // ================= XRPC 呼び出し =================

        IEnumerator CreateSession(HttpResult r)
        {
            var body = new JObject { ["identifier"] = _cfg.identifier.Trim(), ["password"] = _cfg.appPassword.Trim() };
            string service = (_cfg.service ?? "https://bsky.social").TrimEnd('/');
            yield return Send("POST", service + "/xrpc/com.atproto.server.createSession", null, body.ToString(), r);
            if (r.Ok) ApplySession(r.body, isLogin: true);
        }

        IEnumerator RefreshSession(HttpResult r)
        {
            if (_refreshJwt == null) { r.code = 401; r.networkError = null; yield break; }
            yield return Send("POST", _pds + "/xrpc/com.atproto.server.refreshSession", _refreshJwt, null, r);
            if (r.Ok) ApplySession(r.body, isLogin: false);
        }

        IEnumerator ListNotifications(HttpResult r)
        {
            yield return Send("GET", _pds + "/xrpc/app.bsky.notification.listNotifications?limit=50", _accessJwt, null, r);
        }

        void ApplySession(string json, bool isLogin)
        {
            var o = PetJson.Parse(json);
            _accessJwt = PetJson.Str(o, "accessJwt");
            _refreshJwt = PetJson.Str(o, "refreshJwt");

            // アカウントが置かれている PDS を DID ドキュメントから取り出す
            if (o["didDoc"]?["service"] is JArray services)
            {
                foreach (var s in services)
                {
                    string id = PetJson.Str(s, "id");
                    if (id != null && id.EndsWith("#atproto_pds"))
                    {
                        string ep = PetJson.Str(s, "serviceEndpoint");
                        if (!string.IsNullOrEmpty(ep)) _pds = ep.TrimEnd('/');
                        break;
                    }
                }
            }
            if (isLogin) Debug.Log($"[PetPost/Bluesky] ログインしました（@{PetJson.Str(o, "handle")}）");
        }

        static IEnumerator Send(string method, string url, string bearer, string jsonBody, HttpResult r)
        {
            using var req = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = 20 };
            if (jsonBody != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (bearer != null) req.SetRequestHeader("Authorization", "Bearer " + bearer);

            yield return req.SendWebRequest();

            r.code = req.responseCode;
            r.body = req.downloadHandler?.text;
            r.networkError = req.result == UnityWebRequest.Result.ConnectionError ? req.error : null;
        }

        // ================= 新着判定 =================

        void ProcessNotifications(string json)
        {
            if (!(PetJson.Parse(json)["notifications"] is JArray arr)) return;

            // 新しい順に来るので、古い順に並べ直して投函する
            var list = new List<JToken>(arr);
            list.Reverse();

            foreach (var n in list)
            {
                string uri = PetJson.Str(n, "uri");
                if (uri == null || _seen.Contains(uri)) continue;
                Remember(uri);

                // 起動直後の1回目は「既に届いていた分」なので反応しない
                if (!_baselineTaken) continue;

                string reason = PetJson.Str(n, "reason");
                var kind = MapReason(reason);
                if ((acceptKinds & kind) == 0) continue;

                var author = n["author"];
                string name = PetJson.Str(author, "displayName");
                if (string.IsNullOrWhiteSpace(name)) name = "@" + PetJson.Str(author, "handle");

                Emit(new PetEvent(PetEventSourceKind.Bluesky, kind, reason,
                                  PetJson.Str(author, "did"), name,
                                  PetJson.Str(n, "reasonSubject") ?? uri));
            }
            _baselineTaken = true;
        }

        void Remember(string uri)
        {
            _seen.Add(uri);
            _seenOrder.Enqueue(uri);
            while (_seenOrder.Count > 500) _seen.Remove(_seenOrder.Dequeue());
        }

        static PetEventKind MapReason(string reason)
        {
            switch (reason)
            {
                case "like":
                case "like-via-repost": return PetEventKind.Like;
                case "repost":
                case "repost-via-repost": return PetEventKind.Repost;
                case "follow": return PetEventKind.Follow;
                case "mention": return PetEventKind.Mention;
                case "reply": return PetEventKind.Reply;
                case "quote": return PetEventKind.Quote;
                default: return PetEventKind.Other;
            }
        }
    }
}
