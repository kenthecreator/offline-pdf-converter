# v4.4 操作部品の Liquid Glass 風描画試験

## PDF ↔ 画像の解像度バー

PDF から画像に変換するときの解像度バーでは、土台、選択済みの範囲、つまみに進捗バーと同じ光学シェーダーを使う。目盛り、200／300／600 dpi のラベル、入力処理、移動と幅のアニメーションは既存の部品を使う。

Mac Apple Silicon 用の更新版は `dist/glass-test/Offline PDF Converter (v4.4) Glass Test 5.app`。既定オンと強制フォールバックの GUI 検証各11項目、オフライン自己検証15項目が通過。実ウィンドウでクリックの200 dpi、ドラッグの600 dpi、左矢印の300 dpiを確認した。Light／Dark の画面と検証ログは `artifacts/liquid-glass-verification/dpi-glass/`。Windows 実行は未検証。

## 下部の進捗バー

下部の進捗バーでは、トラックと進捗を示す塗りに、操作ボタンと同じ光学シェーダーで縁の屈折・反射を描く。描画層は既存の Fluent テンプレートのトラックとインジケーターに挿入する。表示幅、進捗値、クリップ、処理中の更新は既存の ProgressBar が担当する。

Finder 起動用の更新版は `dist/glass-test/Offline PDF Converter (v4.4) Glass Test 4.app`。比較画像とログは `artifacts/liquid-glass-verification/progress-glass/`。

Mac Apple Silicon で既定オンと強制フォールバックの GUI 検証各10項目、オフライン自己検証15項目が通過。進捗0%・42%・100%に対応するインジケーター幅を確認し、42%の状態を Light／Dark で撮影した。Windows 実行は未検証。

## 選択ピルと通常の操作ボタンの描画を統一

「追加」「削除」「すべて削除」「上へ」「下へ」「選択」などのラベル付き操作ボタンをピル型にそろえた。選択ピルと同じ SkSL を共有し、縁に屈折、明暗、反射を描く。通常の操作ボタンではラベルのある中央を透過させ、既存の塗り、ラベル、押下・無効状態を組み合わせて描画する。屈折の入力は既存の塗りと周囲の背景色。

Mac Apple Silicon 用の更新版は `dist/glass-test/Offline PDF Converter (v4.4) Glass Test 3.app`。Finder から起動すると有効になる。比較画像と検証ログは `artifacts/liquid-glass-verification/unified-pill/`。

GUI 検証で指定の6ボタンすべての Skia 描画を確認した。既定オンの GUI 9項目、通信遮断下のオフライン自己検証15項目、シェーダーのコンパイル失敗を模擬した GUI 9項目が通過。実ウィンドウで Light／Dark を切り替えて撮影した。Windows での実行は未検証。

## ピル型以外のボタンとライトモードの調整

ボタンの縁は、角丸の四角形や小さい円形を含め、元の輪郭に沿って描く。メイン画面、編集画面、ダイアログ、OCR のサムネイルを開くボタンに同じ描画を適用した。ドロップダウンとチェックボックスには既存のテンプレートの装飾層を使い、独立したラジオボタンでは選択マークを囲む縁を調整した。

ライトモードは明るい面でも縁が読めるよう、外側の暗い細線と内側の白い反射に明確な差を付けた。セレクターのピルは元の背景をより強く透かし、屈折量と縁の反射・影を調整した。描画に使う背景は既存の配色と形状。

Mac の Finder 起動用テスト版は `dist/glass-test/Offline PDF Converter (v4.4) Glass Test 2.app`。このアプリは引数なしでガラス表現が有効になる。通常のソースビルドは既定オフ。比較画像と検証ログは `artifacts/liquid-glass-verification/all-buttons/`。

Mac Apple Silicon の配布形態で署名、既定オンの GUI 9項目とオフライン自己検証15項目、強制フォールバックの GUI 9項目が通過。GUI 検証では四角いドロップダウンと OCR プレビューのボタンの適用も確認した。起動引数なしのアプリで Light／Dark を切り替え、実画面を保存した。Windows 実行は未検証。

## ピル型ボタン全体への試験適用

モード、テーマ、変換方向、PDF プレビュー、OCR プレビューの5つのセレクターでは、選択ピルの縁を通して元のトラックが屈折する。通常の横長ピル型ボタンでは、既存の塗りと内容に沿って無彩色の内縁反射と影を描く。メイン画面と別ウィンドウにある「開始」「追加」「完了」などが対象。形状は描画時に幅・高さと角丸半径で判定する。サムネイルや四角いボタンは既存の描画を使う。

試験版は `dist/paddle-edition/Offline PDF Converter (v4.4)-macOS-arm64.zip`。展開したアプリの実行ファイルに `--trial-liquid-glass` を渡す。引数なしで既定表示、`--trial-liquid-glass=simulate-failure` で従来表示への復帰を試せる。検証画像とログの保存先は `artifacts/liquid-glass-verification/pill-controls/verified/`。

Mac Apple Silicon で ZIP 展開後の署名を検証した。通信を遮断した状態で、オン・オフ・強制フォールバックの GUI 検証各9項目とオフライン自己検証各15項目が通過。GUI 検証で5つのセレクター、「開始」、編集画面の「完了」の適用状態を調べ、変換・編集・OCR の操作を実行した。実画面で Light／Dark、モード切り替え、領域外でのドラッグ解除、キー操作、リサイズを確認した。両 RID の依存関係は元の v4.4 と一致。Windows x64 での発行・実行と、今回の対象拡張後の GPU 負荷は未確認。

## 元の背景に戻したモード切替の試験

モード切替トラックを既存の配色と形状に戻した。選択ピルだけが、トラックとヘッダーの実際の色と境界をサンプリングする。ガラスの縁に薄い拡散、無彩色の反射と影を加えた。均一な色の上では光の曲がりが目立ちにくく、トラックの境界にかかるところで現れる。

この段階の ZIP は `artifacts/liquid-glass-verification/plain-glass/previous-package/`。検証ログは親ディレクトリにある。

## 斜めの模様を使った前回の屈折試験

以下は `130e728` 時点の記録。前回の ZIP は `artifacts/liquid-glass-verification/refraction/previous-package/` に退避した。

`codex/liquid-glass-v4.4` の静的な試作 `b57870f` に続き、.NET 8／Avalonia 11.3.12／SkiaSharp 3.119.2 のまま、選択ピルに SkSL の屈折を組み込んだ。試験用トラックの淡い斜めの光とレンズのサンプル元を同じ関数で描く。丸い縁の法線に沿って背景のサンプル位置を曲げ、薄く拡散させる。レンズの内縁に影、外周寄りに鏡面反射を加えた。ラベルと操作は既存の Avalonia コントロールが担当する。

背景の入力はヘッダーに描いた模様とテーマの色に限る。既存の SkiaSharp で計算が完結する。再描画は既存のピル移動、テーマ色、レイアウトの変化に追従する。シェーダーや描画の初期化に失敗した場合は、試験用のトラックとピル装飾を外し、既存表示に戻る。

展開後の実行ファイルを `--trial-liquid-glass` 付きで起動する。引数なしで従来表示に戻り、`--trial-liquid-glass=simulate-failure` ではシェーダーのコンパイルを意図的に失敗させて復帰を確認できる。Windows の発行手順は下記を参照。

検証環境は Apple M3、macOS 27.2.1、外部ディスプレイの倍率 1.0。結果、比較画像、再現用の測定データは `artifacts/liquid-glass-verification/refraction/` に保存した。

| 項目 | 結果 |
| --- | --- |
| Mac 配布版 | arm64 の ZIP を発行し、展開後の ad-hoc 署名を検証。通信を遮断したプロセスでオン・オフ・強制フォールバックの GUI 検証各9項目、オフライン自己検証各15項目が通過。 |
| 比較画像 | Light と Dark でオフと強制フォールバックのヘッダーが画素単位で一致。基準版との比較でも一致。オンの差はモード切替領域の内側。 |
| 屈折の画素検証 | 本体の SkSL と、サンプル位置の変位だけをゼロにした版を3つのピル位置・1倍と2倍で比較。各条件で縁付近に画素差があり、平坦な中央は一致。2倍はオフスクリーンで検証。 |
| 実ウィンドウの入力 | モーション軽減を有効にして、クリック、領域外へのドラッグ解除、再フォーカス後の右矢印、縮小と復元を確認。 |
| 依存と Windows | `osx-arm64` と `win-x64` の復元グラフは元の v4.4 と一致。Windows x64 での発行・描画・実行は、配布用 CRT を用意できる Windows ホストがないため未実施。 |

Mac 実機でオンとオフを各3回計測した。ネイティブ資産は毎回新規展開し、同じ画面で3秒待機してから、15回のモード切替・ドラッグ・矢印キー・リサイズを約7秒で行った。

| 指標 | オフの中央値 | オンの中央値 |
| --- | ---: | ---: |
| ウィンドウが出るまで（秒） | 0.984 | 1.019 |
| 待機中（CPU 秒） | 0.17 | 0.19 |
| 操作中（CPU 秒） | 2.04 | 2.11 |
| 待機後／操作後 RSS（KiB） | 279,296／290,608 | 280,128／291,072 |

生データは `performance.jsonl`。GPU とフレーム時間、実ディスプレイでの2倍表示は未測定。Mac の試験 ZIP はソースのローカルコミット前に同じ差分で発行し、内部の情報バージョンには基点の SHA が付く。

## 静的な装飾を試した初回の記録

以下は `b57870f` 時点の結果。静的試作の ZIP は `artifacts/liquid-glass-verification/static-trial/` に退避した。

## 基準と試験範囲

2026年11月22日に取得した GitHub `main` の先頭は `b88ecc80821e5aca2579502976bd6dc2db933d22`。公開タグ `v4.4.0` は `9bdc05ba7cb95dbf90191eae56d92e6a71117977`。ツリー差分は `MainWindow.axaml` の縮小対応と周囲の背景色のみ。試験は確認した `main` を基準に、独立したローカルクローンの `codex/liquid-glass-v4.4` で実施した。

ヘッダーの `ModeSelectionPill` の内側に、Avalonia の標準ブラシで半透明のグラデーションと細い縁を描く。既存のピルを下地とし、ラベルは既存の描画順で重なる。装飾は入力とフォーカスを受けず、ピル本体の移動に従う。既定オフと生成失敗を模擬した起動では装飾を生成しない。背景の屈折・ぼかしは未導入。

## 起動と切り戻し

Mac の試験 ZIP を基準版と別の場所に展開する。展開先で次を実行する。

```sh
app="./Offline PDF Converter (v4.4).app"
codesign --verify --deep --strict "$app"
exe="$app/Contents/MacOS/OfflinePDFConverter.PaddleEdition"
"$exe" --trial-liquid-glass
"$exe" --trial-liquid-glass=simulate-failure
```

各状態はアプリを終了してから起動する。通常起動または引数なしでオフに戻る。両方の値が指定された場合は失敗の模擬を優先する。各試験引数は `--verify-gui <report.json>` と併用できる。 `--verify-offline` は処理機能の自己検証で、描画は `--verify-gui` と画面で判定する。

同じ Mac パッケージを発行するには `python3 scripts/build-paddle-edition.py --target mac` を使う。Windows x64 の検証環境では、Visual Studio の配布用 CRT を用意し、次を実行する。

```powershell
python scripts/build-paddle-edition.py --target windows
$publish = 'artifacts/paddle-edition/publish-windows'
$exe = "$publish/OfflinePDFConverter.PaddleEdition.exe"
python scripts/verify-published-exe.py $publish
pwsh -File scripts/verify-windows-single-exe.ps1 -ExePath $exe -BlockNetwork -ReportPath artifacts/windows-offline.json
pwsh -File scripts/verify-windows-single-exe.ps1 -ExePath $exe -BlockNetwork -VerifyGui -ReportPath artifacts/windows-gui.json
& $exe --trial-liquid-glass --verify-gui artifacts/windows-on-gui.json
& $exe --trial-liquid-glass=simulate-failure --verify-gui artifacts/windows-fallback-gui.json
```

標準の Windows スクリプトは既定オフを検証する。オンと強制フォールバックの2コマンドも、隔離した Windows ホストを切断してから実行し、同じホストで画面と操作を比較する。

## LiquidAvalonia の採否

NuGet の `LiquidAvalonia 1.0.0`（MIT、SHA-256 `da885f9ef6bab60648637a8331ac2aabd2d8513135e269194d0acb7f1a260149`）を、合成の縞背景と FluentTheme の隔離サンプルに復元した。パッケージの `netstandard2.1` 資産を使い、.NET 8、Avalonia 11.3.12、PDFtoImage 5.2.1 と SkiaSharp 3.119.2 で起動したところ、`GlassImpl` の生成時に `SKRuntimeEffect.Create(string, out string)` の `MissingMethodException` が発生した。描画まで到達していない。パッケージに記載されたソースコミットでは、背景取得も `SkSurface.Snapshot()` でサーフェス全体を対象とする。試験版への依存追加は見送った。 [固定版のソース](https://github.com/A1isandr/LiquidAvalonia/blob/a0225e9c310053efa50137b1f4c955dffb9b9d02/LiquidAvalonia/Controls/GlassImpl.cs)

## 発行と依存関係

| 確認項目 | 結果 |
| --- | --- |
| Mac の基準版と試験版 | 既存の発行スクリプトで arm64 の PaddleOCR 版を発行。ZIP 展開後の ad-hoc 署名の厳密検証が両方通過。アプリと内部版数は v4.4／4.4.0。 |
| ZIP の差分 | 基準版 110,586,711 バイト、試験版 110,585,034 バイト。16 エントリの名前は一致し、内容差はアプリ本体のみ。本体サイズは両方 205,918,976 バイト。 |
| `osx-arm64` と `win-x64` の復元 | 両 RID とも `project.assets.json` の `libraries` と `targets` が基準版と試験版で一致。Avalonia／Avalonia.Skia 11.3.12、SkiaSharp と両 RID のネイティブ資産 3.119.2、PDFium 147.0.7690、RapidOcrNet 4.2.0、ONNX Runtime 1.29.0、HarfBuzzSharp とそのネイティブ資産 8.3.1.1。 |
| Windows x64 の発行・実行 | 未実施。Mac からの発行は既存の MSBuild ターゲットが Visual Studio の x64 CRT を用意できる Windows ホストを要求して停止。対象 Windows 環境を利用できず、単体 exe・CRT 内包・Windows 10／11・クリーン VM・Windows Server CI は今回の試験版で未確認。 |

Mac のフォントは既存の準備済み Noto Sans JP を使い、既存スクリプトでマニフェストの SHA-256 とサイズを照合した。システム Python 3.9 用の FontTools 4.60.2 はこの照合でロードし、フォント生成は行っていない。

## macOS の実機検証

Apple M3（8 GB）、macOS 27.2.1（26B131）、外部ディスプレイ 1920 × 1080、表示倍率 1.0。基準版と試験版オフ／オン／強制フォールバックの4状態で、展開した `.app` の実行ファイルを新しいバンドル展開先から実行した。 `sandbox-exec` のプロファイル `(version 1)(allow default)(deny network*)` で対象プロセスの通信を遮断した。ループバック接続は対照で成功し、プロファイル下では `EPERM` になった。ホスト全体の切断と通信試行の監視は未実施。

| 確認項目 | 結果 |
| --- | --- |
| オフライン自己検証 | 全4状態で合格（各15項目）。PNG／JPEG 変換、画像からPDF、結合・分割・削除・抽出、日本語文字と図形の保存、4種の OCR プロファイル、同名保護・不正入力・中止を確認。 |
| GUI 自己検証 | 全4状態で合格（基準版8項目、試験版は各9項目）。Auto／Light／Dark、開始操作からの変換・分割・編集・OCR、文字コピー、処理中の入力抑止とキャンセルを確認。 |
| 装飾の範囲 | 実ウィンドウの Light／Dark／450 × 350 のヘッダー比較で、オフと強制フォールバックは基準版と画素単位で一致。オンの差分は選択ピル内。Dark の GUI 自己検証画像でも同じ結果。 |
| 配布アプリの起動 | ZIP 展開後の `.app` を LaunchServices 経由で起動し、通常のウィンドウが開くことを確認。 |
| 回帰スイート | 両版で RegressionTests 47件、SearchablePdfTests 8件、PaddleEditionTests 8件、SmallTextOcrTests 4件が通過。旧回帰スイートの明示的な外部 OCR ケースと SmallTextOcrTests の任意のユーザー提供文書は対象外。合成文書の PaddleOCR／Tesseract 認識結果はそれぞれ27回すべてで両版が一致。 |
| 実ウィンドウの入力と伸縮 | モードの連続クリック、領域外でのドラッグ解除、再フォーカス後の右矢印を確認。既存の `OFFLINE_PDF_CONVERTER_REDUCE_MOTION=1` を使った起動でも3モードを切り替え、340 × 332 に縮小、1480 × 850 に拡大し、元のサイズへ復元した。 |
| 未実施の操作確認 | Retina・モニター間移動、OS テーマのライブ変更、VoiceOver と Tab 順序の全体確認、文書のドラッグ＆ドロップ、ファイルダイアログの手動確認、OS の全画面化。 |

## macOS の負荷

基準版、オフ、オンを各3回、実行順を入れ替えて測った。毎回新規のネイティブ資産展開先を使用し、OS のファイルキャッシュは保持した。ウィンドウが現れるまでの時間、静止3秒の対象プロセス CPU、15回の切替とドラッグ・キー操作・リサイズを含む約7秒の区間の CPU、各区間末尾の RSS を計測した。

| 指標 | 基準版 | オフ | オン |
| --- | ---: | ---: | ---: |
| 起動時間、中央値［最小–最大］（秒） | 1.184［0.918–1.222］ | 0.989［0.975–1.122］ | 0.993［0.971–1.012］ |
| 静止時 CPU、中央値［最小–最大］（CPU 秒） | 0.17［0.16–0.19］ | 0.18［0.17–0.18］ | 0.18［0.17–0.19］ |
| 操作中 CPU、中央値［最小–最大］（CPU 秒） | 2.11［2.04–2.18］ | 2.14［1.90–2.18］ | 2.02［1.82–2.12］ |
| 静止後／操作後 RSS、中央値（KiB） | 278,992／290,224 | 278,944／290,144 | 279,760／290,832 |
| 変換・編集・OCR を含む GUI 検証1回の経過時間（秒） | 10.29 | 10.47 | 10.47 |

測定した操作では一貫した CPU 増加は見られず、オンの RSS 中央値はオフより約 0.7～0.8 MiB 高かった。強制フォールバックの代表値は `live-confirmed.jsonl` に記録した。今回の計測回数で性能上限は設定していない。GPU、フレーム時間、長時間の再描画やメモリの推移、GPU 制限環境、処理工程別の CPU／GPU は未測定。

## 結果と再現資料

結果の保存先はクローン直下の `artifacts/liquid-glass-verification/`。 `baseline/` と `trial/` に署名ログ、両 RID の復元資産、自己検証 JSON、実ウィンドウ画像、回帰テストのログを保存した。 `candidate/` に隔離サンプルと起動時の例外ログがある。 `mac-pill-light-off-on.png` と `mac-pill-dark-off-on.png` は上段がオフ、下段がオン。 `mac-pill-input-sequence.png` は初期状態、OCR クリック、領域外へドラッグ、再フォーカス後の右矢印の順。 `mac-package-diff.json`、`live-image-diffs.json`、`live-performance.jsonl`、`live-performance-summary.json` に比較の数値を記録した。初回の試験 ZIP はローカルコミットを作成する前に、当時の差分を含めて発行した。

静的装飾のみをローカル試作に採用した。Windows の発行と実機確認が終わるまで、両 OS の完成条件の判定は保留。屈折を試す場合の次候補は、現行 Avalonia と SkiaSharp に合わせ、許可されたヘッダーの装飾だけをサンプリングする最小の SkSL 描画。
