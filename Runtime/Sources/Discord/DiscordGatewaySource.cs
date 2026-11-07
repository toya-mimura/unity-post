using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PetPost.Sources
{
    /// <summary>
    /// Discord Gateway に直接つなぎ、新メンバー参加・メンション・Bot宛てDMを受け取る。
    /// 「見るだけ」のBot。メッセージ本文は読まないので Message Content Intent は不要。
    ///
    /// Connects straight to the Discord Gateway (WebSocket) and turns member joins,
    /// mentions and DMs-to-the-bot into PetEvents. Read-only: never sends messages,
    /// never reads message content.
    /// </summary>
    public class DiscordGatewaySource : PetEventSource
    {
        // Gateway intents（ビットフラグ）
        const int IntentGuilds = 1 << 0;          //    1
        const int IntentGuildMembers = 1 << 1;    //    2  特権: 開発者ポータルで Server Members Intent をON
        const int IntentGuildMessages = 1 << 9;   //  512
        const int IntentDirectMessages = 1 << 12; // 4096

        const string DefaultGatewayUrl = "wss://gateway.discord.gg";
        const string GatewayQuery = "?v=10&encoding=json";

        [Header("拾うイベント / What to react to")]
        [Tooltip("サーバーに新しいメンバーが来た")]
        public bool memberJoin = true;

        [Tooltip("あなた（設定ファイルの myUserId）がメンションされた")]
        public bool myMentions = true;

        [Tooltip("Bot自身がメンションされた")]
        public bool botMentions = true;

        [Tooltip("@everyone / @here")]
        public bool everyoneMentions = false;

        [Tooltip("Bot宛てのDM")]
        public bool directMessages = true;

        [Tooltip("サーバー内のあらゆる発言（にぎやかになるので既定はOFF）")]
        public bool anyGuildMessage = false;

        [Header("フィルタ / Filters")]
        [Tooltip("ほかのBotやWebhookの発言・参加を無視する。自作の通知Botでメンションさせたいなら OFF のまま。")]
        public bool ignoreBots = false;

        [Tooltip("設定ファイルの代わりに使うパス（空なら既定の場所）")]
        public string configPathOverride = "";

        public override string DisplayName => "Discord";

        // ---- 接続ごとに引き継ぐ状態（受信スレッドだけが触る） ----
        CancellationTokenSource _cts;
        string _token;
        string _myUserId;
        HashSet<string> _guildFilter;
        string _botUserId;
        string _sessionId;
        string _resumeUrl;
        long? _seq;
        volatile bool _reachedReady;

        enum ConnectionEnd { Reconnect, ReconnectFresh, Fatal }

        protected override void StartSource()
        {
            var cfg = PetPostConfig.Load(configPathOverride);
            if (cfg == null) { SetStatus(SourceStatus.Error, "設定ファイルがありません"); return; }

            var d = cfg.discord;
            if (!d.enabled) { SetStatus(SourceStatus.Stopped, "設定で無効"); return; }
            if (string.IsNullOrWhiteSpace(d.botToken)) { SetStatus(SourceStatus.Error, "botToken が空です"); return; }
            if (myMentions && string.IsNullOrWhiteSpace(d.myUserId))
                Debug.LogWarning("[PetPost/Discord] myUserId が空なので、あなたへのメンションは判定できません。");

            _token = d.botToken.Trim();
            _myUserId = d.myUserId?.Trim();
            _guildFilter = new HashSet<string>();
            foreach (var g in d.guildIds)
                if (!string.IsNullOrWhiteSpace(g)) _guildFilter.Add(g.Trim());

            _sessionId = null; _resumeUrl = null; _seq = null; _botUserId = null;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Run(() => RunAsync(token));
        }

        protected override void StopSource()
        {
            _cts?.Cancel();
            _cts = null;
        }

        int ComputeIntents()
        {
            int intents = IntentGuilds;
            if (memberJoin) intents |= IntentGuildMembers;
            if (myMentions || botMentions || everyoneMentions || anyGuildMessage) intents |= IntentGuildMessages;
            if (directMessages) intents |= IntentDirectMessages;
            return intents;
        }

        // ================= 接続ループ =================

        async Task RunAsync(CancellationToken ct)
        {
            int backoffSec = 1;
            var rng = new System.Random();

            while (!ct.IsCancellationRequested)
            {
                bool resume = _sessionId != null && _resumeUrl != null;
                string url = (resume ? _resumeUrl : DefaultGatewayUrl).TrimEnd('/') + "/" + GatewayQuery;
                SetStatus(resume ? SourceStatus.Reconnecting : SourceStatus.Connecting, resume ? "再開中" : "接続中");
                _reachedReady = false;

                ConnectionEnd end;
                try
                {
                    end = await RunConnectionAsync(url, resume, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PetPost/Discord] 接続エラー: {ex.Message}");
                    end = ConnectionEnd.Reconnect;
                }

                if (end == ConnectionEnd.Fatal) return; // ステータスとログは呼び出し先で設定済み

                if (end == ConnectionEnd.ReconnectFresh)
                {
                    _sessionId = null; _resumeUrl = null; _seq = null;
                }

                // 一度でも READY/RESUMED まで行けた接続のあとは、待ち時間をリセット
                if (_reachedReady) backoffSec = 1;

                SetStatus(SourceStatus.Reconnecting, $"{backoffSec}秒後に再接続");
                try { await Task.Delay(backoffSec * 1000 + rng.Next(0, 1000), ct); }
                catch (OperationCanceledException) { break; }
                backoffSec = Math.Min(backoffSec * 2, 60);
            }
            SetStatus(SourceStatus.Stopped);
        }

        sealed class HeartbeatState
        {
            public volatile bool acked = true;
        }

        async Task<ConnectionEnd> RunConnectionAsync(string url, bool resume, CancellationToken ct)
        {
            using var ws = new ClientWebSocket();
            using var connCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var sendLock = new SemaphoreSlim(1, 1);
            var hb = new HeartbeatState();

            await ws.ConnectAsync(new Uri(url), connCts.Token);

            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    string text = await ReceiveTextAsync(ws, connCts.Token);
                    if (text == null) break; // サーバーが閉じた

                    var payload = PetJson.Parse(text);
                    int op = payload.Value<int>("op");
                    var s = payload["s"];
                    if (s != null && s.Type == JTokenType.Integer) _seq = s.Value<long>();
                    var d = payload["d"];

                    switch (op)
                    {
                        case 10: // Hello
                            int interval = d.Value<int>("heartbeat_interval");
                            _ = HeartbeatLoopAsync(ws, sendLock, hb, interval, connCts.Token);
                            if (resume)
                                await SendAsync(ws, sendLock, new JObject
                                {
                                    ["op"] = 6,
                                    ["d"] = new JObject { ["token"] = _token, ["session_id"] = _sessionId, ["seq"] = _seq },
                                }, connCts.Token);
                            else
                                await SendAsync(ws, sendLock, new JObject
                                {
                                    ["op"] = 2,
                                    ["d"] = new JObject
                                    {
                                        ["token"] = _token,
                                        ["intents"] = ComputeIntents(),
                                        ["properties"] = new JObject
                                        {
                                            ["os"] = "windows", ["browser"] = "petpost", ["device"] = "petpost",
                                        },
                                    },
                                }, connCts.Token);
                            break;

                        case 11: // Heartbeat ACK
                            hb.acked = true;
                            break;

                        case 1: // サーバーから「今すぐ心拍を」
                            await SendHeartbeatAsync(ws, sendLock, connCts.Token);
                            break;

                        case 0: // Dispatch
                            HandleDispatch(PetJson.Str(payload, "t"), d);
                            break;

                        case 7: // Reconnect 要求 → 再開できる
                            await CloseQuietlyAsync(ws);
                            return ConnectionEnd.Reconnect;

                        case 9: // Invalid Session
                            bool resumable = d != null && d.Type == JTokenType.Boolean && d.Value<bool>();
                            await Task.Delay(1000 + new System.Random().Next(0, 4000), ct);
                            await CloseQuietlyAsync(ws);
                            return resumable ? ConnectionEnd.Reconnect : ConnectionEnd.ReconnectFresh;
                    }
                }
            }
            catch (WebSocketException) when (!ct.IsCancellationRequested)
            {
                // ゾンビ接続で Abort した場合などもここに来る → 再開を試みる
                return ConnectionEnd.Reconnect;
            }
            finally
            {
                connCts.Cancel(); // 心拍ループを止める
                if (ct.IsCancellationRequested) await CloseQuietlyAsync(ws, normal: true);
            }

            return InterpretClose(ws.CloseStatus.HasValue ? (int)ws.CloseStatus.Value : -1, ws.CloseStatusDescription);
        }

        ConnectionEnd InterpretClose(int code, string reason)
        {
            switch (code)
            {
                case 4004:
                    Fail("Botトークンが無効です（4004）。開発者ポータルでトークンを再発行して設定ファイルを更新してください。");
                    return ConnectionEnd.Fatal;
                case 4014:
                    Fail("許可されていない Intent です（4014）。開発者ポータルの Bot 設定で Server Members Intent を ON にしてください。");
                    return ConnectionEnd.Fatal;
                case 4010: case 4011: case 4012: case 4013:
                    Fail($"接続設定のエラーです（{code} {reason}）。");
                    return ConnectionEnd.Fatal;
                case 4007: case 4009: case 4003: case 4005:
                    return ConnectionEnd.ReconnectFresh;
                default:
                    if (code >= 0) Debug.Log($"[PetPost/Discord] 切断されました（{code} {reason}）。再接続します。");
                    return ConnectionEnd.Reconnect;
            }
        }

        void Fail(string message)
        {
            Debug.LogError("[PetPost/Discord] " + message);
            SetStatus(SourceStatus.Error, message);
        }

        // ================= 心拍 =================

        async Task HeartbeatLoopAsync(ClientWebSocket ws, SemaphoreSlim sendLock, HeartbeatState hb,
                                      int intervalMs, CancellationToken ct)
        {
            try
            {
                // 最初の1回は interval × 0〜1 のランダムな待ち（Discordの仕様）
                await Task.Delay((int)(intervalMs * new System.Random().NextDouble()), ct);
                while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    if (!hb.acked)
                    {
                        // 前回の心拍に返事がない＝ゾンビ接続。切って再開させる。
                        Debug.Log("[PetPost/Discord] 心拍の応答がないため再接続します。");
                        ws.Abort();
                        return;
                    }
                    hb.acked = false;
                    await SendHeartbeatAsync(ws, sendLock, ct);
                    await Task.Delay(intervalMs, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Debug.LogWarning($"[PetPost/Discord] 心拍エラー: {ex.Message}");
            }
        }

        Task SendHeartbeatAsync(ClientWebSocket ws, SemaphoreSlim sendLock, CancellationToken ct) =>
            SendAsync(ws, sendLock, new JObject { ["op"] = 1, ["d"] = _seq }, ct);

        // ================= イベント処理 =================

        void HandleDispatch(string type, JToken d)
        {
            if (d == null || type == null) return;

            switch (type)
            {
                case "READY":
                    _sessionId = PetJson.Str(d, "session_id");
                    _resumeUrl = PetJson.Str(d, "resume_gateway_url");
                    _botUserId = PetJson.Str(d["user"], "id");
                    _reachedReady = true;
                    SetStatus(SourceStatus.Connected, $"{PetJson.Str(d["user"], "username")} として接続");
                    Debug.Log($"[PetPost/Discord] 接続しました（Bot: {PetJson.Str(d["user"], "username")}）");
                    break;

                case "RESUMED":
                    _reachedReady = true;
                    SetStatus(SourceStatus.Connected, "再開しました");
                    break;

                case "GUILD_MEMBER_ADD":
                    if (memberJoin) OnMemberAdd(d);
                    break;

                case "MESSAGE_CREATE":
                    OnMessageCreate(d);
                    break;
            }
        }

        bool GuildAllowed(string guildId) =>
            _guildFilter.Count == 0 || (guildId != null && _guildFilter.Contains(guildId));

        static string NameOf(JToken user, JToken member = null)
        {
            return PetJson.Str(member, "nick")
                ?? PetJson.Str(user, "global_name")
                ?? PetJson.Str(user, "username")
                ?? "だれか";
        }

        static bool IsBot(JToken user) => user?["bot"]?.Type == JTokenType.Boolean && user["bot"].Value<bool>();

        void OnMemberAdd(JToken d)
        {
            string guildId = PetJson.Str(d, "guild_id");
            if (!GuildAllowed(guildId)) return;

            var user = d["user"];
            if (ignoreBots && IsBot(user)) return;

            Emit(new PetEvent(PetEventSourceKind.Discord, PetEventKind.MemberJoin, "GUILD_MEMBER_ADD",
                              PetJson.Str(user, "id"), NameOf(user, d), guildId));
        }

        void OnMessageCreate(JToken d)
        {
            var author = d["author"];
            string authorId = PetJson.Str(author, "id");
            if (authorId == null || authorId == _botUserId) return;          // 自分(Bot)の発言
            if (ignoreBots && (IsBot(author) || PetJson.Str(d, "webhook_id") != null)) return;

            string guildId = PetJson.Str(d, "guild_id");
            string name = NameOf(author, d["member"]);

            // サーバー外 = Bot宛てDM
            if (guildId == null)
            {
                if (directMessages)
                    Emit(new PetEvent(PetEventSourceKind.Discord, PetEventKind.DirectMessage, "MESSAGE_CREATE:dm",
                                      authorId, name, PetJson.Str(d, "channel_id")));
                return;
            }

            if (!GuildAllowed(guildId)) return;

            // 本文は読まず、mentions 配列だけを見る（Message Content Intent 不要）
            bool mentionsMe = false, mentionsBot = false;
            if (d["mentions"] is JArray mentions)
            {
                foreach (var m in mentions)
                {
                    string id = PetJson.Str(m, "id");
                    if (id == null) continue;
                    if (!string.IsNullOrEmpty(_myUserId) && id == _myUserId) mentionsMe = true;
                    if (id == _botUserId) mentionsBot = true;
                }
            }
            bool mentionsEveryone = d["mention_everyone"]?.Type == JTokenType.Boolean && d["mention_everyone"].Value<bool>();
            bool fromMe = !string.IsNullOrEmpty(_myUserId) && authorId == _myUserId;

            string raw = null;
            if (myMentions && mentionsMe && !fromMe) raw = "MESSAGE_CREATE:me";
            else if (botMentions && mentionsBot) raw = "MESSAGE_CREATE:bot";
            else if (everyoneMentions && mentionsEveryone && !fromMe) raw = "MESSAGE_CREATE:everyone";

            if (raw != null)
            {
                Emit(new PetEvent(PetEventSourceKind.Discord, PetEventKind.Mention, raw, authorId, name, guildId));
            }
            else if (anyGuildMessage && !fromMe)
            {
                Emit(new PetEvent(PetEventSourceKind.Discord, PetEventKind.Message, "MESSAGE_CREATE",
                                  authorId, name, guildId));
            }
        }

        // ================= WebSocket ユーティリティ =================

        static async Task<string> ReceiveTextAsync(ClientWebSocket ws, CancellationToken ct)
        {
            var buffer = new ArraySegment<byte>(new byte[16 * 1024]);
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseQuietlyAsync(ws);
                    return null;
                }
                ms.Write(buffer.Array, buffer.Offset, result.Count);
                if (result.EndOfMessage) break;
            }
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        static async Task SendAsync(ClientWebSocket ws, SemaphoreSlim sendLock, JObject obj, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(obj.ToString(Newtonsoft.Json.Formatting.None));
            await sendLock.WaitAsync(ct);
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
            finally { sendLock.Release(); }
        }

        /// <param name="normal">
        /// true: 1000で閉じる（セッション破棄。アプリ終了時）。
        /// false: 4000で閉じる（セッションを残して再開可能にする）。
        /// </param>
        static async Task CloseQuietlyAsync(ClientWebSocket ws, bool normal = false)
        {
            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    using var timeout = new CancellationTokenSource(2000);
                    var status = normal ? WebSocketCloseStatus.NormalClosure : (WebSocketCloseStatus)4000;
                    await ws.CloseOutputAsync(status, "bye", timeout.Token);
                }
            }
            catch { /* 閉じる途中の失敗は無視 */ }
        }
    }
}
