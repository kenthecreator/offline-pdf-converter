# Offline PDF Converter v3.2.0

完全オフライン動作を前提にした、Windows x64向けのPDF/画像変換・PDF編集デスクトップアプリです。Python、Poppler、Adobe製品、外部変換サービスを使わず、発行済みの `.exe` をダブルクリックして利用できます。

## 変更しない基本原則

- 実行時のインターネット接続・外部送信・追加ダウンロードを必要としません。
- 利用者による別ソフト・ランタイムの追加インストールを必要としません。
- Windows版は配布するexe一つだけで、OCRを含む機能を使える構成です。
- 内部の部品展開は自動で行い、利用者にエンジンの場所を設定させません。

v3.2.0はWindows 10（1903以降）／11のx64版とmacOS Apple Silicon版を配布します。Windows版は実行時にユーザーの一時フォルダへの書き込みが必要です。内蔵OCRの部品は初回使用時に展開します。.NETのネイティブ部品も内部で展開されるため、「配布するファイルが一つ」と「実行中にファイルを一切作らない」は異なります。

Mac版アプリのPDF・画像処理は自己完結しています。OCRには別途インストール済みのTesseract 5が必要です。アプリに含まれる日本語・英語の認識データを利用します。Mac版はad-hoc署名で、Appleの公証は未実施です。

詳細と確認範囲は [v3.2.0リリース内容](docs/RELEASE_DETAILS_v3.2.0.md) を参照してください。

## 技術構成の提案

「完全オフライン」「追加ランタイムの事前インストール不要」「Adobe非依存」「PDF各ページの画像化」を満たすため、次の構成を採用しています。

| 領域 | 採用技術 | 理由 |
| --- | --- | --- |
| UI | .NET 8 / C# / Avalonia UI | クロスプラットフォーム開発とWindows x64の自己完結 `.exe` 発行に向いている。WPF風のXAMLで保守しやすい。 |
| PDF → 画像 | PDFtoImage + PDFium + SkiaSharp | Adobe非依存。PDFiumで各ページをレンダリングし、PNG/JPEGへ保存できる。 |
| 画像 → PDF | PDFsharp | MITライセンス。JPEG/PNGをPDFページへ配置する用途に向いている。 |
| テキスト出力 | PdfPig | PDFに埋め込まれた文字情報を、全文・ページ単位・選択範囲からTXTへ保存する。 |
| 配布 | self-contained single-file publish | .NET Runtimeや外部DLLを別途入れずに起動できる。 |

WPF/WinUIはWindows専用UIとして有力ですが、クロスプラットフォーム開発とWindows用単体exe発行の扱いやすさを重視してAvaloniaを選んでいます。MuPDF系はAGPLまたは商用ライセンスの検討が必要になりやすいため、この実装では採用していません。

改良版の保存・再実行・出力プリセット・オフラインOCRについては [改良版の使い方](docs/IMPROVEMENTS.md) を参照してください。Windows版はOCR本体・日本語／英語の認識データもexeに内蔵し、追加インストールや実行時のダウンロードは不要です。

## 主な機能

- PDFをページごとにPNG/JPEGへ変換
- 普通 / 高画質 / 超高画質の画質選択
- 複数ページPDF対応
- 複数PDFの一括変換
- `1,3,5-7` 形式で変換ページを指定
- パスワード付きPDFの読込（追加時にパスワード入力ポップアップを表示）
- JPEG / PNG画像を1つのPDFに結合
- A4縦、A4横、画像サイズに合わせる
- 余白あり/なし
- 複数PDFを1つのPDFに結合
- 複数ページPDFを1ページずつ別PDFに分割
- 複数ページPDFから指定ページを削除して新しいPDFを作成
- 複数ページPDFから選択ページだけを元の順番で1つのPDFとして出力
- PDFに埋め込まれた文字情報をTXTへ出力（全文、選択ページ、プレビュー上でドラッグした選択範囲）
- 選択範囲は「コピー」またはWindowsの`Ctrl+C`／Macの`⌘C`でコピー
- PDF編集時のページプレビュー表示
- ページプレビューのアイコン/リスト表示切り替え
- プレビュー上で削除ページをチェック選択
- PDFページ上への文字・テキスト追加
- テキストボックスの移動、リサイズ、コピー/貼り付け、取り消し
- OS上で利用可能なフォント選択
- 太字、下線、テキスト色、背景色の設定
- 四角形、角丸四角形、丸、線の追加
- 図形の移動、リサイズ、回転、コピー/貼り付け
- 図形の塗り潰し色、境界線の色、境界線の太さ、色なし設定
- PDF編集プレビューの拡大/縮小、Ctrl+マウスホイール、トラックパッドのピンチ操作
- 起動時にデバイスのライト／ダークモードを判別して自動適用（起動後は手動切り替え可能）
- ドラッグ＆ドロップ
- 進捗バー、完了メッセージ、分かりやすいエラー表示

## ディレクトリ構成

```text
offline-pdf-converter/
  src/OfflinePDFConverter/   アプリ本体
  docs/BUILD_WINDOWS.md           Windows用ビルド手順
  docs/MANUAL.md               使い方マニュアル
  docs/DISTRIBUTION.md            配布ファイル構成
  docs/ARCHITECTURE.md            設計メモ
  THIRD_PARTY_LICENSES.md         使用ライブラリとライセンス一覧
```

## Windows用単体exeの作成

詳細は [docs/BUILD_WINDOWS.md](docs/BUILD_WINDOWS.md) を参照してください。

基本コマンド:

```bash
dotnet publish "src/OfflinePDFConverter/OfflinePDFConverter.csproj" \
  -p:PublishProfile=WindowsSingleFile \
  -o artifacts/windows-v3.2.0
```

出力先:

```text
dist/win-x64-single-offline-pdf-converter/Offline PDF Converter.exe
```

## ライセンス

使用ライブラリとライセンスは [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) にまとめています。

アプリアイコン画像（`src/OfflinePDFConverter/Assets/AppIcon.png` および `AppIcon.ico`）の著作権は GitHub ユーザー `kenthecreator` に帰属します。ソースコード本体のMITライセンスとは別の権利表示として扱ってください。
