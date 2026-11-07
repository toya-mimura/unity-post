using UnityEngine;

namespace PetPost.Reactions
{
    /// <summary>
    /// Animator のトリガーを引く演出。ペットが跳ねる・樹が揺れる・ポストの旗が立つなど。
    /// 複数登録するとランダムに1つ選ぶ。
    ///
    /// Fires one (random) Animator trigger.
    /// </summary>
    public class AnimatorTriggerReaction : MonoBehaviour
    {
        public Animator animator;
        public string[] triggers = { "React" };

        public void Play(PetEvent e) => Play();

        public void Play()
        {
            if (animator == null || triggers == null || triggers.Length == 0) return;
            animator.SetTrigger(triggers[Random.Range(0, triggers.Length)]);
        }
    }
}
