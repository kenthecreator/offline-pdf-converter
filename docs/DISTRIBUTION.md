# Offline PDF Converter (v4.4) の配布

最新版は [GitHub Releases](https://github.com/kenthecreator/offline-pdf-converter/releases/latest) と [公式ダウンロードページ](https://kenthecreator.github.io/offline-pdf-converter/) で配布します。

| 対応端末 | 配布ZIP | アプリ |
| --- | --- | --- |
| macOS Apple Silicon | Offline PDF Converter (v4.4)-macOS-arm64.zip | Offline PDF Converter (v4.4).app |
| Windows 10（1903以降）／11 x64 | Offline PDF Converter (v4.4)-Windows-x64.zip | Offline PDF Converter (v4.4).exe |

両版にOCRエンジン・認識モデル・.NET実行基盤を内蔵します。Tesseract、Python、GPUは不要です。v4.4のWindows版はVisual C++の必要なDLLもexe内に含めます。公開済みv4.0のZIPは変更されていません。配布物にはREADME、マニュアル、リリースノート、OCR説明、第三者ライセンスを同梱します。ZIPのSHA-256も公開します。

Windowsのアプリは単体exeです。ネイティブ部品・モデルを一時領域に自動展開するため、一時フォルダへの書き込みが必要です。MacはZIPを展開して.appを起動してください。ad-hoc署名で、Appleの公証は未実施です。

## ローカルでの作成・検証

```sh
python3 scripts/build-paddle-edition.py --target both
codesign --verify --deep --strict "dist/paddle-edition/macos-arm64/Offline PDF Converter (v4.4).app"
python3 scripts/verify-published-exe.py artifacts/paddle-edition/publish-windows
```

ZIPを別フォルダへ展開してからもMacの署名検証を行います。Windows実機では `scripts/verify-windows-single-exe.ps1` にexeのパスを渡して内蔵OCRの自己検証を行います。macOS上でのWindows向け発行成功だけではWindowsの実機動作を保証しません。
