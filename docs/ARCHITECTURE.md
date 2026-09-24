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

## テキスト出力

PdfPigでPDFの文字レイヤーと文字位置を読み取り、全文、選択ページ、またはプレビュー上のドラッグ矩形と重なる文字列をページ番号付きのUTF-8 TXTとして保存します。選択文字列は画面のボタン、Windowsの`Ctrl+C`、Macの`⌘C`からクリップボードへコピーできます。複数PDFはファイル名の見出しを付けて1つのTXTへまとめます。埋め込み文字が存在しない画像PDFはOCR対象として明示し、このサービスでは画像認識を行いません。

文字/テキスト追加では、画面上の編集状態を `PdfSimpleEditRequest` にまとめ、書き出し時にPDF座標へ変換して反映します。テキストボックスは最前面レイヤーとして扱い、図形は四角形、角丸四角形、丸、線を扱います。

フォントはアプリに同梱せず、OS上で利用可能なフォントを参照します。配布物にフォントファイルを含めないことで、フォントライセンス上のリスクを抑えています。

ページプレビューはPDFium/PDFtoImageでレンダリングします。編集画面ではプレビュー画質を高め、ズーム倍率をUI操作、Ctrl+マウスホイール、トラックパッドのピンチ操作で変更できます。

## 追加しやすい機能

- 画質指定: JPEG品質と用途別設定をUIから指定できます。
- 出力名ルール変更: `FileNameHelper` を拡張
- OCR: 専用に構築したTesseract・認識データ・ライセンスをexeに内蔵します。

## 制限

- PDFiumは1プロセス内での同時レンダリングを避けています。
- 画像PDFの文字認識は `OfflineOcrService` からローカルのTesseract 5を呼び出します。Windows版はTesseract本体・認識データをexeに埋め込んでいます。通常の文字抽出は従来どおりPdfPigを使用します。
- 既存PDF内の文字を直接編集する機能は実装していません。文字や図形をPDF上に追加する方式です。
- 単体exe方式ではネイティブライブラリを一時フォルダに展開します。

## 改良版の構成

`AtomicFile` が出力の確定と衝突回避を共通化します。`BatchRunner` が個別出力操作の入力ごとの結果を管理します。`MainWindow.Editor.cs`、`MainWindow.Batch.cs`、`MainWindow.Ocr.cs`、`MainWindow.Presets.cs` に画面の役割を分離しました。OCRは `ProcessStartInfo.ArgumentList` で引数を渡し、シェルを経由せず、取消時には子プロセスも停止します。

## v3.2.0のWindows単体配布

`WindowsSingleFile.pubxml` が自己完結の単体exeを発行し、exe以外の出力ファイルが残る場合はエラーにします。`ocr/windows-x64.zip` はマネージドリソースとして内蔵し、部品本体・日本語／英語データ・ライセンス・由来情報・ファイルごとのハッシュを含みます。

`BundledOcrRuntime` は内蔵ZIP全体と各ファイルのSHA-256を確認し、ユーザー専用の一時領域へ展開します。外部のインストール先やPATHを検索しません。OCR画面からエンジン／認識データの場所指定を削除しています。展開は初回OCR時、後始末はアプリの通常終了時です。

OCR本体はMinGW-w64でWindows x64向けに静的リンクし、PNG・zlib以外の画像ライブラリ、curl、archive、訓練ツールを組み込みません。入力はアプリが生成したPNGに限定します。Windows標準のKernel32/UCRTだけに依存します。UTF-8マニフェストを付け、日本語を含むパスを扱う構成です。ソースの版・SHA-256と構築手順はvendor/ocr-source-manifest.jsonとscripts/にあります。
