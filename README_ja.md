<div align="center">

<img src="docs/assets/cmc-logo.png" alt="CMCLauncher logo" width="120" height="120" />

# CMCLauncher

**Minecraft Java の世界は、ここから。**

[English](README.md) · [简体中文](README_zh.md) · [日本語](README_ja.md)

<p>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Latest release" src="https://img.shields.io/github/v/release/CMCLauncher-Dev/CMCLauncher?style=flat-square&amp;color=2088ff" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/CMCLauncher-Dev/CMCLauncher/total?style=flat-square&amp;color=35b879" /></a>
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/actions/workflows/ci.yml"><img alt="CI" src="https://img.shields.io/github/actions/workflow/status/CMCLauncher-Dev/CMCLauncher/ci.yml?branch=main&amp;style=flat-square&amp;label=build" /></a>
<a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-AGPL--3.0-7654c4?style=flat-square" /></a>
<br />
<img alt="Windows x64" src="https://img.shields.io/badge/Windows-x64-0078d4?style=flat-square" />
<img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4?style=flat-square" />
<img alt="WinUI 3" src="https://img.shields.io/badge/WinUI-3-00a4ef?style=flat-square" />
<a href="https://github.com/CMCLauncher-Dev/CMCLauncher/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/CMCLauncher-Dev/CMCLauncher?style=flat-square" /></a>
</p>

### [ダウンロード](https://github.com/CMCLauncher-Dev/CMCLauncher/releases) · [公式サイト](https://cmcl.ctfun.top/) · [問題を報告](https://github.com/CMCLauncher-Dev/CMCLauncher/issues)

</div>

Minecraft Java Edition 向けの Windows ネイティブランチャーです。C#、.NET 8、WinUI 3 で構築されています。

英語・簡体中文・日本語・ロシア語・フランス語・スペイン語・アラビア語の UI 翻訳を内蔵しており、**設定 → 全般 → 言語**で自動または手動で選択できます。

## プラットフォーム対応

| プラットフォーム | UI | 互換性 | 更新・保守状況 | 対応システム / ハードウェア |
| --- | --- | --- | --- | --- |
| Windows | WinUI 3 | ✅ 対応 | 継続的に保守 | Windows 10（2004 以降）/ 11、x64 |
| Linux | Avalonia | ⚠️ 実験段階 | 安定するまで実験的な更新を継続 | Debian 系、Arch 系、Red Hat 系ディストリビューション、x64 |
| macOS | Avalonia | ⚠️ 実験段階。**重大な問題が発生する可能性があります** | 保守の優先度は低め | Intel（x64）、Apple M シリーズ（ARM64） |

Linux と macOS のビルドはテスト用です。macOS ビルドは **署名・公証されていません**。テスト前にインスタンスをバックアップしてください。

動作要件と既知の制限については、[Linux パッケージガイド](docs/LINUX_PACKAGES.md)と [Avalonia フロントエンドの説明](CMCLauncher.Avalonia/README.md)を参照してください。

## プレビュー

<div align="center">
<img src="docs/assets/launcher-home.png" alt="CMCLauncher home" width="960" />
</div>

## 次のワールドに向けて

<table>
<tr>
<td width="33%" valign="top">
<h3>🧩 インスタンスを管理</h3>
<p>分離したゲームフォルダー、ローカル読み込み、PCL / HMCL 互換。</p>
</td>
<td width="33%" valign="top">
<h3>📦 Modpack とローダー</h3>
<p>Fabric、Forge、NeoForge、Modrinth パックと対応 ZIP の読み込み。</p>
</td>
<td width="33%" valign="top">
<h3>🔎 コミュニティを探す</h3>
<p>Modrinth / CurseForge から Mod、リソースパック、シェーダーを検索。</p>
</td>
</tr>
<tr>
<td width="33%" valign="top">
<h3>🎨 自分らしい見た目</h3>
<p>ライト／ダークテーマ、配色とフォントの調整、7 言語の UI。</p>
</td>
<td width="33%" valign="top">
<h3>📈 システムの状態を確認</h3>
<p>CPU、メモリ、GPU ごとのリアルタイムグラフと Java メモリの自動割り当て。</p>
</td>
<td width="33%" valign="top">
<h3>🔄 更新と接続</h3>
<p>検証付き更新、Beta チャンネル、Frp Client のバージョンと接続を管理。</p>
</td>
</tr>
</table>

## クイックスタート

### 動作要件

- Windows 10 バージョン 2004（ビルド 19041）以降の x64、Linux、または macOS。
- Minecraft Java Edition のプロフィールを持つ Microsoft アカウントでログインしてください。オフラインアカウントを利用するには、ゲームを所有する Microsoft アカウントで少なくとも一度ログインする必要があります。

### 使い始める

1. [Releases](https://github.com/CMCLauncher-Dev/CMCLauncher/releases) から `CMCLauncher-1.0.5-win-x64.zip` をダウンロードし、全体を展開して `CMCLauncher.exe` を実行します。
2. **設定 → Java** で既存の Java をスキャンまたはインポートするか、Temurin Java をダウンロードします。
3. **設定 → アカウント** で Microsoft デバイスコードログインを選択します。ブラウザーで認証を完了し、Minecraft Java プロフィールの確認を待ちます。
4. ゲームバージョンをダウンロードするか、既存の `.minecraft` フォルダーまたは Modpack をインポートします。ホーム画面でバージョンとアカウントを選択し、起動します。

**設定 → アカウント** の外部ログイン（authlib-injector）は、Microsoft アカウントの Minecraft Java Edition プロフィール確認後に利用できます。

配布アーカイブには .NET と Windows App SDK ランタイムが含まれているため、.NET を別途インストールする必要はありません。

## 機能

| 分野 | 対応機能 |
| --- | --- |
| ゲームとインスタンス | 正式版・スナップショット・エイプリルフール版の閲覧、分離インスタンスの作成、ローカルゲームフォルダーのインポート |
| ローダー | Fabric・Forge・NeoForge の検索とインストール、インポートしたバージョンのローダー判別 |
| Modpack | Modrinth `.mrpack`、対応マニフェストを含む ZIP、MultiMC / Prism の ZIP エクスポートのインポート。ドラッグ＆ドロップに対応 |
| コミュニティコンテンツ | Modrinth・CurseForge・両方から選択、ページ切り替え、プロジェクトのバージョン表示、ゲーム・ローダー・インストール先の互換性確認 |
| コンテンツ管理 | Mod のメタデータとアイコン表示、インポート・無効化・削除、ワールドごとのデータパック管理 |
| 設計図 | `.litematic`・`.schematic`・`.schem`・`.nbt` ファイルの検索・インポート・エクスポート・名前変更・削除 |
| Java とメモリ | Temurin Java のスキャン・インポート・ダウンロード、Java バージョンの自動選択、ヒープメモリの自動／手動割り当て |
| Frp Client | 公式 Windows x64 版のダウンロード、INI/TOML 設定の検証、クライアントの起動と停止 |
| 外観と言語 | ライト・ダーク・システムテーマ、配色・フォント・拡大率・外観プリセット、ランチャーとゲームの言語設定 |
| アカウントと診断 | Microsoft デバイスコードログイン、外部 authlib-injector ログイン、Windows DPAPI による資格情報の暗号化、実行ログ、ゲームエラーレポートのエクスポート |

UI 翻訳は実行ファイル横の `locales/` にある編集可能な JSON ファイルから読み込みます。有効な言語パックを追加すると言語選択に表示されます。スキーマ、再読み込みの動作、翻訳方法は [言語パック](docs/LOCALIZATION.md)を参照してください。

## 対応範囲と制限

### Frp Client

**その他 → Frp Client** を開き、オンラインのリリース一覧からバージョンを選んで **ダウンロード** を押します。**リリースを更新** で一覧を更新し、**さらに読み込む** で過去のバージョンを取得します。ダウンロード元は [frp 公式リリース](https://github.com/fatedier/frp/releases)で、公式 SHA-256 の検証が必要です。クライアントは必要に応じてダウンロードし、ランチャーには同梱しません。

## データとログ

| 場所 | 内容 |
| --- | --- |
| `%APPDATA%/CMCLauncher.Native/settings.json` | ランチャー設定、インスタンス記録、暗号化されたアカウント資格情報 |
| `%APPDATA%/CMCLauncher.Native/library/` | 既定のインスタンスとライブラリ。ゲームのルートディレクトリはランチャーで変更可能 |
| `%APPDATA%/CMCLauncher.Native/runtimes/` | ランチャーがダウンロードした Java |
| 実行ファイル横の `logs/launcher-yyyyMMdd.log` | ランチャーの実行ログ |
| 各インスタンス内の `logs/` と `crash-reports/` | ゲームログとクラッシュレポート |

## ソースから実行

Windows に .NET 8 SDK と、WinUI 3 プロジェクトのビルドに必要な Windows SDK／開発ツールをインストールしてください。対象フレームワークは `net8.0-windows10.0.19041.0`、アーキテクチャは x64 です。リポジトリをクローンし、ルートディレクトリで次を実行します。

```powershell
git clone https://github.com/CMCLauncher-Dev/CMCLauncher.git
cd CMCLauncher
dotnet run --project CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -p:Platform=x64
```

ローカルで簡単にビルドするには、リポジトリ直下の `build-and-run.cmd` をダブルクリックします。自己完結型の Release ビルドが `.artifacts/local-runs/` に出力されます。

アプリを開かずにビルドする場合は、PowerShell で `./build-and-run.ps1 -NoLaunch` を実行します。

CurseForge をローカルでテストする場合は、コマンドプロンプトで `build-and-run.cmd "-curseforgekey=YOUR_KEY"` を実行します。

### Microsoft ログインの設定

client ID のないソースビルドでも UI は起動しますが、Microsoft ログインは利用できません。有効にするには `.config/clientid.local.json` を作成し、設定済みの公開 OAuth client ID を記入します。

```json
{
  "clientId": "YOUR_MICROSOFT_CLIENT_ID"
}
```

### ビルドとパッケージ

Release 構成をビルドします。

```powershell
dotnet build CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64
```

自己完結型のディレクトリを発行し、ZIP アーカイブを作成します。

```powershell
dotnet publish CMCLauncher.WinUI/CMCLauncher.WinUI.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o dist/cmc-launcher
./scripts/package-release.ps1 -PublishDirectory dist/cmc-launcher -ArchivePath dist/CMCLauncher-1.0.5-win-x64.zip
```

### テスト

ネイティブ回帰テストを実行します。

```powershell
dotnet run --project CMCLauncher.Tests/Windows/CMCLauncher.Tests.csproj -c Release
```

これらのテストは Windows DPAPI を使用するため、Windows 上で実行する必要があります。最後のローカル Java 検出テストには、PATH 上の `java` も必要です。

アプリのディレクトリを発行した後、WinUI 統合チェックを実行します。

```powershell
& ./dist/cmc-launcher/CMCLauncher.exe --verify-ui "$PWD/.artifacts/winui-check"
```

このチェックは独立したデータディレクトリを使用し、スクリーンショットと `passed.txt` を生成します。失敗は `failure.txt` に記録されます。

## ソース構成

| ディレクトリ | 内容 |
| --- | --- |
| [`CMCLauncher.WinUI/`](CMCLauncher.WinUI/) | デスクトップのエントリーポイント、ウィンドウ、ページ、コントロール、UI サービス |
| [`CMCLauncher.Core/`](CMCLauncher.Core/) | ダウンロード、認証プロトコル、リソース閲覧、ストレージ、プラットフォーム非依存サービス |
| [`CMCLauncher.Platform/Windows/`](CMCLauncher.Platform/Windows/) | Windows 資格情報、メモリ・地域情報の取得、レジストリ連携、更新のインストール |
| [`CMCLauncher.Tests/Core/`](CMCLauncher.Tests/Core/) | クロスプラットフォームのコア回帰チェック |
| [`CMCLauncher.Tests/Windows/`](CMCLauncher.Tests/Windows/) | ネイティブ回帰テストと証明書フィクスチャ |
| [`scripts/`](scripts/) | 発行、リソース整理、パッケージ用スクリプト |
| [`docs/`](docs/) | 公式サイトのソースとスクリーンショット |
| [`.github/workflows/`](.github/workflows/) | 自動テスト、ビルド、タグ付きリリース |

## フィードバックと貢献

[Issues](https://github.com/CMCLauncher-Dev/CMCLauncher/issues) で問題を報告してください。ランチャーと Windows のバージョン、Minecraft とローダーのバージョン、再現手順を添えてください。起動に失敗した場合は、エラーレポートまたは関連ログも添付してください。共有前にアカウント情報やローカルパスが含まれていないか確認してください。

Pull Request を送る前に、変更に関連するテストを実行してください。レイアウト変更にはスクリーンショットを添え、ダウンロード・インストール・インポートを変更した場合は、キャンセルや失敗後の後片付けも確認してください。

## ライセンス

CMCLauncher は [GNU Affero General Public License v3.0](LICENSE) で公開されています。サードパーティの依存関係には各ライセンスが適用されます。

Minecraft、Mod、Modpack は各所有者に帰属します。CMCLauncher は Mojang Studios、Microsoft、NetEase と提携していません。
