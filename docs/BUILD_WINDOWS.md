# v3.2.0 Windows単体exeの作成

通常のビルドには.NET 8 SDKを使用します。内蔵OCRの構築済みアーカイブはリポジトリに含まれ、アプリ利用者が何かをインストールする必要はありません。

```sh
dotnet publish src/OfflinePDFConverter/OfflinePDFConverter.csproj -p:PublishProfile=WindowsSingleFile -o artifacts/windows-v3.2.0
python3 scripts/verify-published-exe.py artifacts/windows-v3.2.0
```

出力フォルダの配布ファイルは `OfflinePDFConverter.exe` 一つです。ファイル名は配布時に変更できます。`WindowsSingleFile` プロファイルは自己完結、ネイティブライブラリ内包、不要なデバッグ情報の除外を行い、exe以外が残るとエラーにします。

Windows上では以下の実行検証も行います。

```powershell
pwsh -File scripts/verify-windows-single-exe.ps1 -ExePath artifacts/windows-v3.2.0/OfflinePDFConverter.exe
```

## OCR本体を再構築する場合のみ

開発環境にPython 3、CMake、Ninja、MinGW-w64のx86_64クロスコンパイラを用意します。今回の構築環境はmacOS arm64、MinGW-w64 14.0.0 / GCC 16.2.0です。

```sh
python3 scripts/prepare-ocr-sources.py
bash scripts/build-windows-ocr.sh
python3 scripts/package-windows-ocr.py
```

最初のスクリプトだけが開発用ソースを取得します。URLとSHA-256は `vendor/ocr-source-manifest.json` で固定されています。このスクリプトは配布exeから呼ばれません。構築時にWindows UTF-8マニフェストを追加し、静的リンク、PNG入力、HTTP機能なしで構築します。最後のスクリプトは依存先がWindows標準DLLだけであることを検査し、認識データ、ライセンス、由来情報を内蔵アーカイブにまとめます。

## 旧版の開発手順（参考）

# Windows用ビルド手順

この手順は開発用のmacOSまたはWindows環境で実行します。

## 前提

- .NET 8 SDKを開発環境にインストール
- 初回の `dotnet restore` 時だけインターネット接続が必要

## 依存パッケージの復元

```bash
cd offline-pdf-converter
dotnet restore
```

## 開発端末での動作確認

対応する開発環境ではUIの起動確認もできます。

```bash
dotnet run --project "src/OfflinePDFConverter/OfflinePDFConverter.csproj"
```

## Windows x64単体exeの発行

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

出力ファイル:

```text
dist/win-x64-single-offline-pdf-converter/Offline PDF Converter.exe
```

この `.exe` は.NET Runtimeと必要なネイティブライブラリを含む自己完結形式です。起動時にネイティブライブラリが一時フォルダへ展開されるため、利用環境で一時フォルダへの書き込みが禁止されている場合は、単体exeではなくフォルダ配布方式に切り替えてください。

## フォルダ配布方式の予備手順

厳しいセキュリティ設定で単体exeが起動しない場合の代替です。

```bash
dotnet publish "src/OfflinePDFConverter/OfflinePDFConverter.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false
```

この場合は `publish` フォルダ全体を配布します。通常は単体exe方式を優先してください。

## 推奨確認

1. インターネットを切ったWindows環境で起動する。
2. PDFをPNG/JPEGへ変換できる。
3. 複数ページPDFで `元PDF名_page001.png` のように出力される。
4. 複数画像を1つのPDFへ変換できる。
5. PDFの結合、1ページずつ分割、選択ページ削除、選択ページのみの出力ができる。
6. PDF上に文字・テキスト、図形を追加して書き出せる。
7. `THIRD_PARTY_LICENSES.md` を配布物に同梱する。
