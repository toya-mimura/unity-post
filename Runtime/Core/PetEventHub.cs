using System;
using System.Collections.Generic;
using UnityEngine;

namespace PetPost
{
    /// <summary>
    /// ポストから手紙を取り出し、連打対策（間引き・クールダウン）をしてから配達する。
    /// シーンに1つ置く。
    ///
    /// Drains the bus on the main thread, applies anti-spam throttling, then delivers
    /// events one at a time. Put exactly one in the scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PetEventHub : MonoBehaviour
    {
        public static PetEventHub Instance { get; private set; }

        [Header("連打対策 / Anti-spam")]
        [Tooltip("演出と演出の最小間隔（秒）。一斉に届いても順番に再生される。")]
        [Min(0f)] public float minSecondsBetweenEvents = 0.6f;

        [Tooltip("同じ人・同じ種類のイベントを無視する時間（秒）。メンション連打対策。")]
        [Min(0f)] public float perActorCooldownSeconds = 20f;

        [Tooltip("待ち行列の上限。超えたら古いものから捨てる。")]
        [Min(1)] public int maxQueued = 20;

        [Header("配達先 / Delivery")]
        [Tooltip("ルーター以外にも直接受け取りたいときに使う（ログ表示など）。")]
        public PetEventUnityEvent onDelivered = new PetEventUnityEvent();

        [Tooltip("届いたイベントをConsoleに出す。")]
        public bool logEvents = true;

        /// <summary>C#から購読したい場合はこちら。</summary>
        public event Action<PetEvent> Delivered;

        readonly Queue<PetEvent> _queue = new Queue<PetEvent>();
        readonly Dictionary<string, float> _lastSeen = new Dictionary<string, float>();
        float _nextDeliveryTime;

        public int QueuedCount => _queue.Count;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[PetPost] PetEventHub がシーンに複数あります。1つにしてください。", this);
                enabled = false;
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float now = Time.unscaledTime;

            // 1) ポストから全部取り出して、クールダウン判定してから並べる
            while (PetEventBus.TryTake(out var e))
            {
                if (IsCoolingDown(e, now)) continue;

                _queue.Enqueue(e);
                while (_queue.Count > maxQueued) _queue.Dequeue();
            }

            // 2) 間隔を空けて1通ずつ配達
            if (_queue.Count > 0 && now >= _nextDeliveryTime)
            {
                var e = _queue.Dequeue();
                _nextDeliveryTime = now + minSecondsBetweenEvents;
                Deliver(e);
            }
        }

        bool IsCoolingDown(PetEvent e, float now)
        {
            if (perActorCooldownSeconds <= 0f || string.IsNullOrEmpty(e.actorId)) return false;

            string key = $"{e.source}:{e.kind}:{e.actorId}";
            if (_lastSeen.TryGetValue(key, out float last) && now - last < perActorCooldownSeconds)
                return true;

            _lastSeen[key] = now;

            // 長時間起動しっぱなしでも辞書が膨らまないように、たまに掃除
            if (_lastSeen.Count > 512) PruneCooldowns(now);
            return false;
        }

        void PruneCooldowns(float now)
        {
            var stale = new List<string>();
            foreach (var kv in _lastSeen)
                if (now - kv.Value >= perActorCooldownSeconds) stale.Add(kv.Key);
            foreach (var k in stale) _lastSeen.Remove(k);
        }

        void Deliver(PetEvent e)
        {
            if (logEvents) Debug.Log($"[PetPost] {e}");
            try { Delivered?.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex, this); }
            try { onDelivered.Invoke(e); }
            catch (Exception ex) { Debug.LogException(ex, this); }
        }
    }
}
