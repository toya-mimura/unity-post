using System;
using UnityEngine.Events;

namespace PetPost
{
    /// <summary>
    /// どこから届いたか。新しい受信元を足すときは、ここに2のべき乗の値を1行追加する。
    /// Where the event came from. Add one power-of-two line here when you add a new source.
    /// </summary>
    [Flags]
    public enum PetEventSourceKind
    {
        Discord = 1 << 0,
        Bluesky = 1 << 1,
        Debug   = 1 << 2,
        Custom  = 1 << 3,
    }

    /// <summary>
    /// 何が起きたか（受信元をまたいだ共通の種類）。
    /// What happened, shared across sources.
    /// </summary>
    [Flags]
    public enum PetEventKind
    {
        MemberJoin    = 1 << 0,  // Discord: 新メンバー参加
        Mention       = 1 << 1,  // Discord/Bluesky: メンション
        DirectMessage = 1 << 2,  // Discord: Bot宛てDM
        Message       = 1 << 3,  // Discord: サーバー内の任意の発言（既定はOFF）
        Like          = 1 << 4,  // Bluesky
        Repost        = 1 << 5,  // Bluesky
        Follow        = 1 << 6,  // Bluesky
        Reply         = 1 << 7,  // Bluesky
        Quote         = 1 << 8,  // Bluesky
        Other         = 1 << 30, // 上記以外（rawKind を見て判断）
    }

    /// <summary>
    /// 受信元に依存しない「手紙」1通ぶんの情報。本文は持たない。
    /// One "letter", independent of the source. Message text is intentionally not carried.
    /// </summary>
    [Serializable]
    public class PetEvent
    {
        public PetEventSourceKind source;
        public PetEventKind kind;

        /// <summary>受信元の生の種類名（例: "GUILD_MEMBER_ADD", "like"）。</summary>
        public string rawKind;

        /// <summary>誰が（Discordのユーザー ID、BlueskyのDIDなど）。</summary>
        public string actorId;

        /// <summary>表示名（手紙の差出人などに使う）。</summary>
        public string actorName;

        /// <summary>どこで（サーバー ID、投稿URIなど）。</summary>
        public string contextId;

        public DateTime timestampUtc;

        public PetEvent() { }

        public PetEvent(PetEventSourceKind source, PetEventKind kind, string rawKind,
                        string actorId, string actorName, string contextId = null)
        {
            this.source = source;
            this.kind = kind;
            this.rawKind = rawKind;
            this.actorId = actorId;
            this.actorName = actorName;
            this.contextId = contextId;
            timestampUtc = DateTime.UtcNow;
        }

        public override string ToString() =>
            $"[{source}/{kind}] {actorName} ({actorId}) raw={rawKind} ctx={contextId}";
    }

    /// <summary>Inspectorで PetEvent を受け取るメソッドを登録するための UnityEvent。</summary>
    [Serializable]
    public class PetEventUnityEvent : UnityEvent<PetEvent> { }
}
