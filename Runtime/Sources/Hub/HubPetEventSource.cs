using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace PetPost.Sources
{
    /// <summary>
    /// cf-unity-hub（Cloudflare Workers）から WebSocket で届く "pet" イベントを PetEvent にする受信元。
    /// いまは Bluesky の通知（Hub 側で監視）が届く。Hub 側に受信元を足せば、Unity 側はこのまま使える。
    ///
    /// cf-unity-hub の CloudflareHubClient には実行時につなぐ（型を直接参照しない）ので、
    /// cf-unity-hub を入れていないプロジェクトでもコンパイルエラーにはならない。
    ///
    /// Receives "pet" events pushed by cf-unity-hub over its WebSocket. Binds to
    /// CfUnityHub.CloudflareHubClient at runtime via reflection, so this compiles without cf-unity-hub.
    /// </summary>
    public class HubPetEventSource : PetEventSource
    {
        const string HubTypeName = "CfUnityHub.CloudflareHubClient";

        [Tooltip("cf-unity-hub の CloudflareHubClient。空ならシーンから自動で探す")]
        public MonoBehaviour hub;

        [Tooltip("反応する種類（複数選択可）")]
        public PetEventKind acceptKinds =
            PetEventKind.Like | PetEventKind.Repost | PetEventKind.Follow |
            PetEventKind.Mention | PetEventKind.Reply | PetEventKind.Quote;

        [Tooltip("ローカルの設定ファイルで Bluesky を直接受信しているときは、Hub からの Bluesky イベントを捨てる（二重反応を防ぐ）")]
        public bool skipBlueskyWhenLocalActive = true;

        public override string DisplayName => "Hub 経由（Bluesky など）";

        EventInfo _messageEvent, _connectionEvent;
        Action<string, string> _onMessage;
        Action<bool> _onConnection;

        protected override void StartSource()
        {
            if (hub == null) hub = FindHub();
            if (hub == null || hub.GetType().FullName != HubTypeName)
            {
                SetStatus(SourceStatus.Error, "cf-unity-hub の CloudflareHubClient が見つかりません");
                return;
            }

            var type = hub.GetType();
            _messageEvent = type.GetEvent("MessageReceived");
            _connectionEvent = type.GetEvent("ConnectionChanged");
            if (_messageEvent == null)
            {
                SetStatus(SourceStatus.Error, "CloudflareHubClient が古い版です（MessageReceived がありません）。cf-unity-hub を更新してください");
                return;
            }

            Unbind();
            _onMessage = OnMessage;
            _onConnection = OnConnectionChanged;
            _messageEvent.AddEventHandler(hub, _onMessage);
            _connectionEvent?.AddEventHandler(hub, _onConnection);

            var isConnected = type.GetProperty("IsConnected");
            OnConnectionChanged(isConnected != null && (bool)isConnected.GetValue(hub));
        }

        protected override void StopSource() => Unbind();

        void Unbind()
        {
            if (hub == null) return;
            if (_onMessage != null) _messageEvent?.RemoveEventHandler(hub, _onMessage);
            if (_onConnection != null) _connectionEvent?.RemoveEventHandler(hub, _onConnection);
            _onMessage = null;
            _onConnection = null;
        }

        static MonoBehaviour FindHub()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (mb != null && mb.GetType().FullName == HubTypeName) return mb;
            return null;
        }

        void OnConnectionChanged(bool connected)
        {
            if (connected) SetStatus(SourceStatus.Connected, "Hub に接続中");
            else SetStatus(SourceStatus.Reconnecting, "Hub に再接続中");
        }

        void OnMessage(string type, string json)
        {
            if (type != "pet") return;

            JObject o;
            try { o = JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }); }
            catch { return; }
            if (o == null) return;

            if (!Enum.TryParse(Str(o, "source"), true, out PetEventSourceKind source)) source = PetEventSourceKind.Custom;
            if (!Enum.TryParse(Str(o, "kind"), true, out PetEventKind kind)) kind = PetEventKind.Other;
            if ((acceptKinds & kind) == 0) return;

            if (source == PetEventSourceKind.Bluesky && skipBlueskyWhenLocalActive && LocalBlueskyActive()) return;

            Emit(new PetEvent(source, kind, Str(o, "rawKind"), Str(o, "actorId"), Str(o, "actorName"), Str(o, "contextId")));
        }

        static bool LocalBlueskyActive()
        {
            if (PetPostConfig.Origin != PetPostConfig.ConfigOrigin.Local) return false;
            var cfg = PetPostConfig.Load();
            return cfg != null && cfg.bluesky != null && cfg.bluesky.enabled;
        }

        static string Str(JObject o, string key)
        {
            var v = o[key];
            return v == null || v.Type == JTokenType.Null ? null : v.ToString();
        }
    }
}
