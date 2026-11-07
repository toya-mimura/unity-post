using UnityEngine;

namespace PetPost.Reactions
{
    /// <summary>
    /// 効果音を鳴らす演出。ピッチを少し揺らして、連続しても単調にならないようにする。
    /// Plays a random clip with slight pitch variation.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioReaction : MonoBehaviour
    {
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Range(0f, 0.5f)] public float pitchJitter = 0.08f;

        AudioSource _src;

        void Awake() => _src = GetComponent<AudioSource>();

        public void Play(PetEvent e) => Play();

        public void Play()
        {
            if (clips == null || clips.Length == 0) return;
            if (_src == null) _src = GetComponent<AudioSource>();
            _src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            _src.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
        }
    }
}
