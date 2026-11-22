# Offline PDF Converter (v5.1) の配布

公開先は [GitHub Releases](https://github.com/kenthecreator/offline-pdf-converter/releases/latest) と [公式ダウンロードページ](https://kenthecreator.github.io/offline-pdf-converter/) です。

| 対応端末 | 公開ZIP | 同梱アプリ |
| --- | --- | --- |
| macOS Apple Silicon | Offline.PDF.Converter.v5.1.-macOS-arm64.zip | Offline PDF Converter (v5.1).app |
| Windows 10（1903以降）／11 x64 | Offline.PDF.Converter.v5.1.-Windows-x64.zip | Offline PDF Converter (v5.1).exe |

両版に.NET、OCRエンジンと認識モデル、LibreOfficeKitを同梱します。Mac版はad-hoc署名、未公証です。Windows版は未署名の単体exeで、必要なVisual C++ DLLを内蔵します。初回は内蔵ファイルの展開に一時フォルダーへの書き込みが必要です。

## 各OSでの作成・確認

```sh
python3 scripts/prepare-office-engine.py --target mac --output artifacts/office-engine
python3 scripts/build-paddle-edition.py --target mac --office-macos-app artifacts/office-engine/LibreOffice.app
python3 scripts/verify-v5.1-release.py --archives dist/paddle-edition --target macOS-arm64
```

Windowsでの作成は [Windows用単体exeのビルド手順](BUILD_WINDOWS.md) を参照してください。両OSとも公式LibreOffice 26.8.3.1（`861effc5289d539f15c4c18d1ba8579ffd78728b`）を使います。インストーラーのSHA-256を固定し、展開時に同一のビルドIDを検証します。

`Verify and prepare v5.1` はWindows Server 2022とmacOSで各配布ZIPを作成し、Windows Server 2025で同じWindows ZIPを再検証します。両Windows環境では対象exeのネットワークを遮断します。3環境のOffice出力について、ページ数・日本語の文字と順序・用紙寸法・文字と画像の位置を比較します。別途、Windows 10／11のクリーン環境での動作とMicrosoft OfficeのPDF出力とのレイアウトを確認します。

## GitHub Releasesと公式ページの切り替え

1. `codex/release-v5.1` のCI結果を確認します。公開候補のコミットを `main` に反映した後、`Verify and prepare v5.1` を `main` で `prepare_draft` を有効にして実行します。
2. 両OSのビルド・GUI・Office出力比較が成功すると、検証結果とZIPを含む `v5.1.0` のリリース下書きができます。
3. 管理者のGitHub CLI認証で `python3 scripts/publish-v5.1.py` を実行すると、下書きのZIP・SHA-256・検証結果と、CIで検証したコミットのダウンロードページを読み取りで照合します。
4. 公開を実行する際は、同じスクリプトに `--publish` を付けます。v5.1を公開してタグと最新版を確認した後、`gh-pages` にv5.1のアイコンと `index.html` を反映します。Pagesのデプロイ後に、アイコン、2つのZIPとSHA-256、使い方リンクを確認します。
5. 作業フォルダのREADMEと公開版の記録も、実際に確認したタグと配布パスに更新します。

## Office文書の組み込み変換

Word（`.docx`）、Excel（`.xlsx`）、PowerPoint（`.pptx`）を元ファイルと同じフォルダーに透かしなしで保存します。原稿に指定されたフォントを利用できない場合は代替フォントが選ばれ、改行や配置に差が出ることがあります。日本語用の代替フォントとしてNoto Sans JPを同梱します。

[LibreOfficeKit](https://docs.libreoffice.org/libreofficekit.html) · [LibreOfficeのライセンス](https://www.libreoffice.org/about-us/licenses/)
