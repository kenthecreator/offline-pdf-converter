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

## macOS版の配布

macOS版のアイコンは、`.app/Contents/Resources/AppIcon.icns` を配置し、
`Info.plist` の `CFBundleIconFile` から参照します。Finderの「情報を見る」経由で
カスタムアイコンを貼り付ける方法は使用しません。この方法で付く
`com.apple.FinderInfo` やリソースフォークは、署名後のアプリをmacOSが
不正な付加データ付きと判定する原因になります。

配布前は、不要な拡張属性を除去してからアプリ全体を署名し、厳格な検証を行います。

```bash
xattr -cr "Offline PDF Converter (v3.1.0).app"
codesign --force --deep --sign - --timestamp=none \
  "Offline PDF Converter (v3.1.0).app"
codesign --verify --deep --strict \
  "Offline PDF Converter (v3.1.0).app"
```

上記の `-` はad-hoc署名です。署名整合性は確認できますが、初回起動時の
Gatekeeper警告はなくなりません。一般利用者が警告なしで起動できる配布物には、
Apple Developer ProgramのDeveloper ID証明書による署名とAppleの公証が必要です。

ZIP作成後は、ZIPから別フォルダへ展開した `.app` に対しても同じ
`codesign --verify --deep --strict` を実行し、SHA-256チェックサムを公開します。
