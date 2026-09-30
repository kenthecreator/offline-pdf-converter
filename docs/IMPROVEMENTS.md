# v3.2.0の使い方と検証

## 追加した機能

- PDF・画像・TXTは同じ保存先の一時ファイルに書き込み、完成後だけ正式名で確定します。既存ファイルは置き換えず、連番で保存します。失敗・中止時には作業用ファイルを削除します。
- PDF→画像、PDF分割、OCRの一括処理は、入力ごとに成功・失敗・中止・未処理を表示します。失敗だけ、または中止・未処理だけを再実行できます。再実行はファイルの先頭から行い、保存済みのページは別名で残します。
- 処理中に「処理を中止」を表示します。PDFの処理は現在のページ処理が戻った段階で止まります。OCRの外部プロセスは終了させます。
- PDF→画像の設定はv3.1.0の「解像度」に戻しました。「普通（200 dpi）」「高画質（300 dpi）」「超高画質（600 dpi）」をスライダーで選択します。初期値は普通です。
- 独立した「OCR処理」カテゴリーで、スキャンPDFをTXTへ文字認識できます。原本プレビューと認識設定を同じ画面に表示します。
- メイン画面から既存の編集処理を `MainWindow.Editor.cs` に分離し、一括結果・OCRも別ファイルにしました。今回は段階的な整理で、画面と状態を完全に分離するMVVMへの全面移行は行っていません。

結合や画像→PDFは複数入力で1つの成果物を作るため、従来の一括結果表示を使います。失敗した入力だけで別の結合PDFを作るような再実行は行いません。

## v4.0の内蔵OCR

Mac・Windowsの両版でPaddleOCRを内蔵し、検索・選択・コピーできるPDFを作成します。「OCR処理」でPDF、4種類の言語設定、対象ページ、保存先を選び「開始」を押します。処理前・処理後を比較し、クリックして文字を確認できます。TXT出力は廃止しました。

Tesseract・Python・GPUは不要です。WindowsはVisual C++ v14（x64）ランタイムが必要です。ネット接続や外部送信は行いません。モデルはハッシュ確認後に一時領域へ展開します。詳細は [検索可能PDF](SEARCHABLE_PDF.md) を参照してください。

## 自動テスト（旧Tesseract実装の回帰検証を含む）

```sh
dotnet run --project tests/RegressionTests -c Release -p:PaddleOcrEdition=false
```

標準テストは実際にPDFを生成し、保存失敗・取消・並行保存、ページ指定、結合、削除、抽出、パスワード付きPDFの分割、日本語文字抽出、画像レンダリングを検証します。破損PDF、誤ったパスワード、画像からのPDF作成、失敗した画像の扱い、OCR子プロセスの中止・異常終了も確認します。失敗時には非ゼロの終了コードを返します。

実エンジンによる日本語OCRテストは、次の環境変数を設定すると追加実行します。

```sh
OCR_TEST_ENGINE=/opt/homebrew/bin/tesseract \
OCR_TEST_DATA="$PWD/src/OfflinePDFConverter/ocr/tessdata" \
dotnet run --project tests/RegressionTests -c Release -p:PaddleOcrEdition=false
```

Windows/MacのCI定義を追加しています。ローカルで確認したOSはmacOS arm64です。Windows実機とCIの実行結果は別途確認が必要です。

## 保存処理の補足

Windowsでは上書きしないファイル移動を使います。macOSでは排他的なファイル名変更を使います。それ以外のUnixではハードリンクによる確定を使います。保存先がその操作に対応していない場合は失敗として扱い、元ファイルを上書きする方式へは切り替えません。

一時ファイルを完成名へ確定する仕組みは、停電時のディスクへの永続化まで保証するものではありません。

## 単体exeの検証（Windows）

```powershell
pwsh -File scripts/verify-windows-single-exe.ps1 -ExePath artifacts/windows-v4.0/OfflinePDFConverter.PaddleEdition.exe
```

検証スクリプトは空のフォルダへexeだけをコピーし、PATHをWindows標準の場所だけに制限したうえで、アプリ内の日本語画像 → PDF → 画像レンダリング → 内蔵OCRを実行します。実際の配布exe自身が検証するため、開発機にインストール済みのTesseractや同梱し忘れたファイルには依存しません。スクリプト自体はネットワークを遮断しません。ネットワーク無効のWindows Sandboxで実行すると、初回からオフラインで動くことも確認できます。
