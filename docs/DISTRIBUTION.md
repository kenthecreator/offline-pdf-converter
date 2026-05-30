# 配布ファイル構成

## 通常構成

操作に必要なのは `.exe` だけです。ライセンス表記は配布物として同じフォルダに置いてください。

```text
Offline PDF Converter/
  Offline PDF Converter.exe
  THIRD_PARTY_LICENSES.md
  MANUAL.md
```

`Offline PDF Converter.exe` をダブルクリックして起動します。Python、Poppler、Adobe製品のインストールは不要です。

## 単体exeだけで配布する場合

アプリの動作自体は `Offline PDF Converter.exe` 単体で可能です。ただし、OSSライセンス表記の保持が必要になる場合があります。配布時は `THIRD_PARTY_LICENSES.md` もあわせて提供してください。

## フォルダ配布方式を使う場合

単体exeがセキュリティ設定や一時フォルダ展開の制限で起動できない場合は、`dotnet publish` のフォルダ配布方式で作成した `publish` フォルダ全体を配布します。

```text
Offline PDF Converter/
  Offline PDF Converter.exe
  *.dll
  runtimes/
  その他の発行ファイル
  THIRD_PARTY_LICENSES.md
  MANUAL.md
```

通常は単体exe方式を優先してください。
