using System.Collections.Generic;
using UnityEngine;

namespace PetPost.Reactions
{
    /// <summary>
    /// プレハブを生成する演出。「リンゴが落ちる」「手紙が届く」などに使う。
    /// リンゴ: Rigidbody付きのリンゴプレハブ ＋ 樹の枝あたりに置いた BoxCollider(isTrigger) を spawnArea に。
    ///
    /// Spawns a prefab (apple, letter...) at a random point inside spawnArea.
    /// Any IPetEventReceiver on the spawned object gets the event.
    /// </summary>
    public class SpawnPrefabReaction : MonoBehaviour
    {
        public GameObject prefab;

        [Tooltip("この箱の中のランダムな位置に出す（isTrigger推奨）。空ならこのオブジェクトの位置。")]
        public BoxCollider spawnArea;

        [Tooltip("生成先の親。空ならシーン直下。")]
        public Transform parent;

        public bool randomYaw = true;

        [Tooltip("生成直後に与える速度（Rigidbodyがある場合）。")]
        public Vector3 initialVelocity = Vector3.zero;

        [Tooltip("自動で消すまでの秒数。0なら消さない。")]
        [Min(0f)] public float lifetime = 15f;

        [Tooltip("同時に存在できる最大数。超えたら古いものから消す。")]
        [Min(1)] public int maxAlive = 30;

        readonly List<GameObject> _alive = new List<GameObject>();

        /// <summary>Inspectorでは「Dynamic PetEvent」の Play を選ぶ。</summary>
        public void Play(PetEvent e) => Spawn(e);

        /// <summary>引数なしで使いたいとき（ボタンなど）。</summary>
        public void Play() => Spawn(null);

        public GameObject Spawn(PetEvent e)
        {
            if (prefab == null) return null;

            _alive.RemoveAll(go => go == null);
            while (_alive.Count >= maxAlive)
            {
                Destroy(_alive[0]);
                _alive.RemoveAt(0);
            }

            Vector3 pos = spawnArea != null ? RandomPointIn(spawnArea) : transform.position;
            Quaternion rot = randomYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * prefab.transform.rotation
                                       : prefab.transform.rotation;

            var go = Instantiate(prefab, pos, rot, parent);
            _alive.Add(go);

            if (initialVelocity != Vector3.zero && go.TryGetComponent<Rigidbody>(out var rb))
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = initialVelocity;
#else
                rb.velocity = initialVelocity;
#endif
            }

            if (e != null)
                foreach (var r in go.GetComponentsInChildren<IPetEventReceiver>(true))
                    r.Receive(e);

            if (lifetime > 0f) Destroy(go, lifetime);
            return go;
        }

        static Vector3 RandomPointIn(BoxCollider box)
        {
            Vector3 half = box.size * 0.5f;
            var local = box.center + new Vector3(
                Random.Range(-half.x, half.x),
                Random.Range(-half.y, half.y),
                Random.Range(-half.z, half.z));
            return box.transform.TransformPoint(local);
        }
    }
}
