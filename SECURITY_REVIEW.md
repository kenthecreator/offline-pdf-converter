# OfflinePDFImageConverter 安全性確認メモ

作成日: 2026-05-24

## 確認対象

```text
dist/release/OfflinePDFImageConverter.exe
```

ファイル種別:

```text
PE32+ executable (GUI) x86-64, for MS Windows
```

SHA-256:

```text
07d88fa47256b615c580cbf0738a271ef0497fea615d585dde9269506f251485
```

## アプリの性質

- PDFとJPEG/PNG画像を相互変換し、PDFの結合・分割・指定ページ削除・ページプレビュー表示と表示切り替えを行うローカルGUIアプリです。
- インストーラーではありません。
- 管理者権限は不要です。
- Adobe Acrobat、Adobeライセンス、Poppler、Python、PowerShell、バッチファイルには依存しません。
- .NET 8自己完結形式の単体exeです。
- 起動時にネイティブライブラリをWindowsの一時フォルダへ展開する可能性があります。

## 通信について

手書きソースコード内を確認した範囲では、以下のような通信・外部起動APIは使っていません。

- `HttpClient`
- `WebRequest`
- `Socket`
- `TcpClient`
- `UdpClient`
- `Process.Start`
- `System.Net`

注意: 自己完結exeには.NETランタイムが同梱されるため、`System.Net.*` などの標準ライブラリ自体は含まれることがあります。ただし、このアプリのコードから通信機能を呼び出してはいません。

## ファイル操作について

このアプリが行う主なファイル操作は次の通りです。

- ユーザーが選んだPDFを読み込む
- ユーザーが選んだJPEG/PNG画像を読み込む
- ユーザーが選んだ出力先フォルダへPNG/JPEG/PDFを書き出す
- ユーザーが選んだ複数PDFを読み込み、結合後のPDFを書き出す
- ユーザーが選んだPDFを1ページずつ別PDFとして書き出す
- ユーザーが指定したページを除いた新しいPDFを書き出す
- PDF/画像変換に必要な一時ファイルまたはネイティブライブラリを一時フォルダへ展開する可能性がある

元ファイルの削除、任意のファイル削除、レジストリ変更、サービス登録、スタートアップ登録は実装していません。

## ライブラリ

主な利用ライブラリ:

- Avalonia UI
- PDFtoImage
- PDFium
- SkiaSharp
- PDFsharp
- Microsoft .NET Runtime

詳細は `THIRD_PARTY_LICENSES.md` を参照してください。

## 重要な注意点

### 1. コード署名はまだありません

現状のexeは未署名です。そのため、Windows Defender SmartScreenで「発行元不明」「WindowsによってPCが保護されました」のような警告が出る可能性があります。

警告が出ること自体が直ちにマルウェア判定を意味するわけではありません。ただし、利用環境のセキュリティポリシーにより実行できない可能性があります。

### 2. 利用環境のポリシーを確認してください

利用前に、必要に応じて以下を確認してください。

- 未署名exeの実行が許可されているか
- OSSライブラリ利用が利用環境のルール上問題ないか
- PDFiumなどのネイティブライブラリ同梱が許可されているか

## 推奨する確認手順

1. `OfflinePDFImageConverter.exe` のSHA-256をこのメモと照合する。
2. Windows上でMicrosoft Defenderのファイルスキャンを実行する。
3. 必要に応じてウイルス対策ソフトやEDRで検査する。
4. 必要に応じてソースコード、ライセンス表、SHA-256を確認する。
5. 可能であれば正式なコード署名証明書で署名する。

## 配布対象

```text
dist/release/
  OfflinePDFImageConverter.exe
  README.md
  MANUAL.md
  THIRD_PARTY_LICENSES.md
  SECURITY_REVIEW.md
```

## 追加で安全性を上げる方法

- 正式なコード署名証明書でexeに署名する。
- 公式の配布場所を固定する。
- ハッシュ値を公開し、取得後に照合できるようにする。
- 単体exeが一時フォルダ展開で止まる場合は、フォルダ配布方式に切り替える。
