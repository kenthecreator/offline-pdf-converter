# v4.0 Windows単体exeの作成

開発用.NET 8 SDKを使用します。利用者向けのv4.0はPaddleOCRの内蔵版です。

```sh
dotnet publish src/OfflinePDFConverter/OfflinePDFConverter.csproj -p:PaddleOcrEdition=true -p:PublishProfile=WindowsSingleFile -p:UsedAvaloniaProducts= -o artifacts/windows-v4.0
python3 scripts/verify-published-exe.py artifacts/windows-v4.0
```

発行される `OfflinePDFConverter.PaddleEdition.exe` を配布時に `Offline PDF Converter (v4.0).exe` に変更します。自己完結、ネイティブ部品内包で、不要なデバッグ情報・インポートライブラリを除外し、exe以外が残ればエラーになります。

```powershell
pwsh -File scripts/verify-windows-single-exe.ps1 -ExePath artifacts/windows-v4.0/OfflinePDFConverter.PaddleEdition.exe
```

Windows 10（1903以降）／11 x64向けです。Microsoft Visual C++ v14（x64）ランタイムが必要です。.NET、Tesseract、Python、GPUの追加インストールやOCRモデルの実行時ダウンロードは不要です。

## Mac・WindowsのZIP作成

```sh
python3 scripts/build-paddle-edition.py --target both
```

モデル取得・更新は開発時だけの作業です。`scripts/prepare-paddle-ocr.py`、`scripts/prepare-ocr-language-models.py` と内蔵manifestに取得元・固定ハッシュを記録しています。旧Tesseractの構築スクリプトは旧実装の回帰検証用に残しています。
