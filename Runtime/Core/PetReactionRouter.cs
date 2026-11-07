using System;
using System.Collections.Generic;
using UnityEngine;

namespace PetPost
{
    public enum PickMode
    {
        [Tooltip("重みに従って1つだけ選ぶ")] WeightedRandom,
        [Tooltip("登録した演出を全部実行する")] All,
        [Tooltip("上から順番に1つずつ")] Cycle,
    }

    [Serializable]
    public class ReactionOption
    {
        public string name = "演出";
        [Tooltip("WeightedRandom のときの出やすさ。0で無効。")]
        [Min(0f)] public float weight = 1f;
        public PetEventUnityEvent action = new PetEventUnityEvent();

        // Inspectorの「＋」で作られた要素は初期値が入らないことがあるので、初回だけ補う
        [SerializeField, HideInInspector] bool initialized;

        public void EnsureDefaults()
        {
            if (initialized) return;
            if (weight <= 0f) weight = 1f;
            if (string.IsNullOrEmpty(name)) name = "演出";
            action ??= new PetEventUnityEvent();
            initialized = true;
        }
    }

    [Serializable]
    public class ReactionRule
    {
        public string label = "新しいルール";
        public bool enabled = true;

        [Tooltip("対象の受信元（複数選択可）")]
        public PetEventSourceKind sources = (PetEventSourceKind)~0;

        [Tooltip("対象のイベント種類（複数選択可）")]
        public PetEventKind kinds = (PetEventKind)~0;

        [Tooltip("特定の相手だけに反応したいとき。ID（DiscordのユーザーID、BlueskyのDID）をカンマ区切りで。空なら全員。")]
        public string actorIds = "";

        public PickMode pick = PickMode.WeightedRandom;

        [Tooltip("このルールに一致したら、下のルールは見ない。特別扱いの相手を上に置くときに使う。")]
        public bool stopAfterMatch = false;

        public List<ReactionOption> options = new List<ReactionOption>();

        [SerializeField, HideInInspector] bool initialized;

        public void EnsureDefaults()
        {
            if (!initialized)
            {
                enabled = true;
                if (string.IsNullOrEmpty(label)) label = "新しいルール";
                if (sources == 0) sources = (PetEventSourceKind)~0;
                if (kinds == 0) kinds = (PetEventKind)~0;
                initialized = true;
            }
            options ??= new List<ReactionOption>();
            foreach (var o in options) o?.EnsureDefaults();
        }

        [NonSerialized] internal int cycleIndex;
        [NonSerialized] HashSet<string> _actorSet;
        [NonSerialized] string _actorSetSource;

        internal bool Matches(PetEvent e)
        {
            if (!enabled) return false;
            if ((sources & e.source) == 0) return false;
            if ((kinds & e.kind) == 0) return false;

            if (!string.IsNullOrWhiteSpace(actorIds))
            {
                if (_actorSet == null || _actorSetSource != actorIds)
                {
                    _actorSetSource = actorIds;
                    _actorSet = new HashSet<string>();
                    foreach (var id in actorIds.Split(','))
                    {
                        var t = id.Trim();
                        if (t.Length > 0) _actorSet.Add(t);
                    }
                }
                if (e.actorId == null || !_actorSet.Contains(e.actorId)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 届いた手紙を「ルール」に照らして演出に振り分ける。
    /// ルールは上から順に評価され、一致したものすべてが実行される（stopAfterMatch で打ち切り）。
    ///
    /// Routes delivered events to reactions via ordered rules.
    /// </summary>
    public class PetReactionRouter : MonoBehaviour
    {
        [Tooltip("空ならシーンの PetEventHub を自動で使う")]
        public PetEventHub hub;

        public List<ReactionRule> rules = new List<ReactionRule>();

        void OnEnable()
        {
            if (hub == null) hub = PetEventHub.Instance != null ? PetEventHub.Instance : FindHub();
            if (hub == null)
            {
                Debug.LogWarning("[PetPost] PetEventHub が見つかりません。", this);
                return;
            }
            hub.Delivered += Route;
        }

        void OnDisable()
        {
            if (hub != null) hub.Delivered -= Route;
        }

        static PetEventHub FindHub()
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<PetEventHub>();
#else
            return FindObjectOfType<PetEventHub>();
#endif
        }

        [Tooltip("どのルールにも一致しなかったとき、理由の手がかりをConsoleに出す")]
        public bool logUnmatched = true;

        void OnValidate()
        {
            if (rules == null) return;
            foreach (var r in rules) r?.EnsureDefaults();
        }

        public void Route(PetEvent e)
        {
            bool matched = false;
            foreach (var rule in rules)
            {
                if (!rule.Matches(e)) continue;
                matched = true;
                Run(rule, e);
                if (rule.stopAfterMatch) break;
            }

            if (!matched && logUnmatched)
                Debug.Log($"[PetPost] {e.source}/{e.kind} に一致するルールがありません。" +
                          "Router の rules で Enabled・Sources・Kinds を確認してください。", this);
        }

        void Run(ReactionRule rule, PetEvent e)
        {
            var opts = rule.options;
            if (opts == null || opts.Count == 0)
            {
                Debug.LogWarning($"[PetPost] ルール「{rule.label}」に演出（options）が登録されていません。", this);
                return;
            }

            switch (rule.pick)
            {
                case PickMode.All:
                    foreach (var o in opts) Invoke(o, e);
                    break;

                case PickMode.Cycle:
                    rule.cycleIndex %= opts.Count;
                    Invoke(opts[rule.cycleIndex], e);
                    rule.cycleIndex++;
                    break;

                default:
                    var picked = PickWeighted(opts);
                    if (picked != null) Invoke(picked, e);
                    else Debug.LogWarning($"[PetPost] ルール「{rule.label}」の演出の Weight がすべて0です。1以上にしてください。", this);
                    break;
            }
        }

        static ReactionOption PickWeighted(List<ReactionOption> opts)
        {
            float total = 0f;
            foreach (var o in opts) total += Mathf.Max(0f, o.weight);
            if (total <= 0f) return null;

            float r = UnityEngine.Random.value * total;
            foreach (var o in opts)
            {
                r -= Mathf.Max(0f, o.weight);
                if (r <= 0f && o.weight > 0f) return o;
            }
            for (int i = opts.Count - 1; i >= 0; i--)
                if (opts[i].weight > 0f) return opts[i];
            return null;
        }

        void Invoke(ReactionOption o, PetEvent e)
        {
            if (o.action == null || o.action.GetPersistentEventCount() == 0)
                Debug.LogWarning($"[PetPost] 演出「{o.name}」の Action に何もつながっていません。", this);
            try { o.action.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }
    }
}
