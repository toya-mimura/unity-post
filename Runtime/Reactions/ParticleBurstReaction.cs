using UnityEngine;

namespace PetPost.Reactions
{
    /// <summary>
    /// パーティクルを一発出す演出。キラキラ・花びら・紙吹雪など。
    /// One-shot particle burst. Optionally moves to a random point first.
    /// </summary>
    public class ParticleBurstReaction : MonoBehaviour
    {
        public ParticleSystem particles;

        [Tooltip("一度に出す粒の数。0なら ParticleSystem 側の設定（Bursts）で Play する。")]
        [Min(0)] public int emitCount = 40;

        [Tooltip("指定すると、この箱の中のランダムな位置で出す。")]
        public BoxCollider randomArea;

        public void Play(PetEvent e) => Play();

        public void Play()
        {
            if (particles == null) return;

            if (randomArea != null)
            {
                Vector3 half = randomArea.size * 0.5f;
                var local = randomArea.center + new Vector3(
                    Random.Range(-half.x, half.x), Random.Range(-half.y, half.y), Random.Range(-half.z, half.z));
                particles.transform.position = randomArea.transform.TransformPoint(local);
            }

            if (emitCount > 0) particles.Emit(emitCount);
            else particles.Play(true);
        }
    }
}
