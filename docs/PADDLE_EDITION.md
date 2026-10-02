# Offline PDF Converter (v4.3) — 内蔵OCR

v4.3のMac・Windows配布版は、PaddleOCRを内蔵した検索可能PDFの生成に対応します。

## v4.3の使い方

アプリ名は「Offline PDF Converter (v4.3)」です。「OCR処理」でPDFを追加し、「PDFの言語」、認識対象ページ、PDF保存先を選び、「開始」を押します。検索可能PDFへ出力し、PDF編集のTXT出力は廃止しました。

- 日本語（横書き）: 日本語用PP-OCRv4認識モデル、横書きの読み順。
- 日本語（縦書き）: 日本語用PP-OCRv4認識モデル、右の列から読む順序と縦方向の文字レイヤー。
- 英語: 英語用PP-OCRv4認識モデル。
- 日本語・英語 混在: PP-OCRv6 small多言語認識モデル。

検出は全項目でPP-OCRv6 smallを使用します。専用の辞書もモデルとともに埋め込み、認識中のダウンロードや外部送信はありません。混在は横書き向けです。

[検索可能PDFの仕様・検証・制限](SEARCHABLE_PDF.md)を参照してください。

Mac版はApple Silicon用、ad-hoc署名で未公証です。Tesseract・Python・GPUは不要です。v4.3のWindows x64版は.NET・認識モデル・Visual C++の必要なDLLをexeに内蔵します。公開済みv4.0ではVisual C++ v14（x64）ランタイムが別途必要です。v4.3の公開ゲートはWindows Server 2022/2025で同じexeの通信遮断下の主要機能・実ウィンドウ検証を実行します。Windows 10/11の実機、Visual C++未導入VM、PC全体の通信切断、OSのファイル選択ダイアログと全マウス操作の網羅は未確認です。結果JSONはリリースに添付します。

## 構成

- 検出・認識: PP-OCRv6 small（前回比較と同一のSHA-256）。角度分類: PP-OCRv2。
- .NET実行基盤: RapidOcrNet 4.2.0、Microsoft ONNX Runtime 1.29.0。CPU、intra-op 4 / inter-op 1スレッド。
- v6用前処理: 最大辺2000px、短辺の下限736px、追加外周余白なし。縦書き指定では検出した座標を使って右の列から読み順を並べます。横書きでは、明確な縦の余白で分かれた二段組を左の段落から並べます。複雑な表や見出しをまたぐ段組は構造解析の対象外です。
- 全モデル、文字辞書、ライセンス全文をアプリに埋め込みます。OCR画面の「使用ライセンス」で通知を表示できます。
- 固定ファイル・ハッシュ: `src/OfflinePDFConverter/ocr/paddle/manifest.json`。

## 旧版の2026-09-30の検証結果（TXT出力・旧言語設定）

以下の精度・速度は旧版の結果で、v4.3の言語別モデルやPDF出力の精度・速度を表すものではありません。

アプリの画像→PDF機能で評価画像9枚からPDFを作成し、実際の`OfflineOcrService`を通して、PDF画像化・認識・TXT保存まで各画像3回実行しました。比較元は同じアプリのPDF処理経路を使うTesseract 5.5.3＋同梱tessdata_fastです。以前の資料のうち「縦書き」「二段組」が実際には横書きになっていた生成設定を修正しました。今回はその2枚を描画して確認し、正しい配置で測定しています。他の7枚は以前とSHA-256が一致します。旧測定の「縦書き・二段組」評価は無効です。

| 方式 | 文字誤り率 | 誤り数 | 初回を除くPDF→TXT平均秒／ページ | 白紙の文字誤認識 |
| --- | ---: | ---: | ---: | ---: |
| PaddleOCR Edition | 0% | 0 / 1,344 | 0.8619 | なし |
| 現在のTesseract方式 | 4.8363% | 65 / 1,344 | 0.8686 | なし |

初回の横書きPDF→TXTはPaddleOCRが1.2713秒、Tesseractが1.0884秒でした。PaddleOCRはモデル読み込み後に再利用するため、連続処理の時間と初回の時間を分けています。平均差は約0.8%であり、安定した速度改善や別PCでの優位性を示す値とは扱いません。以前のPNG認識だけの測定とは処理範囲が異なり、直接比較できません。

Tesseractの65文字分の編集距離のうち、二段組の読み順が57文字分を占めます。その他の文字誤認識は8文字です。PaddleOCR Editionには単純な二段組の読み順調整を追加しています。今回の値はモデル単体でなく、アプリの出力結果の比較です。

横書き2書体、劣化、小さい文字、日英混在、縦書き、二段組、英語、白紙を評価しました。精度はNFKC正規化後、空白・改行を除いた文字誤り率です。反復で異なる結果がある場合は最大の誤り数を採用します。小規模な印刷文書の評価であり、一般のPDF、手書き、ルビ、複雑な表で同じ精度を保証しません。

PaddleOCR固有の検証は8件成功・0件失敗（モデル内蔵、PDF→TXT、認識途中の中止と再実行、タイムアウト後の復帰、不正画像からの復帰、ページ指定と既存ファイル保護、暗号化PDFを含む）。通常版の既存回帰テストも35件成功・0件失敗です。最終配布ファイルの検証記録は`docs/paddle-edition-results/`へ保存しています。

配布用Macアプリを画面から操作し、修正した資料の縦書き1ページと横書き・二段組・白紙8ページを2件のTXTに保存しました。1,344文字の誤りは0で、二段組の段落順も一致しました。配布用Macアプリの`--verify-offline`も、TesseractやPythonをPATHから除いた環境で成功しました。

## 再作成

開発環境に.NET 8 SDKとPython 3を用意します。モデルとNuGet取得には通信が必要ですが、利用者のPDFは送信しません。大きな生成型OCRやPython推論環境は取得しません。

```sh
python3 scripts/prepare-paddle-ocr.py
python3 scripts/build-paddle-edition.py --target both
```

Macパッケージの作成にはmacOSのsips、iconutil、codesignを使います。Windows上で発行する場合は`--target windows`を指定します。通常版とEditionのビルド・復元先を分離しています。

検証:

```sh
dotnet build tests/PaddleEditionTests -c Release -p:PaddleOcrEdition=true
dotnet artifacts/paddle-edition/bin/PaddleEditionTests/Release/net8.0/PaddleEditionTests.dll "$PWD" "$PWD/artifacts/paddle-edition/new-verification"
```

先に`scripts/ocr-benchmark-fixtures.py`で評価画像を作成します（開発用PillowとMacのシステムフォントが必要）。認識の再現には正解テキストをOCRへ渡しません。配布アプリは`--verify-offline <report.json>`で主要機能と異常入力・取消を含む自己検証を行えます。`--verify-gui <report.json>`は実ウィンドウの自動検証です。通常起動ではこれらを実行しません。

## v4.3の小文字OCRとビルド用フォント準備

ページ番号だけが文字のページもOCR対象とします。通常のA4は300dpi、検出前長辺上限は4096pxです。文字層にはNoto Sans Japaneseを内蔵します。ビルドの前に、固定版のフォント生成依存を準備します。これは開発用の手順で、利用者には不要です。

```sh
python -m pip install fonttools==4.61.1
python scripts/prepare-ocr-font.py
```

フォントは固定コミットとSHA-256で確認し、exe／app内に埋め込みます。利用時の追加取得はありません。
