# OSCQuery Explorer

Windows向けのOSCQueryを用いたOSC操作支援ツールです。  
VRChat向けに開発されましたが、VRChat専用アプリではありません。  

## 主な機能

- mDNSによるOSCQueryサービスの探索と、HTTPエンドポイントの手動指定
- OSCQueryアドレス空間のツリー表示、検索、定期更新
- ノードの現在値・メタデータ表示と、型に応じたOSC値の送信
- よく使うノードを直接操作できるPinnedパネル
- OSCQueryに存在しないカスタムノードの登録
- OSC送受信とSYSTEMイベントの共有履歴
- アドレス、Type Tag、ポート、ログレベル等によるログフィルター
- TXT／CSVエクスポート
- 接続先をミラーするローカルOSCQuery Server
- サービス別のPinned、カスタムノード、公開状態、Explorer状態の保存
- System／Light／Darkテーマ
- GitHub Releasesを利用した更新確認

設定は`%LocalAppData%\OSCQueryExplorer\settings.json`へ保存されます。OSC履歴はメモリ上にのみ保持され、アプリ終了時に破棄されます。

## 必要環境

- Windows 11
- .NET 10 Desktop Runtime（インストーラー版では未導入の場合に自動インストールされます）

ポータブル版ではNETランタイムの事前インストールは不要です。
インストーラー版でRuntimeが未導入の場合は、Microsoft公式配布元から取得するためインターネット接続が必要です。

## ビルドと実行

```powershell
dotnet restore .\OSCQueryExplorer.slnx
dotnet build .\OSCQueryExplorer.slnx
dotnet test --project .\OSCQueryExplorer.Tests\OSCQueryExplorer.Tests.csproj
dotnet run --project .\OSCQueryExplorer\OSCQueryExplorer.csproj
```

起動後は自動接続しません。Discoveryで検出されたサービスを選択するか、OSCQuery HTTPエンドポイントを入力して明示的に接続してください。

## Logフィルター

空白区切りの複数条件はANDとして扱われます。先頭の`-`は除外条件です。引用符で空白を含む値を指定でき、`\`で次の文字をエスケープできます。

| 入力例 | 内容 |
| --- | --- |
| `Velocity` | 表示行全体に`Velocity`を含む |
| `-Velocity` | `Velocity`を含む行を除外 |
| `Gesture -Velocity` | `Gesture`を含み`Velocity`を含まない |
| `port:9001` | 送信元または宛先ポートが9001 |
| `src:9001` | 送信元ポートが9001 |
| `dst:9001` | 宛先ポートが9001 |
| `addr:/input/` | OSCアドレスに`/input/`を含む |
| `type:f` | Type Tagに`f`を含む |
| `level:ERROR` | ERRORのSYSTEMイベント |

`port:`、`src:`、`dst:`は数字の部分一致ではなく、ポート番号として比較されます。Record OFFはOSC履歴の追加だけを停止し、SYSTEMイベントと現在値の更新は継続します。Pauseは画面反映だけを停止します。

## 設定

画面上部の「設定」から、テーマ、ログ文字サイズ、OSCQueryツリーの更新間隔、LAN公開、起動時の更新確認を変更できます。更新確認はGitHub Releasesへ接続し、既定では24時間に1回まで実行します。新しいバージョンがある場合も、確認なしにブラウザーを開くことはありません。

LAN公開は既定で無効です。有効にすると、次回接続時からローカルOSCQuery ServerとOSC受信ポートがLANインターフェイスで待ち受けます。信頼できるネットワークでのみ使用してください。

## プロジェクト構成

- `OSCQueryExplorer` — WPF UI、ViewModel、画面操作
- `OSCQueryExplorer.Core` — ノード、履歴、検索、設定、ツリー管理
- `OSCQueryExplorer.Protocol` — OSC codec／UDP、OSCQuery HTTP／WebSocket、mDNS、ローカルServer
- `OSCQueryExplorer.Tests` — codec、履歴、検索、ツリー統合のテスト
- `installer` — Inno Setup定義
- `.github/workflows` — ビルド／Release Workflow

## 謝辞

OSCQuery Explorerは、いくつかのオープンソースプロジェクトを利用しています。開発者およびコントリビューターの皆様に感謝します。

サードパーティーライブラリの詳細は[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)を参照してください。

## ライセンス

OSCQuery Explorerは[MIT License](LICENSE)で公開されています。
