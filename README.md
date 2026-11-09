# Unity Post — Discord / Bluesky の通知をUnityで構築したシーンに連携させる Unity スクリプト

- アプリ起動中だけ通知を受け取ります。
- 「リンゴが落ちる」「手紙が届く」「キラキラが出る」などの演出に振り分けます。
- サーバー不要で、Unity単体で動きます。

```
 Discord Gateway ──(WebSocket)──▶ DiscordGatewaySource ─┐
                                                        ├─▶ PetEventBus ─▶ PetEventHub ─▶ PetReactionRouter ─▶ 演出
 Bluesky API ──(30秒ごとに確認)──▶ BlueskyNotificationSource ┘    （ポスト）   （連打対策）    （ルールで振り分け）
```

| 役割 | ファイル | ひとこと |
|---|---|---|
| 手紙の形 | `Core/PetEvent.cs` | 受信元・種類・差出人。本文は持たない |
| ポスト | `Core/PetEventBus.cs` | どのスレッドからでも投函できる |
| 受信元の基底 | `Core/PetEventSource.cs` | 新しい受信元はこれを継承 |
| 配達係 | `Core/PetEventHub.cs` | 連打の間引き・同じ人のクールダウン・順番に配達 |
| 振り分け | `Core/PetReactionRouter.cs` | 「誰から・何が」でルールを作り、演出をランダム/全部/順番に実行 |
| 秘密情報 | `Core/PetPostConfig.cs` | トークンを Assets の外から読む |
| Discord | `Sources/Discord/DiscordGatewaySource.cs` | 新メンバー・メンション・Bot宛てDM |
| Bluesky | `Sources/Bluesky/BlueskyNotificationSource.cs` | いいね・リポスト・フォロー・メンション・リプライ・引用 |
| 演出サンプル | `Reactions/*.cs` | プレハブ生成、パーティクル、Animator、効果音、差出人名の表示 |
| 確認用 | `Debug/PetPostDebugPanel.cs` | 接続状態の表示と、偽イベントの発火ボタン |
| デモ作成 | `Editor/PetPostDemoBuilder.cs` | メニュー「Unity Post > デモシーンを作る」 |
| おまけ素材 | `Samples/` | リンゴのメッシュ、ふわふわ飛ぶ手紙の演出（[説明](Samples/about-sample-objects.md)） |

---

## 1. Unity に入れる
- 想定環境：Unity 2022.3 / Unity 6、Windows スタンドアロン

1. `Unity-post` フォルダごと `Assets/` に入れる。
2. **Newtonsoft JSON を入れる**：Window → Package Manager → 左上の「＋」→ *Install package by name…* → `com.unity.nuget.newtonsoft-json` を検索
   - **一字一句そのまま**入力。`newtonsoft` だけだと見つかりません。
   - Version は空欄でOK
   - すでに入っているプロジェクトも多いです。Consoleに `Newtonsoft` のエラーが出なければOK。

## 2. 【任意】Discord Bot を用意する

[Discord Developer Portal](https://discord.com/developers/applications) で：

1. **New Application** → 名前を付ける（例：Unity-post）。
   
2. 左メニュー **【Bot】**
   - **Reset Token** → 表示されたトークンをコピー
     - 注意：あとで設定ファイルへ。再表示はできません。
   - 左メニュー **【Installation】** の **Install Link** を **None** にして保存する
     - これをしないと次で「プライベートアプリにはデフォルトの認証リンクを設定できません」と出て保存できません。
   - **Public Bot を OFF**（自分以外が招待できないようにする）。
   - **Privileged Gateway Intents** ： **Server Members Intent を ON**
     - 新メンバー検知に必要です。
     - Message Content Intent は **不要**（本文は読まないため）。

3. 左メニュー **【General Information】** の **Application ID** をコピー。

4. 次のURLの `<APP_ID>` を置き換えてブラウザで開き、自分が管理者であるサーバーを選んでbotを招待：
   ```
   https://discord.com/oauth2/authorize?client_id=<APP_ID>&scope=bot&permissions=0
   ```
   - `permissions=0` なので、Botは発言・削除・管理が一切できません。
   - 見えるのは `@everyone` が見られるチャンネルだけです
   -（非公開チャンネルも拾いたい場合は、そのチャンネルでBotに「チャンネルを見る」を許可）。

5. **自分のユーザーID**：Discordの ユーザー設定 → 詳細設定 → **開発者モード ON** → 自分のアイコンを右クリック → **ユーザーIDをコピー**。
   - サーバーを限定したいときは、サーバーアイコンを右クリック → サーバーIDをコピー → `guildIds` に入れる

## 3. 【任意】Bluesky のアプリパスワードを作る

- Bluesky の 設定 → プライバシーとセキュリティ → **アプリパスワード** → 追加。
  - 名前は「Unity-post」など。
  - 「DMへのアクセスを許可」は **OFF のまま** でOK。
  - 普段のパスワードは使わないでください（漏れても、アプリパスワードだけ削除すれば無効化できます）。

## 4. シーンを組む

> **手早く試したいとき**：  
> メニューの **Unity Post > デモシーンを作る** で、床・仮の樹・Apple プレハブ・生成エリア・ルールのつなぎ込みまで自動で組めます（Ctrl+Z で取り消し可）。  
> 新しいシーン（File > New Scene）で実行するのがおすすめです。以下は手で組む場合の説明です。


空の GameObject「UnityPost」を作り、次を付けます：

- `PetEventHub`（1つだけ）
- `PetReactionRouter`
- `DiscordGatewaySource`
- `BlueskyNotificationSource`
- `PetPostDebugPanel`（確認用。エディタと Development Build でだけ表示）

演出は、それぞれのオブジェクトに付けます：

| 演出 | 付けるもの | メモ |
|---|---|---|
| リンゴが落ちる | `SpawnPrefabReaction` | リンゴプレハブ（Rigidbody＋Collider）。樹の葉のあたりに BoxCollider（isTrigger）を置いて `spawnArea` に。地面にも Collider を |
| 手紙が届く | `SpawnPrefabReaction` | 手紙プレハブに `PetEventTextReceiver` を付け、`onSummary` → TextMeshPro の `text`（Dynamic string）につなぐと「〇〇さんがいいねしてくれたよ」が出る |
| キラキラ | `ParticleBurstReaction` | Looping OFF・Play On Awake OFF の ParticleSystem |
| ペットが跳ねる | `AnimatorTriggerReaction` | Animator に `React` などのトリガーを作っておく |
| 効果音 | `AudioReaction` | 複数のクリップからランダム、ピッチも少し揺れる |

### ルールの例（PetReactionRouter）

ルールは上から順に見て、一致したものを全部実行します（`stopAfterMatch` で打ち切り）。

| # | label | sources | kinds | actorIds | pick | 演出 |
|---|---|---|---|---|---|---|
| 1 | 仲良しさん | Everything | Everything | `123…,did:plc:…` | All | 手紙＋効果音（stopAfterMatch ON） |
| 2 | 新メンバー | Discord | MemberJoin | | All | 手紙＋ペットが跳ねる |
| 3 | 呼ばれた | Discord, Bluesky | Mention, DirectMessage | | WeightedRandom | リンゴ（重み9）／金のリンゴ（重み1） |
| 4 | いいね系 | Bluesky | Like, Repost | | WeightedRandom | キラキラ／リンゴ |
| 5 | フォロー | Bluesky | Follow | | All | 手紙 |

**演出の登録方法**：ルールの options の「＋」→ action の「＋」→ 演出を付けたオブジェクトをドラッグ → 関数一覧の **Dynamic PetEvent** にある `Play` を選ぶ。
（Static Parameters の `Play()` でも動きますが、手紙に差出人を渡すには Dynamic のほうを選んでください）

## 5. 設定ファイルを書く

1. 手順4のコンポーネントを付けてから一度 ▶再生 すると、雛形が自動で作られ、Consoleに場所が出ます（コンポーネントがないと何も起きません）。
2. 再生を止めて値を書き、もう一度再生してください。デバッグパネルの「フォルダを開く」でも開けます。

- Windows：`%USERPROFILE%\AppData\LocalLow\<Company Name>\<Product Name>\petpost_config.json`
  （Company Name / Product Name は Player Settings の値。変えると場所も変わります）

```json
{
    "discord": {
        "enabled": true,
        "botToken": "コピーしたBotトークン",
        "myUserId": "123456789012345678",
        "guildIds": []
    },
    "bluesky": {
        "enabled": true,
        "identifier": "yourname.bsky.social",
        "appPassword": "xxxx-xxxx-xxxx-xxxx",
        "service": "https://bsky.social"
    }
}
```

このファイルは **Assets の外** にあるので、ビルドにもGitにも入りません。
**Assets の中にコピーしないでください。**

## 6. 動作確認

1. ▶再生 → 左上のパネルで、Discord / Bluesky が **緑●Connected** になるのを確認。
2. パネルの「テスト送信」ボタンで、本物と同じ形の偽イベントが流れます。ルールと演出をここで調整。
   「同じ人がメンション×5」で、クールダウンにより1回だけ反応することも確認できます。
3. 本物で試す：
   - **Discord・Botへのメンション**：自分のサーバーで `@（Botの名前）` とBotをメンション → `botMentions` で反応。
   - **Discord・自分へのメンション**：自分で自分をメンションしても反応しません（仕様）。チャンネルの ウェブフック を作って、PowerShell から送ると試せます：
     ```powershell
     $url = "https://discord.com/api/webhooks/xxxx/yyyy"   # チャンネル設定 → 連携サービス → ウェブフック
     Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Body '{"content":"<@あなたのユーザーID> こんにちは"}'
     ```
     （ウェブフックのURLもトークンと同じく秘密です。試したら削除してOK）
   - **Bluesky**：自分の操作では通知が来ないので、別アカウントか友人に「いいね」してもらう。届くまで最大30秒ほどかかります。

## 7. 困ったとき

| 症状 | 原因と対処 |
|---|---|
| Discord が赤●「4004」 | トークンが違う。Portal で Reset Token して設定ファイルを更新 |
| Discord が赤●「4014」 | Server Members Intent が OFF。Portal の Bot 設定で ON |
| 新メンバーに反応しない | Botが参加しているサーバーか、`guildIds` の指定を確認 |
| メンションに反応しない | `myUserId` が正しいか。Botがそのチャンネルを見られるか |
| Bluesky が赤●「ログインに失敗」 | identifier（ハンドル）とアプリパスワードを確認。誤ったまま連打しないよう、自動では再試行しません |
| 再生しても Console に何も出ない | シーンに Unity Post のコンポーネントが付いていない（手順4） |
| デバッグパネルが小さすぎる | Game ビューが4Kなど高解像度。Full HD にするか `uiScale` を上げる |
| ボタンを押してもリンゴが落ちない | ルールの **Enabled** が OFF、または演出の **Weight** が 0。Console の `[UnityPost]` の警告に理由が出ます |
| 何も起きない | シーンに `PetEventHub` があるか。ルールの sources / kinds が Nothing になっていないか。Console の `[UnityPost]` ログ |
| 一斉に来ると演出が詰まる | Hub の `minSecondsBetweenEvents`（間隔）と `maxQueued`（上限）で調整 |

## 8. 受信元を増やす（Misskey、Gmail など）

1. `PetEvent.cs` の `PetEventSourceKind` に1行追加（例：`Misskey = 1 << 4,`）。必要なら `PetEventKind` にも。
2. `PetEventSource` を継承して、届いたら `Emit()` するだけ：

```csharp
using PetPost;

public class MisskeySource : PetEventSource
{
    public override string DisplayName => "Misskey";

    protected override void StartSource()
    {
        SetStatus(SourceStatus.Connecting);
        // ここで WebSocket 接続やポーリングを開始
        // 届いたら（別スレッドからでもOK）:
        // Emit(new PetEvent(PetEventSourceKind.Misskey, PetEventKind.Like, "reaction", userId, userName));
    }

    protected override void StopSource()
    {
        // 接続を止める
    }
}
```

受信元でなくても、どのスクリプトからでも `PetEventBus.Publish(...)` で投函できます（タイマーで「おやつの時間」を送る、など）。
トークン類は `PetPostConfig` にセクションを足して、同じファイルから読むのがおすすめです。

## 9. 安全メモ

- Botトークン・アプリパスワードは **設定ファイルにだけ** 書く。スクリーンショットや配信にも映さない。
- 漏れたかも？と思ったら：Discord は Reset Token、Bluesky はアプリパスワードを削除して作り直し。
- このBotは **送信しない・権限ゼロ・コマンドなし** なので、ほかの人が操作する入口がありません。
- 友人に配る場合は、各自が自分のBotを作って自分の設定ファイルを用意する形になります（1つのBotトークンを配らないこと）。
