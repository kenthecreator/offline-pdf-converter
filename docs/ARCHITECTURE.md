# 設計メモ

## 方針

UI、変換処理、データモデルを分けています。将来的にページ範囲指定、パスワード付きPDF、PDF結合、OCRなどを追加する場合は、UIを大きく壊さずサービス層を拡張します。

## レイヤー

```text
Views/
  MainWindow.axaml       日本語UI
  MainWindow.axaml.cs    画面操作、ファイル選択、進捗表示

Models/
  PdfToImageRequest      PDF→画像の入力条件
  ImageToPdfRequest      画像→PDFの入力条件
  ConversionProgress     進捗通知
  ConversionResult       変換結果

Services/
  PdfToImageService      PDFium/PDFtoImageを使ったPDFレンダリング
  ImageToPdfService      PDFsharpを使ったPDF作成
  FriendlyErrorFormatter 専門用語を避けたエラー文言
```

## PDFレンダリング

PDFtoImageはPDFiumを利用します。PDFium呼び出しは並列処理向きではないため、複数PDFも1件ずつ処理します。壊れたPDFが混ざった場合は、そのファイルのエラーを記録して次のファイルへ進みます。

## 画像PDF化

PDFsharpで新規PDFを作り、選択された画像を順番に1ページずつ配置します。A4縦、A4横、画像サイズに合わせる、余白あり/なしをサービス側で扱います。

## 追加しやすい機能

- ページ範囲指定: `PdfToImageRequest` に開始/終了ページを追加
- パスワード付きPDF: UIにパスワード欄を追加し、PDFtoImageへ渡す
- 画質指定: JPEG品質をUIから指定
- 出力名ルール変更: `FileNameHelper` を拡張
- OCR: 別サービスを追加。ただし完全オフラインOCRはモデル同梱とライセンス確認が必要

## 制限

- PDFiumは1プロセス内での同時レンダリングを避けています。
- OCRやPDF内テキスト抽出は実装していません。
- パスワード付きPDFの入力欄は現時点ではありません。
- 単体exe方式ではネイティブライブラリを一時フォルダに展開します。
