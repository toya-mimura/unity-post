using System.Collections.Generic;
using UnityEngine;

namespace PetPost
{
    public enum SourceStatus
    {
        Stopped,
        Connecting,
        Connected,
        Reconnecting,
        Error,
    }

    /// <summary>
    /// 受信元アダプターの基底クラス。新しい受信元（Gmail、Misskeyなど）はこれを継承して
    /// StartSource / StopSource を実装し、届いたら Emit() するだけ。
    ///
    /// Base class for source adapters. Subclass it, implement StartSource/StopSource,
    /// and call Emit() when something arrives. Emit() is thread-safe.
    /// </summary>
    public abstract class PetEventSource : MonoBehaviour
    {
        static readonly List<PetEventSource> s_active = new List<PetEventSource>();

        /// <summary>現在有効な受信元（デバッグパネル表示用）。</summary>
        public static IReadOnlyList<PetEventSource> Active => s_active;

        public abstract string DisplayName { get; }

        // 受信スレッドから書き換えるので volatile
        volatile SourceStatus _status = SourceStatus.Stopped;
        volatile string _statusMessage = "";

        public SourceStatus Status => _status;
        public string StatusMessage => _statusMessage;

        protected void SetStatus(SourceStatus status, string message = "")
        {
            _status = status;
            _statusMessage = message ?? "";
        }

        /// <summary>手紙を投函する。どのスレッドから呼んでもよい。</summary>
        protected void Emit(PetEvent e) => PetEventBus.Publish(e);

        void OnEnable()
        {
            if (!s_active.Contains(this)) s_active.Add(this);
            StartSource();
        }

        void OnDisable()
        {
            s_active.Remove(this);
            StopSource();
            SetStatus(SourceStatus.Stopped);
        }

        protected abstract void StartSource();
        protected abstract void StopSource();

        // ================= Workers（cf-unity-hub）からの設定待ち =================

        System.Action _remoteReadyCallback;

        /// <summary>
        /// 設定が Workers から届くのを待ち、届いたら StartSource をやり直す。
        /// StartSource の中で PetPostConfig.Load() が null で、PetPostConfig.IsRemotePending のときに呼ぶ。
        /// </summary>
        protected void WaitForRemoteConfig()
        {
            SetStatus(SourceStatus.Connecting, "Workers から設定を取得中");
            if (_remoteReadyCallback != null) return;
            _remoteReadyCallback = () =>
            {
                _remoteReadyCallback = null;
                if (this != null && isActiveAndEnabled) StartSource();
            };
            PetPostConfig.WhenRemoteReady(_remoteReadyCallback);
        }

        void OnDestroy()
        {
            PetPostConfig.CancelWhenRemoteReady(_remoteReadyCallback);
            _remoteReadyCallback = null;
        }
    }
}
