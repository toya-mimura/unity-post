using UnityEngine;
using UnityEngine.Events;

namespace PetPost.Reactions
{
    [System.Serializable] public class StringUnityEvent : UnityEvent<string> { }

    /// <summary>
    /// 手紙プレハブなどに付けて、差出人名や「〇〇さんからいいね」を TextMeshPro 等に流す。
    /// Inspector で onActorName → TMP_Text.text（Dynamic string）に繋ぐ。
    ///
    /// Put on a letter prefab; wire onActorName / onSummary to TMP_Text.text (dynamic string).
    /// </summary>
    public class PetEventTextReceiver : MonoBehaviour, IPetEventReceiver
    {
        public StringUnityEvent onActorName = new StringUnityEvent();
        public StringUnityEvent onSummary = new StringUnityEvent();

        public void Receive(PetEvent e)
        {
            onActorName.Invoke(e.actorName ?? "");
            onSummary.Invoke(Summarize(e));
        }

        public static string Summarize(PetEvent e)
        {
            string who = string.IsNullOrEmpty(e.actorName) ? "だれか" : e.actorName;
            switch (e.kind)
            {
                case PetEventKind.MemberJoin:    return $"{who}さんが遊びに来たよ";
                case PetEventKind.Mention:       return $"{who}さんが呼んでるよ";
                case PetEventKind.DirectMessage: return $"{who}さんから手紙が届いたよ";
                case PetEventKind.Message:       return $"{who}さんがおしゃべりしてるよ";
                case PetEventKind.Like:          return $"{who}さんがいいねしてくれたよ";
                case PetEventKind.Repost:        return $"{who}さんがリポストしてくれたよ";
                case PetEventKind.Follow:        return $"{who}さんがフォローしてくれたよ";
                case PetEventKind.Reply:         return $"{who}さんから返事が来たよ";
                case PetEventKind.Quote:         return $"{who}さんが引用してくれたよ";
                default:                         return $"{who}さんから何か届いたよ";
            }
        }
    }
}
