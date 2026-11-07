using UnityEngine;
using UnityEngine.Events;

namespace PetPost.Reactions
{
    /// <summary>
    /// 生成されたプレハブ（手紙など）が「誰から何が届いたか」を受け取るためのインターフェース。
    /// Implemented by components on spawned prefabs that want the event that spawned them.
    /// </summary>
    public interface IPetEventReceiver
    {
        void Receive(PetEvent e);
    }
}
