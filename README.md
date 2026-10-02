# Offline PDF Converter (v4.4)

完全オフライン動作を前提にした、Windows x64／macOS Apple Silicon向けのPDF・画像変換とPDF編集のデスクトップアプリです。[公式ダウンロードページ](https://kenthecreator.github.io/offline-pdf-converter/)から配布ZIPを取得できます。

v4.4のOCR画面と変更点は [検索可能PDFの説明](docs/SEARCHABLE_PDF.md) を参照してください。PDF編集のTXT出力機能は廃止しました。

## v4.4のオフラインOCR

スキャンしたPDFに、検索・選択・コピーできる文字レイヤーを追加します。原本のページの見た目を保ったPDFとして書き出すため、画像だけだった資料を検索できるようになります。

- Mac・WindowsともにPaddleOCRの認識エンジンとモデルを内蔵し、実行時のネット接続・外部送信・追加ダウンロードを必要としません。
- Tesseract、Python、GPU、.NETランタイムの別途インストールは不要です。
- Windows版はVisual C++の必要なDLLと日本語OCR用フォントもexe内に含めます。利用者による追加インストール・初回ダウンロードは不要です。4.0・4.1・4.2の配布物は保持しています。
- Windows 10（1903以降）／11 x64、macOS Apple Silicon向けです。Mac版はad-hoc署名で、Appleの公証は未実施です。
- ネイティブ部品とOCRモデルは実行時に一時領域へ自動展開します。単体exeでも実行中の一時ファイルは作成されます。

[最新版のダウンロード](https://github.com/kenthecreator/offline-pdf-converter/releases/latest) ／ [v4.4リリース内容](docs/RELEASE_DETAILS_v4.4.0.md) ／ [OCRの仕様・制限](docs/SEARCHABLE_PDF.md)

v4.4では、トリミング範囲外の文字によって本文のOCRが省略される問題と、長い日本語が保存時に折り返されない問題を修正しました。v4.3から引き継いだ改善として、ページ番号のみが文字で本文が画像・輪郭線のPDFもOCRできるようにし、小さい日本語を検出前の縮小で失わないよう改善しました。非表示文字層にはNoto Sans Japaneseを内蔵します。実際の配布exeから主要機能と実ウィンドウを検証します。検証条件と未確認事項は[v4.4リリース内容](docs/RELEASE_DETAILS_v4.4.0.md)を参照してください。

## 技術構成の提案

「完全オフライン」「追加ランタイムの事前インストール不要」「Adobe非依存」「PDF各ページの画像化」を満たすため、次の構成を採用しています。

| 領域 | 採用技術 | 理由 |
| --- | --- | --- |
| UI | .NET 8 / C# / Avalonia UI | クロスプラットフォーム開発とWindows x64の自己完結 `.exe` 発行に向いている。WPF風のXAMLで保守しやすい。 |
| PDF → 画像 | PDFtoImage + PDFium + SkiaSharp | Adobe非依存。PDFiumで各ページをレンダリングし、PNG/JPEGへ保存できる。 |
| 画像 → PDF | PDFsharp | MITライセンス。JPEG/PNGをPDFページへ配置する用途に向いている。 |
| 検索可能PDF | PDFsharp + OCR + PdfPig | 原本のページに見えない文字を追加し、既存の文字レイヤーは重複させずに保持する。 |
| 配布 | self-contained single-file publish | .NET Runtimeや外部DLLを別途入れずに起動できる。 |

WPF/WinUIはWindows専用UIとして有力ですが、クロスプラットフォーム開発とWindows用単体exe発行の扱いやすさを重視してAvaloniaを選んでいます。MuPDF系はAGPLまたは商用ライセンスの検討が必要になりやすいため、この実装では採用していません。

改良版の保存・再実行・解像度設定・オフラインOCRについては [改良版の使い方](docs/IMPROVEMENTS.md) を参照してください。Mac・WindowsともにOCRモデルを内蔵します。Windows単体exeの同梱構成と検証条件は [Windowsビルド手順](docs/BUILD_WINDOWS.md) を参照してください。

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
- OCR処理で検索・選択・コピーできる文字レイヤー付きPDFを作成
- PDFの言語を日本語（横書き）／日本語（縦書き）／英語／日本語・英語 混在から選択
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
- Auto（初期設定）でデバイスのライト／ダーク設定に追従。手動切り替えも可能
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

Mac・Windowsのv4.4配布物を作成:

```sh
python3 scripts/build-paddle-edition.py --target both
```

出力は `dist/paddle-edition/` の `Offline PDF Converter (v4.4)-macOS-arm64.zip` と `Offline PDF Converter (v4.4)-Windows-x64.zip` です。Windows単独の発行には `-p:PaddleOcrEdition=true` を指定してください。旧Tesseract実装は回帰検証用に残しています。

## ライセンス

使用ライブラリとライセンスは [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) にまとめています。

アプリアイコン画像（`src/OfflinePDFConverter/Assets/AppIcon.png` および `AppIcon.ico`）の著作権は GitHub ユーザー `kenthecreator` に帰属します。ソースコード本体のMITライセンスとは別の権利表示として扱ってください。
