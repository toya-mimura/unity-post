# サンプル

「おまけ」の演出素材です。必要なものだけ取り込んでください。

| フォルダ | 中身 | 使うもの |
|---|---|---|
| `Apple/` | `Apple.glb`（リンゴのメッシュ） | `SpawnPrefabReaction` |
| `Letter/` | `envelope.glb`（封筒）、`LetterFlutter.cs`、`LetterDeliveryReaction.cs` | 手紙が地点A→Bへふわふわ飛ぶ演出 |

<!-- 作者・利用条件をここに書いてください（例：作成 toya-mimura／本リポジトリと同じ条件で利用可） -->

---

## 準備：glTFast を入れる（.glb を読み込むため）

Unity は標準では `.glb` を読み込めません。

1. Window → Package Manager → 左上の「＋」→ *Install package by name…*
2. `com.unity.cloud.gltfast` を**一字一句そのまま**入力（Version は空欄でOK）→ Install

入れると、`.glb` が普通のモデル（Prefab のように扱えるアセット）として表示されます。

---

## Apple：リンゴを Sphere から差し替える

1. `Apple.glb` をプロジェクトに入れる。
2. Apple プレハブ（デモシーンなら `Assets/PetPostDemo/Apple.prefab`）をダブルクリックで開く。
3. 子の `Visual`（Sphere）を削除し、代わりに `Apple.glb` を子として置く。
4. 当たり判定は親の Sphere Collider のまま使えます。見た目に合わせて Radius を調整してください。

## Letter：手紙がふわふわ届く

`LetterFlutter.cs` が飛び方（経路・揺れ・回転）を、`LetterDeliveryReaction.cs` が「届くたびに1通ずつ生成して飛ばす」係を担当します。**2つはセットで**入れてください（片方だけだとコンパイルエラーになります）。

1. `Letter/` フォルダごとプロジェクトに入れる。
2. **手紙プレハブ**：空の GameObject `Letter` に `LetterFlutter` を付け、子に `envelope.glb` を置く。Point A / Point B は空欄のままでOK（生成時に係が渡します）。Project にドラッグしてプレハブにし、シーンからは消す。
3. **地点**：空の GameObject を2つ作り、`LetterPointA`（飛び立つ場所）と `LetterPointB`（だいたいの着地点）にする。
4. **係**：空の GameObject `LetterDelivery` に `LetterDeliveryReaction` を付け、Letter Prefab・Point A・Point B を設定。
5. **ルール**：`PetReactionRouter` に、たとえば次のルールを足す。
   - Sources：Discord, Bluesky
   - Kinds：Mention, DirectMessage, Reply, Quote
   - Action → `LetterDelivery` → **LetterDeliveryReaction > Play**（Dynamic PetEvent のほう）
6. 再生して、デバッグパネルの「DM」や「リプライ」で確認。

手紙プレハブに TextMeshPro と `PetEventTextReceiver` を付け、`On Summary` を TextMeshPro の `text` につなぐと、「〇〇さんから手紙が届いたよ」と差出人を表示できます。

### LetterFlutter の主な調整項目

| 項目 | 内容 |
|---|---|
| Duration / Duration Jitter | A→B にかかる秒数と、そのばらつき |
| Landing Radius | B を中心に、この半径のどこかに着地 |
| Lift Height | 出だしでふわっと持ち上がる高さ |
| Sway Amplitude / Frequency | 左右の揺れの幅と速さ |
| Noise Amplitude / Frequency | 風のゆらぎ（不規則さ） |
| Max Tilt / Spin Speed / Wobble Angle | 傾き・回転・パタパタ感 |
| Height Curve | 高さの落ち方（横軸＝経過、縦軸＝A の高さに対する割合） |
