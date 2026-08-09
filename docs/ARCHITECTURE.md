# 設計メモ

## 方針

UI、変換処理、PDF編集処理、データモデルを分けています。将来的にページ範囲指定、パスワード付きPDF、OCRなどを追加する場合は、UIを大きく壊さずサービス層を拡張します。

## レイヤー

```text
Views/
  MainWindow.axaml       日本語UI
  MainWindow.axaml.cs    画面操作、ファイル選択、進捗表示

Models/
  PdfToImageRequest      PDF→画像の入力条件
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
  AppFontResolver        OS上のフォントをPDF出力へ解決
  FileNameHelper         出力ファイル名の衝突回避
  PageRangeParser        ページ範囲指定の解釈
  FriendlyErrorFormatter 専門用語を避けたエラー文言
```

## PDFレンダリング

PDFtoImageはPDFiumを利用します。PDFium呼び出しは並列処理向きではないため、複数PDFも1件ずつ処理します。壊れたPDFが混ざった場合は、そのファイルのエラーを記録して次のファイルへ進みます。

## 画像PDF化

PDFsharpで新規PDFを作り、選択された画像を順番に1ページずつ配置します。A4縦、A4横、画像サイズに合わせる、余白あり/なしをサービス側で扱います。

## PDF編集

PDF編集はPDFsharpを中心に処理します。結合、1ページずつ分割、指定ページ削除、選択ページのみの出力、文字/テキスト追加、図形追加を `PdfDocumentService` に集約しています。

選択ページ出力では、ページプレビューのチェックまたは `1,3,5-7` 形式で対象を指定します。選択ページを元PDF内の昇順で1つのPDFへ追加するため、非連続ページも連続したページ構成で出力されます。

文字/テキスト追加では、画面上の編集状態を `PdfSimpleEditRequest` にまとめ、書き出し時にPDF座標へ変換して反映します。テキストボックスは最前面レイヤーとして扱い、図形は四角形、角丸四角形、丸、線を扱います。

フォントはアプリに同梱せず、OS上で利用可能なフォントを参照します。配布物にフォントファイルを含めないことで、フォントライセンス上のリスクを抑えています。

ページプレビューはPDFium/PDFtoImageでレンダリングします。編集画面ではプレビュー画質を高め、ズーム倍率をUI操作、Ctrl+マウスホイール、トラックパッドのピンチ操作で変更できます。

## 追加しやすい機能

- PDF→画像のページ範囲指定: `PdfToImageRequest` に開始/終了ページを追加
- パスワード付きPDF: UIにパスワード欄を追加し、PDFtoImageへ渡す
- 画質指定: JPEG品質をUIから指定
- 出力名ルール変更: `FileNameHelper` を拡張
- OCR: 別サービスを追加。ただし完全オフラインOCRはモデル同梱とライセンス確認が必要

## 制限

- PDFiumは1プロセス内での同時レンダリングを避けています。
- OCRやPDF内テキスト抽出は実装していません。
- 既存PDF内の文字を直接編集する機能は実装していません。文字や図形をPDF上に追加する方式です。
- パスワード付きPDFの入力欄は現時点ではありません。
- 単体exe方式ではネイティブライブラリを一時フォルダに展開します。
