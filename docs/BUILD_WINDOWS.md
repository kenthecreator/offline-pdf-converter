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
5. `THIRD_PARTY_LICENSES.md` を配布物に同梱する。
