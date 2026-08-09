# Offline PDF Converter

完全オフライン動作を前提にした、Windows x64向けのPDF/画像変換・PDF編集デスクトップアプリです。Python、Poppler、Adobe製品、外部変換サービスを使わず、発行済みの `.exe` をダブルクリックして利用できます。

## 技術構成の提案

「完全オフライン」「追加ランタイムの事前インストール不要」「Adobe非依存」「PDF各ページの画像化」を満たすため、次の構成を採用しています。

| 領域 | 採用技術 | 理由 |
| --- | --- | --- |
| UI | .NET 8 / C# / Avalonia UI | クロスプラットフォーム開発とWindows x64の自己完結 `.exe` 発行に向いている。WPF風のXAMLで保守しやすい。 |
| PDF → 画像 | PDFtoImage + PDFium + SkiaSharp | Adobe非依存。PDFiumで各ページをレンダリングし、PNG/JPEGへ保存できる。 |
| 画像 → PDF | PDFsharp | MITライセンス。JPEG/PNGをPDFページへ配置する用途に向いている。 |
| 配布 | self-contained single-file publish | .NET Runtimeや外部DLLを別途入れずに起動できる。 |

WPF/WinUIはWindows専用UIとして有力ですが、クロスプラットフォーム開発とWindows用単体exe発行の扱いやすさを重視してAvaloniaを選んでいます。MuPDF系はAGPLまたは商用ライセンスの検討が必要になりやすいため、この実装では採用していません。

## 主な機能

- PDFをページごとにPNG/JPEGへ変換
- 普通 / 高画質 / 超高画質の画質選択
- 複数ページPDF対応
- 複数PDFの一括変換
- JPEG / PNG画像を1つのPDFに結合
- A4縦、A4横、画像サイズに合わせる
- 余白あり/なし
- 複数PDFを1つのPDFに結合
- 複数ページPDFを1ページずつ別PDFに分割
- 複数ページPDFから指定ページを削除して新しいPDFを作成
- 複数ページPDFから選択ページだけを元の順番で1つのPDFとして出力
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
- ライトモード/ダークモード切り替え
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
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishTrimmed=false \
  -o dist/win-x64-single-offline-pdf-converter
```

出力先:

```text
dist/win-x64-single-offline-pdf-converter/Offline PDF Converter.exe
```

## ライセンス

使用ライブラリとライセンスは [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) にまとめています。

アプリアイコン画像（`src/OfflinePDFConverter/Assets/AppIcon.png` および `AppIcon.ico`）の著作権は GitHub ユーザー `kenthecreator` に帰属します。ソースコード本体のMITライセンスとは別の権利表示として扱ってください。
