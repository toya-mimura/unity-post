using System.IO;
using PetPost.Reactions;
using PetPost.Sources;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;

namespace PetPost.EditorTools
{
    /// <summary>
    /// メニュー「PetPost」から、デモ用の床・仮の樹・リンゴ・ルールのつなぎ込みまでを自動で組む。
    /// 作ったものはすべて Ctrl+Z で取り消せる。
    ///
    /// Menu items that build a working demo (floor, placeholder tree, apple prefab,
    /// spawn area, router rule) in the open scene. Everything is undoable.
    /// </summary>
    public static class PetPostDemoBuilder
    {
        const string DemoFolder = "Assets/PetPostDemo";
        const string ApplePrefabPath = DemoFolder + "/Apple.prefab";
        const string DemoRootName = "PetPostDemo";
        const string RuleLabel = "リンゴ（デモ）";

        // ================= メニュー =================

        [MenuItem("PetPost/デモシーンを作る", priority = 0)]
        static void BuildDemo()
        {
            var existing = GameObject.Find(DemoRootName);
            if (existing != null)
            {
                if (!EditorUtility.DisplayDialog("PetPost",
                        "このシーンにはすでに PetPostDemo があります。作り直しますか？\n（Ctrl+Z で元に戻せます）",
                        "作り直す", "やめる"))
                    return;
                Undo.DestroyObjectImmediate(existing);
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("PetPost デモシーン作成");

            EnsureFolder();
            var router = EnsureSystemObject();
            var apple = EnsureApplePrefab();

            // --- デモ一式の親 ---
            var root = new GameObject(DemoRootName);
            Undo.RegisterCreatedObjectUndo(root, "PetPost demo");

            // --- 床（Planeには最初から MeshCollider が付いている） ---
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localScale = new Vector3(2f, 1f, 2f); // 20m × 20m
            floor.GetComponent<Renderer>().sharedMaterial = SaveMaterial("Floor", floor, new Color(0.55f, 0.6f, 0.45f));

            // --- 仮の樹：幹だけコライダーあり、葉っぱはコライダーなし ---
            var tree = new GameObject("Tree（仮）");
            tree.transform.SetParent(root.transform, false);

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); // 高さ2m × scale
            trunk.name = "Trunk";
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            trunk.transform.localScale = new Vector3(0.4f, 1.5f, 0.4f);   // 高さ3m
            trunk.GetComponent<Renderer>().sharedMaterial = SaveMaterial("Trunk", trunk, new Color(0.45f, 0.3f, 0.18f));

            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.name = "Canopy";
            canopy.transform.SetParent(tree.transform, false);
            canopy.transform.localPosition = new Vector3(0f, 4f, 0f);
            canopy.transform.localScale = new Vector3(3.2f, 2.4f, 3.2f);
            Object.DestroyImmediate(canopy.GetComponent<Collider>()); // リンゴがすり抜けて落ちるように
            canopy.GetComponent<Renderer>().sharedMaterial = SaveMaterial("Canopy", canopy, new Color(0.3f, 0.6f, 0.3f));

            // --- リンゴが生まれる場所（樹冠の中） ---
            var area = new GameObject("AppleSpawnArea");
            area.transform.SetParent(tree.transform, false);
            area.transform.localPosition = new Vector3(0f, 4f, 0f);
            var box = area.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.4f, 1.2f, 2.4f);

            var spawner = area.AddComponent<SpawnPrefabReaction>();
            spawner.prefab = apple;
            spawner.spawnArea = box;

            // --- ルールにつなぐ ---
            AddAppleRule(router, spawner);

            // --- カメラを樹に向ける（Ctrl+Zで戻せる） ---
            var cam = Camera.main;
            if (cam != null)
            {
                Undo.RecordObject(cam.transform, "PetPost demo camera");
                cam.transform.position = new Vector3(0f, 3f, -9f);
                cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 2.6f, 0f) - cam.transform.position);
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = area;
            SceneView.lastActiveSceneView?.FrameSelected();

            EditorUtility.DisplayDialog("PetPost",
                "デモシーンを作りました。\n\n▶再生して、デバッグパネルの「メンション」や「いいね」を押すとリンゴが落ちます。\n\n" +
                "・リンゴの見た目：Assets/PetPostDemo/Apple.prefab を開き、子の Visual を差し替え\n" +
                "・樹を差し替え：Tree（仮）の Trunk / Canopy を消して自分の樹を置き、AppleSpawnArea を樹冠に移動",
                "OK");
        }

        [MenuItem("PetPost/設定ファイルの場所を開く", priority = 20)]
        static void RevealConfig()
        {
            string path = PetPostConfig.DefaultPath;
            if (File.Exists(path)) EditorUtility.RevealInFinder(path);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                EditorUtility.RevealInFinder(Path.GetDirectoryName(path));
                Debug.Log($"[PetPost] 設定ファイルはまだありません。▶再生すると雛形が作られます:\n{path}");
            }
        }

        // ================= 部品 =================

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(DemoFolder))
                AssetDatabase.CreateFolder("Assets", "PetPostDemo");
        }

        /// <summary>PetEventHub がなければ、受信まわり一式を持つ PetPost オブジェクトを作る。</summary>
        static PetReactionRouter EnsureSystemObject()
        {
            var hub = Object.FindFirstObjectByType<PetEventHub>();
            if (hub == null)
            {
                var go = new GameObject("PetPost");
                Undo.RegisterCreatedObjectUndo(go, "PetPost system");
                hub = go.AddComponent<PetEventHub>();
                go.AddComponent<PetReactionRouter>();
                go.AddComponent<DiscordGatewaySource>();
                go.AddComponent<BlueskyNotificationSource>();
                go.AddComponent<DebugTools.PetPostDebugPanel>();
            }

            var router = hub.GetComponent<PetReactionRouter>();
            if (router == null) router = Object.FindFirstObjectByType<PetReactionRouter>();
            if (router == null) router = Undo.AddComponent<PetReactionRouter>(hub.gameObject);
            return router;
        }

        /// <summary>
        /// Apple.prefab を作る。親（物理）＋子 Visual（見た目）に分けてあるので、
        /// 見た目は子を差し替えるだけで変えられる。すでにあれば再利用。
        /// </summary>
        static GameObject EnsureApplePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ApplePrefabPath);
            if (existing != null) return existing;

            var apple = new GameObject("Apple");
            apple.transform.localScale = Vector3.one * 0.3f;
            var rb = apple.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            apple.AddComponent<SphereCollider>().radius = 0.5f;

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(apple.transform, false);
            visual.GetComponent<Renderer>().sharedMaterial = SaveMaterial("Apple", visual, new Color(0.8f, 0.1f, 0.1f));

            var prefab = PrefabUtility.SaveAsPrefabAsset(apple, ApplePrefabPath);
            Object.DestroyImmediate(apple);
            return prefab;
        }

        /// <summary>プリミティブの既定マテリアル（使っているレンダーパイプラインに合ったもの）を複製して色を付ける。</summary>
        static Material SaveMaterial(string name, GameObject primitive, Color color)
        {
            string path = $"{DemoFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(primitive.GetComponent<Renderer>().sharedMaterial) { color = color };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static void AddAppleRule(PetReactionRouter router, SpawnPrefabReaction spawner)
        {
            Undo.RecordObject(router, "PetPost demo rule");

            router.rules.RemoveAll(r => r != null && r.label == RuleLabel);

            var rule = new ReactionRule
            {
                label = RuleLabel,
                enabled = true,
                sources = PetEventSourceKind.Discord | PetEventSourceKind.Bluesky | PetEventSourceKind.Debug,
                kinds = (PetEventKind)~0,
                pick = PickMode.WeightedRandom,
            };
            var option = new ReactionOption { name = "リンゴが落ちる", weight = 1f };
            UnityEventTools.AddPersistentListener(option.action, new UnityAction<PetEvent>(spawner.Play));
            rule.options.Add(option);
            rule.EnsureDefaults();

            router.rules.Add(rule);
            EditorUtility.SetDirty(router);
        }
    }
}
