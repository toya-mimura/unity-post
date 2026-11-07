using System.Collections.Concurrent;
using UnityEngine;

namespace PetPost
{
    /// <summary>
    /// 全受信元が手紙を投函する「ポスト」。どのスレッドからでも Publish してよい。
    /// 取り出しは PetEventHub がメインスレッドで行う。
    ///
    /// The shared mailbox. Publish from any thread; PetEventHub drains it on the main thread.
    /// Any script (not only PetEventSource subclasses) may publish here.
    /// </summary>
    public static class PetEventBus
    {
        static ConcurrentQueue<PetEvent> s_queue = new ConcurrentQueue<PetEvent>();

        public static void Publish(PetEvent e)
        {
            if (e != null) s_queue.Enqueue(e);
        }

        internal static bool TryTake(out PetEvent e) => s_queue.TryDequeue(out e);

        // 「Enter Play Mode Options」でドメインリロードを切っていても前回の残りを持ち越さない
        // Clear leftovers when domain reload is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_queue = new ConcurrentQueue<PetEvent>();
    }
}
