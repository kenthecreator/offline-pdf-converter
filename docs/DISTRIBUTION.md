# Offline PDF Converter (v5.5) の配布

公開先は [GitHub Releases](https://github.com/kenthecreator/offline-pdf-converter/releases/latest) と [公式ダウンロードページ](https://kenthecreator.github.io/offline-pdf-converter/) です。

| 対応端末 | 公開ZIP | 同梱アプリ |
| --- | --- | --- |
| macOS Apple Silicon | Offline.PDF.Converter.v5.5.-macOS-arm64.zip | Offline PDF Converter (v5.5).app |
| Windows 10（1903以降）／11 x64 | Offline.PDF.Converter.v5.5.-Windows-x64.zip | Offline PDF Converter (v5.5).exe |

両版に.NET、OCRエンジンと認識モデルを同梱します。Mac版はad-hoc署名、未公証です。Windows版は未署名の単体exeで、必要なVisual C++ DLLを内蔵します。初回は内蔵ファイルの展開に一時フォルダーへの書き込みが必要です。

## 各OSでの作成・確認

```sh
python3 scripts/build-paddle-edition.py --target mac
python3 scripts/verify-v5.5-release.py --archives dist/paddle-edition --target macOS-arm64
```

Windowsでの作成は [Windows用単体exeのビルド手順](BUILD_WINDOWS.md) を参照してください。

`Verify and prepare v5.5` はWindows Server 2022とmacOSで各配布ZIPを作成し、Windows Server 2025で同じWindows ZIPを再検証します。両Windows環境では対象exeのネットワークを遮断します。

## GitHub Releasesと公式ページの切り替え

1. 公開候補のコミットを `main` に反映した後、`Verify and prepare v5.5` を `main` で `prepare_draft` を有効にして実行します。
2. 両OSのビルド・GUI検証が成功すると、検証結果とZIPを含む `v5.5.0` のリリース下書きができます。
3. 管理者のGitHub CLI認証で `python3 scripts/publish-v5.5.py` を実行すると、下書きのZIP・SHA-256・検証結果と、CIで検証したコミットのダウンロードページを読み取りで照合します。
4. 公開を実行する際は、同じスクリプトに `--publish` を付けます。v5.5を公開してタグと最新版を確認した後、`gh-pages` に新しいアイコンと `index.html` を反映します。Pagesのデプロイ後に、アイコン、2つのZIPとSHA-256、使い方リンクを確認します。
5. 作業フォルダのREADMEと公開版の記録も、実際に確認したタグと配布パスに更新します。
