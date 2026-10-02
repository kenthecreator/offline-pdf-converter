# 設計メモ

## 方針

起動時はAvaloniaの`PlatformSettings.GetColorValues().ThemeVariant`からデバイスの配色を取得し、Fluentテーマ、独自カラーパレット、ヘッダーアイコン、テーマ切替スイッチへ同じ初期値を適用します。起動後の手動切り替えは従来どおり利用できます。

UI、変換処理、PDF編集処理、データモデルを分けています。OCRも独立したサービスとして追加しています。

## レイヤー

```text
Views/
  MainWindow.axaml       日本語UI
  MainWindow.axaml.cs    画面操作、ファイル選択、進捗表示

Models/
  PdfToImageRequest      PDF→画像の入力条件
  PdfTextExtractionRequest PDF文字抽出の入力条件
  ImageToPdfRequest      画像→PDFの入力条件
  PdfMergeRequest        PDF結合の入力条件
  PdfSplitRequest        PDF分割の入力条件
  PdfDeletePagesRequest  ページ削除の入力条件
  PdfExtractPagesRequest 選択ページ出力の入力条件
  PdfSimpleEditRequest   テキストボックス/図形編集の入力条件
  PdfPagePreviewItem     ページプレビュー情報
  ConversionProgress     進捗通知
  ConversionResult       変換結果

Services/
  PdfToImageService      PDFium/PDFtoImageを使ったPDFレンダリング
  ImageToPdfService      PDFsharpを使ったPDF作成
  PdfDocumentService     PDF結合、分割、ページ削除、選択ページ出力、文字/図形追加
  PdfTextExtractionService PdfPigを使った埋め込み文字の抽出
  AppFontResolver        OS上のフォントをPDF出力へ解決
  FileNameHelper         元ファイル名を含む出力名の生成と衝突回避
  PageRangeParser        ページ範囲指定の解釈
  FriendlyErrorFormatter 専門用語を避けたエラー文言
```

## PDFレンダリング

PDFtoImageはPDFiumを利用します。PDFium呼び出しは並列処理向きではないため、複数PDFも1件ずつ処理します。壊れたPDFが混ざった場合は、そのファイルのエラーを記録して次のファイルへ進みます。

PDF→画像では、`PageRangeParser` で `1,3,5-7` 形式を解釈し、選択したページ番号だけをPDFtoImageへ渡します。パスワード付きPDFでは、追加時のポップアップで検証したファイル単位のパスワードを、ページ数取得と画像レンダリングの両方へ渡します。

出力名は`FileNameHelper`で共通生成します。単一入力は`元ファイル名_指定名`、複数入力を1つへまとめる処理は最大3件の元ファイル名を連結し、4件以上は`先頭2件_ほかN件_指定名`へ短縮します。分割とPDF→画像は各入力ファイルの元名を個別に付けます。すでに元名を含む指定名は重複して付加しません。

## 画像PDF化

PDFsharpで新規PDFを作り、選択された画像を順番に1ページずつ配置します。A4縦、A4横、画像サイズに合わせる、余白あり/なしをサービス側で扱います。

## PDF編集

PDF編集はPDFsharpを中心に処理します。結合、1ページずつ分割、指定ページ削除、選択ページのみの出力、文字/テキスト追加、図形追加を `PdfDocumentService` に集約しています。

選択ページ出力では、ページプレビューのチェックまたは `1,3,5-7` 形式で対象を指定します。選択ページを元PDF内の昇順で1つのPDFへ追加するため、非連続ページも連続したページ構成で出力されます。

PDFsharpで開くすべての処理にファイル単位のパスワードを渡せるようにし、結合、分割、ページ操作、文字・図形追加でもパスワード付きPDFを扱います。パスワードは設定へ保存せず、PDFが一覧から削除された時点でメモリからも削除します。

## v4.0の検索可能PDFと文字確認

PDF編集の文字抽出・TXT出力のUIは廃止しました。旧サービスは回帰検証用に残しています。

`SearchablePdfService` が原本ページをコピーし、OCR結果を不可視の文字レイヤーとしてPDFに追加します。既に文字を持つページはスキップします。`PaddleOcrService` は言語別モデル・辞書を使い、縦書き・横書きの読み順を調整します。`BundledPaddleOcrRuntime` が内蔵リソースのSHA-256を確認し、一時領域へ展開します。Mac・Windowsともに同じCPUエンジンを使い、実行時の外部送信・ダウンロードはありません。

Windowsの日本語TTCフォントは `FontCollectionReader` が先頭のフォントを単独のsfntデータへ変換してPDFsharpに渡します。OSフォント自体の再配布は行いません。内蔵OCRリソースは `.gitattributes` で改行変換を禁止し、Windowsでも固定ハッシュを維持します。

`MainWindow.OcrPreview.cs` がPdfPigの実際の文字位置から選択・コピーを提供します。`MainWindow.OcrMotion.cs` が左右の表示形式・スクロール、対象全ページのスキャン、5秒の最低表示時間、完了後の間接照明を管理します。確認画面は文字追加の編集画面と同じ左右配置・拡大縮小パネル・完了ボタンを使います。

文字/テキスト追加では、画面上の編集状態を `PdfSimpleEditRequest` にまとめ、書き出し時にPDF座標へ変換して反映します。テキストボックスは最前面レイヤーとして扱い、図形は四角形、角丸四角形、丸、線を扱います。

v4.2ではOCR文字層に使う日本語フォントとしてZen Kaku Gothic New Regular（SIL OFL 1.1）を未改変でexeに内蔵します。OSフォントの導入状況に依存しません。文字追加・そのプレビューも既定では内蔵フォントを使います。OSフォントは実際に列挙できたものから選択できます。OSフォントのファイルは再配布しません。

ページプレビューはPDFium/PDFtoImageでレンダリングします。編集画面ではプレビュー画質を高め、ズーム倍率をUI操作、Ctrl+マウスホイール、トラックパッドのピンチ操作で変更できます。

## 追加しやすい機能

- 解像度指定: v3.1.0と同じスライダーで200／300／600 dpiを選択します。JPEG品質は95です。
- 出力名ルール変更: `FileNameHelper` を拡張
- OCR: PaddleOCRのモデル・辞書・ライセンスをMac・Windows配布物に内蔵します。

## 制限

- PDFiumは1プロセス内での同時レンダリングを避けています。
- v4.1のOCRはPaddleOCR内蔵版です。Windows版はVisual C++の必要なDLLもexe内に同梱し、別途インストールを不要にしています。
- 既存PDF内の文字を直接編集する機能は実装していません。文字や図形をPDF上に追加する方式です。
- 単体exe方式ではネイティブライブラリを一時フォルダに展開します。

## 改良版の構成

画面のカテゴリーは `PDF編集`、`PDF↔画像`、`OCR処理` の3つです。`ConversionMode.Ocr` と `MainWindow.Ocr.cs` がOCR専用のPDF一覧、ページプレビュー、認識設定を管理します。PDF一覧と保存済みパスワードは既存画面と共有し、処理は既存の `OfflineOcrService` と `BatchRunner` を使います。

`AtomicFile` が出力の確定と衝突回避を共通化します。`BatchRunner` が個別出力操作の入力ごとの結果を管理します。`MainWindow.Editor.cs`、`MainWindow.Batch.cs`、`MainWindow.Ocr.cs`、`MainWindow.Presets.cs` に画面の役割を分離しました。PaddleOCRはアプリ内のONNX RuntimeでCPU推論します。旧Tesseract実装の子プロセス制御は回帰検証用に残しています。

## 旧v3.2.0のWindows単体配布（回帰検証用）

`WindowsSingleFile.pubxml` が自己完結の単体exeを発行し、exe以外の出力ファイルが残る場合はエラーにします。`ocr/windows-x64.zip` はマネージドリソースとして内蔵し、部品本体・日本語／英語データ・ライセンス・由来情報・ファイルごとのハッシュを含みます。

`BundledOcrRuntime` は内蔵ZIP全体と各ファイルのSHA-256を確認し、ユーザー専用の一時領域へ展開します。外部のインストール先やPATHを検索しません。OCR画面からエンジン／認識データの場所指定を削除しています。展開は初回OCR時、後始末はアプリの通常終了時です。

OCR本体はMinGW-w64でWindows x64向けに静的リンクし、PNG・zlib以外の画像ライブラリ、curl、archive、訓練ツールを組み込みません。入力はアプリが生成したPNGに限定します。Windows標準のKernel32/UCRTだけに依存します。UTF-8マニフェストを付け、日本語を含むパスを扱う構成です。ソースの版・SHA-256と構築手順はvendor/ocr-source-manifest.jsonとscripts/にあります。

## v4.2のWindows配布検証

`OfflineFeatureChecks`は配布exeから変換・ページ操作・文字/図形追加・4言語OCR・異常入力・取消・出力保護を実行します。`MainWindow.Verification`は明示的な`--verify-gui`起動に限り、実ウィンドウのコントロールイベント、プレビュー、OCR、選択・コピー、処理中の終了を確認します。通常起動では実行されません。検証レポートは実際のチェック一覧と失敗理由を記録します。

## v4.3の小文字OCRとビルド用フォント準備

ページ番号だけが文字のページもOCR対象とします。通常のA4は300dpi、検出前長辺上限は4096pxです。文字層にはNoto Sans Japaneseを内蔵します。ビルドの前に、固定版のフォント生成依存を準備します。これは開発用の手順で、利用者には不要です。

```sh
python -m pip install fonttools==4.61.1
python scripts/prepare-ocr-font.py
```

フォントは固定コミットとSHA-256で確認し、exe／app内に埋め込みます。利用時の追加取得はありません。
