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
