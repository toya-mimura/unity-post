using System.IO;
using UnityEngine;

namespace PetPost.DebugTools
{
    /// <summary>
    /// 画面左上に小さなパネルを出す開発用ツール。
    /// ・各受信元の接続状態
    /// ・偽イベントの発火ボタン（Discord/Bluesky のふりをして送るので、ルールの確認に使える）
    /// ・設定ファイルの場所を開くボタン
    /// OnGUI なので Input System の設定に関係なく動く。
    ///
    /// Dev overlay: source status, fake-event buttons (sent as Discord/Bluesky so your rules
    /// match), and a button that opens the config folder. Uses OnGUI, so it works with
    /// either input backend.
    /// </summary>
    public class PetPostDebugPanel : MonoBehaviour
    {
        public bool visible = true;

        [Tooltip("リリースビルドでも表示する（通常はエディタと Development Build だけ）")]
        public bool showInReleaseBuild = false;

        [Range(0.75f, 3f)] public float uiScale = 1f;

        int _fakeCounter;
        Vector2 _scroll;

        void OnGUI()
        {
            if (!visible) return;
            if (!Application.isEditor && !UnityEngine.Debug.isDebugBuild && !showInReleaseBuild) return;

            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            float h = Mathf.Min(560f, Screen.height / uiScale - 20f);
            GUILayout.BeginArea(new Rect(10, 10, 380, h), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            GUILayout.Label("<b>PetPost</b>", Rich());

            // --- 状態 ---
            if (PetEventSource.Active.Count == 0) GUILayout.Label("受信元がありません");
            foreach (var s in PetEventSource.Active)
                GUILayout.Label($"{Dot(s.Status)} {s.DisplayName}: {s.Status}\n   {s.StatusMessage}", Rich());

            var hub = PetEventHub.Instance;
            GUILayout.Label(hub != null ? $"待ち行列: {hub.QueuedCount}" : "<color=#ff6666>PetEventHub がありません</color>", Rich());

            // --- 偽イベント ---
            GUILayout.Space(6);
            GUILayout.Label("テスト送信（Discord）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("新メンバー")) Fake(PetEventSourceKind.Discord, PetEventKind.MemberJoin);
            if (GUILayout.Button("メンション")) Fake(PetEventSourceKind.Discord, PetEventKind.Mention);
            if (GUILayout.Button("DM")) Fake(PetEventSourceKind.Discord, PetEventKind.DirectMessage);
            GUILayout.EndHorizontal();

            GUILayout.Label("テスト送信（Bluesky）");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("いいね")) Fake(PetEventSourceKind.Bluesky, PetEventKind.Like);
            if (GUILayout.Button("リポスト")) Fake(PetEventSourceKind.Bluesky, PetEventKind.Repost);
            if (GUILayout.Button("フォロー")) Fake(PetEventSourceKind.Bluesky, PetEventKind.Follow);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("メンション")) Fake(PetEventSourceKind.Bluesky, PetEventKind.Mention);
            if (GUILayout.Button("リプライ")) Fake(PetEventSourceKind.Bluesky, PetEventKind.Reply);
            if (GUILayout.Button("引用")) Fake(PetEventSourceKind.Bluesky, PetEventKind.Quote);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("いいね×10（一斉に届いたとき）"))
                for (int i = 0; i < 10; i++) Fake(PetEventSourceKind.Bluesky, PetEventKind.Like);

            if (GUILayout.Button("同じ人がメンション×5（連打対策の確認）"))
                for (int i = 0; i < 5; i++)
                    PetEventBus.Publish(new PetEvent(PetEventSourceKind.Discord, PetEventKind.Mention,
                                                     "debug", "debug-same-user", "連打さん"));

            // --- 設定ファイル ---
            GUILayout.Space(6);
            GUILayout.Label("設定ファイル:");
            GUILayout.TextField(PetPostConfig.DefaultPath);
            if (GUILayout.Button("フォルダを開く"))
            {
                string dir = Path.GetDirectoryName(PetPostConfig.DefaultPath);
                Directory.CreateDirectory(dir);
                Application.OpenURL("file:///" + dir.Replace('\\', '/'));
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.matrix = prev;
        }

        void Fake(PetEventSourceKind source, PetEventKind kind)
        {
            _fakeCounter++;
            // 毎回別人にして、連打対策（クールダウン）に引っかからないようにする
            PetEventBus.Publish(new PetEvent(source, kind, "debug",
                                             $"debug-user-{_fakeCounter}", $"テスト{_fakeCounter}"));
        }

        static string Dot(SourceStatus s)
        {
            switch (s)
            {
                case SourceStatus.Connected: return "<color=#5cd65c>●</color>";
                case SourceStatus.Connecting:
                case SourceStatus.Reconnecting: return "<color=#e6c34d>●</color>";
                case SourceStatus.Error: return "<color=#ff6666>●</color>";
                default: return "<color=#999999>●</color>";
            }
        }

        static GUIStyle s_rich;
        static GUIStyle Rich()
        {
            if (s_rich == null) s_rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return s_rich;
        }
    }
}
