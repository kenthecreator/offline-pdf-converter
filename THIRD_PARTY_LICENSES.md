# 使用ライブラリとライセンス一覧

このアプリはAdobe AcrobatなどAdobe製品には依存しません。PDFレンダリングにはPDFium系ライブラリを使用します。

## アプリアイコン

| 対象 | 権利者 | 備考 |
| --- | --- | --- |
| `src/OfflinePDFConverter/Assets/AppIcon.png` / `AppIcon.ico` | GitHub user `kenthecreator` | アプリアイコン画像の著作権は `kenthecreator` に帰属します。 |

## 直接利用している主なパッケージ

| ライブラリ | バージョン | 用途 | ライセンス | URL |
| --- | ---: | --- | --- | --- |
| Avalonia | 11.3.12 | デスクトップUI | MIT | https://www.nuget.org/packages/Avalonia |
| Avalonia.Desktop | 11.3.12 | デスクトップ実行基盤 | MIT | https://www.nuget.org/packages/Avalonia.Desktop |
| Avalonia.Themes.Fluent | 11.3.12 | UIテーマ | MIT | https://www.nuget.org/packages/Avalonia.Themes.Fluent |
| Avalonia.Fonts.Inter | 11.3.12 | UIフォント | MIT / Inter font: SIL Open Font License 1.1 | https://www.nuget.org/packages/Avalonia.Fonts.Inter |
| PDFtoImage | 5.2.1 | PDFを画像へ変換 | MIT | https://www.nuget.org/packages/PDFtoImage |
| PDFsharp | 6.2.4 | 画像からPDFを作成 | MIT | https://www.nuget.org/packages/PDFsharp |
| PdfPig | 0.1.16 | PDFの文字位置を読み取る | Apache-2.0 | https://www.nuget.org/packages/PdfPig |

## 主な推移依存

| ライブラリ | 用途 | ライセンス | 備考 |
| --- | --- | --- | --- |
| PDFium / bblanchon.PDFium.* | PDFレンダリング用ネイティブライブラリ | Apache-2.0 package / PDFium BSD-3-Clause系 | PDFtoImage経由で同梱されます。PDFium本体とサードパーティ通知の確認が必要です。 |
| SkiaSharp | 画像エンコード/描画 | MIT | PDFtoImage経由で利用されます。 |
| Skia native components | 2D描画エンジン | BSD-style and third-party notices | SkiaSharpに含まれるネイティブコンポーネントです。 |
| Microsoft .NET Runtime | 自己完結exe実行基盤 | MIT and Microsoft notices | self-contained発行で含まれます。 |

## ライセンス上の注意

- MIT、Apache-2.0、BSD-3-Clause系は permissive license ですが、著作権表示とライセンス文の保持が必要です。
- PDFiumやSkiaには追加のサードパーティコンポーネントが含まれるため、最終配布時はNuGetパッケージ内のライセンス/noticeも確認してください。
- MuPDF系はAGPLまたは商用ライセンスの検討が必要になりやすいため、この実装では採用していません。
- iText、Ghostscript、Poppler、Adobe製品には依存していません。

## 配布時の推奨

配布物には、この `THIRD_PARTY_LICENSES.md` を同梱してください。厳密な監査が必要な場合は、発行後の `publish` フォルダに含まれる依存パッケージのライセンス文もあわせて保存してください。

## OCR認識データ

### v4.0の内蔵OCR部品

v4.0のMac・Windows配布物は `PaddleOcrEdition=true` でビルドし、以下の部品を使用します。

| 部品 | 固定版・構成 | ライセンス |
| --- | --- | --- |
| PaddleOCRモデル | PP-OCRv6 small検出・混在認識、PP-OCRv4日本語・英語認識、PP-OCRv2角度分類。RapidOCR v3.9.2のONNX配布物 | Apache-2.0 |
| RapidOcrNet | 4.2.0 / commit 708cae2fcb88720e1d891a81b5ee3e8b2bcc139e | Apache-2.0 |
| Microsoft.ML.OnnxRuntime / Managed | 1.29.0 | MITおよび同梱ThirdPartyNotices |
| Clipper2 | 2.0.0 | Boost Software License 1.0 |
| PContourのC#移植（RapidOcrNet内） | Lingdong Huang / BobLd | MIT |

取得URL、SHA-256、文字辞書の由来を`src/OfflinePDFConverter/ocr/paddle/manifest.json`に記録します。上記ライセンス全文と通知をアプリに埋め込み、OCR画面の「使用ライセンス」で表示します。SkiaSharp、PDFium、.NETなど共通部品には上記の既存のライセンスが適用されます。

日本語（jpn / jpn_vert）と英語（eng）の認識データは https://github.com/tesseract-ocr/tessdata_fast から取得しています。Apache License 2.0。ライセンス全文と取得URL・SHA-256は `src/OfflinePDFConverter/ocr/tessdata/` に同梱しています。v3.2.0ではTesseract 5.5.3、Leptonica 1.87.0、libpng 1.6.58、zlib 1.3.2を静的リンクしたWindows x64エンジンを内蔵しています。各ライセンス、MinGWのライセンス、GCC Runtime Library Exception 3.1とGPLv3本文は内蔵OCRアーカイブ内のlicenses/とvendor/ocr-licenses/に含めています。外部のTesseractインストーラは配布しません。
