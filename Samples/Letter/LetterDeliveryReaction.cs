using System.Collections.Generic;
using UnityEngine;

namespace PetPost.Reactions
{
    /// <summary>
    /// 手紙プレハブ（LetterFlutter 付き）を1通ずつ生成して、地点A→地点Bへ飛ばす演出。
    /// プレハブはシーンの地点A/Bを参照できないので、生成した直後にこの係が渡してあげる。
    /// 何通届いても、それぞれが自分の軌道でふわふわ飛ぶ。
    ///
    /// Spawns one LetterFlutter prefab per event, hands it the scene's point A/B,
    /// and starts its flight. Any IPetEventReceiver on the letter (e.g. a sender-name
    /// label) gets the event.
    /// </summary>
    public class LetterDeliveryReaction : MonoBehaviour
    {
        [Tooltip("LetterFlutter が付いた手紙プレハブ（子に letter.fbx）")]
        public LetterFlutter letterPrefab;

        [Tooltip("飛び立つ場所（ポスト、樹の枝など）")]
        public Transform pointA;

        [Tooltip("だいたいの着地点（ペットの近くなど）。着地の散らばりはプレハブの Landing Radius で決まる")]
        public Transform pointB;

        [Tooltip("生成先の親。空ならシーン直下")]
        public Transform parent;

        [Tooltip("着地してから消えるまでの秒数。0なら消さない")]
        [Min(0f)] public float stayAfterLanding = 20f;

        [Tooltip("同時に存在できる手紙の最大数。超えたら古いものから消す")]
        [Min(1)] public int maxLetters = 10;

        readonly List<GameObject> _alive = new List<GameObject>();

        /// <summary>Router からは「Dynamic PetEvent」の Play を選ぶ（差出人を手紙に渡せる）。</summary>
        public void Play(PetEvent e) => Deliver(e);

        /// <summary>ボタンなどから引数なしで呼びたいとき。</summary>
        public void Play() => Deliver(null);

        public LetterFlutter Deliver(PetEvent e)
        {
            if (letterPrefab == null || pointA == null || pointB == null)
            {
                Debug.LogWarning("[UnityPost] LetterDeliveryReaction の Letter Prefab / Point A / Point B を設定してください。", this);
                return null;
            }

            _alive.RemoveAll(go => go == null);
            while (_alive.Count >= maxLetters)
            {
                Destroy(_alive[0]);
                _alive.RemoveAt(0);
            }

            var letter = Instantiate(letterPrefab, pointA.position, pointA.rotation, parent);
            letter.pointA = pointA;
            letter.pointB = pointB;
            letter.playOnStart = false; // Start() での二重再生を防ぐ（ここで再生する）
            letter.Play();
            _alive.Add(letter.gameObject);

            if (e != null)
                foreach (var r in letter.GetComponentsInChildren<IPetEventReceiver>(true))
                    r.Receive(e);

            if (stayAfterLanding > 0f)
            {
                float flight = letter.duration + Mathf.Abs(letter.durationJitter);
                Destroy(letter.gameObject, flight + stayAfterLanding);
            }
            return letter;
        }

        void OnDrawGizmosSelected()
        {
            if (pointA == null || pointB == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(pointA.position, pointB.position);
            Gizmos.DrawWireSphere(pointA.position, 0.15f);
        }
    }
}
