# v4.2 Windows単体exeの作成

開発用.NET 8 SDKを使用します。利用者向けのv4.2はPaddleOCRの内蔵版です。

```sh
dotnet publish src/OfflinePDFConverter/OfflinePDFConverter.csproj -p:PaddleOcrEdition=true -p:PublishProfile=WindowsSingleFile -p:UsedAvaloniaProducts= -o artifacts/windows-v4.2
python3 scripts/verify-published-exe.py artifacts/windows-v4.2
```

発行される `OfflinePDFConverter.PaddleEdition.exe` を配布時に `Offline PDF Converter (v4.2).exe` に変更します。自己完結、ネイティブ部品内包で、不要なデバッグ情報・インポートライブラリを除外し、exe以外が残ればエラーになります。

```powershell
pwsh -File scripts/verify-windows-single-exe.ps1 -ExePath artifacts/windows-v4.2/OfflinePDFConverter.PaddleEdition.exe
```

Windows 10（1903以降）／11 x64向けです。v4.2の単体exeにはVisual C++ x64のrelease CRT DLLも埋め込みます。利用者のPCへのランタイム導入は不要です。.NET、Tesseract、Python、GPUの追加インストールやOCRモデルの実行時ダウンロードは不要です。

## ビルド時のCRT同梱

ビルド用Windowsには、適用されるライセンス条件を満たすVisual StudioのC++ビルド環境が必要です。`scripts/prepare-windows-crt.ps1`がVisual Studioの`VC/Redist/MSVC/<version>/x64/Microsoft.VC*.CRT`から未改変のrelease DLLを収集し、MSBuildがネイティブ部品として単体exeに埋め込みます。System32、debug_nonredist、非公式DLLサイトからは取得しません。これはビルド時のみの処理で、利用者のPCではインストーラ・ダウンロード・管理者権限を使いません。

自動検出できない場合は、`-p:WindowsCrtSourceDirectory="C:/.../x64/Microsoft.VC143.CRT"`を指定してください。CRTが見つからない場合、外部ランタイムに依存するexeを黙って作らず、ビルドを失敗させます。新しいWindows配布物はWindowsでビルドしてください。DLLの版とSHA-256はexe内の`OfflinePDFConverter.WindowsCrtManifest.json`に記録します。

実行時には.NETがDLLを利用者専用の一時フォルダに展開します。配布・持ち運びに必要な実行ファイルはexe一つですが、ディスクへの一時展開は必要です。Windows本体に含まれるOS部品には依存します。

## リリース前の検証

1. `python scripts/verify-published-exe.py <publishフォルダ>`で、OCRモデルとCRT DLLの実データがexeに埋め込まれていることを確認します。
2. `scripts/verify-windows-single-exe.ps1`で、新規のDLL展開先とexeだけのフォルダからOCRを実行します。自己検証はCRTのハッシュと読み込み元も調べ、システムのCRTに依存していた場合は失敗します。
3. Visual C++再頒布可能パッケージ・.NET・Pythonを追加していないWindows 10/11の検証用VMへexeだけをコピーし、ネットワークアダプタを切断します。最初の起動から`"Offline PDF Converter (v4.2).exe" --verify-offline report.json`を実行し、`passed: true`を確認します。その後、日本語横書き・縦書き・英語・混在のGUI操作も確認します。

CIホストには開発用ランタイムがあるため、CI成功だけで未導入・完全オフライン環境の検証済みとは扱いません。公開済みv4.0のZIPにはこの変更は含まれていません。

CIでは追加で`verify-windows-single-exe.ps1 -BlockNetwork -ReportPath artifacts/network-blocked-selftest.json`を実行します。テスト対象exeに全プロファイルの送受信ブロックを適用し、有効なルールと適用先を確認してから、新しい一時フォルダでOCRを実行します。ルールとプロファイル設定は終了時に戻します。このオプションは検証用の使い捨てWindowsホストで管理者として実行します。これはアプリ単位の通信遮断テストで、ホスト全体のネットワーク切断やWindows 10/11の検証を意味しません。

## Mac・WindowsのZIP作成

```sh
python3 scripts/build-paddle-edition.py --target both
```

モデル取得・更新は開発時だけの作業です。`scripts/prepare-paddle-ocr.py`、`scripts/prepare-ocr-language-models.py` と内蔵manifestに取得元・固定ハッシュを記録しています。旧Tesseractの構築スクリプトは旧実装の回帰検証用に残しています。

## v4.2機能・GUI検証

```powershell
./scripts/verify-windows-single-exe.ps1 -ExePath artifacts/windows-v4.2/OfflinePDFConverter.PaddleEdition.exe -BlockNetwork -ReportPath artifacts/windows-features.json
./scripts/verify-windows-single-exe.ps1 -ExePath artifacts/windows-v4.2/OfflinePDFConverter.PaddleEdition.exe -BlockNetwork -VerifyGui -ReportPath artifacts/windows-gui.json
```

Firewallを変更するこのスクリプトは検証用のWindows CI/VMで管理者として実行します。正常終了・失敗ともルールを削除してプロファイル設定を復元します。配布exeの通常実行ではFirewallを変更しません。結果JSONとGUIスクリーンショットを保存します。
