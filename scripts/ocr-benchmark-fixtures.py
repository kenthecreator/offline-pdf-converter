"""Create deterministic, raster-only Japanese OCR fixtures; never redistribute fonts."""
import hashlib
import io
import json
import random
import unicodedata
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/ocr-benchmark/fixtures'
OUT.mkdir(parents=True, exist_ok=True)
fonts = list(Path('/System/Library/Fonts').glob('*.ttc'))
def font_path(name):
    return next(p for p in fonts if unicodedata.normalize('NFC', p.name) == name)
sans = font_path('ヒラギノ角ゴシック W3.ttc')
serif = font_path('ヒラギノ明朝 ProN.ttc')
lines = [
    '資料の保存と文字認識の確認',
    'この文書は日本語の文字認識を比較するための評価用資料です。',
    '外部の通信サービスを使わず、端末内で処理を完了します。',
    '認識結果は原本と照合し、数字や固有名詞を確認してください。',
    '東京都千代田区丸の内一丁目、担当者は佐藤健一です。',
    '受付番号はＡＢＣ１２３、金額は１２，８００円です。',
    '処理の途中で中止した場合は、完成済みのファイルを残します。',
    '同じ名前のファイルがある場合、連番を付けて保存します。',
]
cases = []
def add(name, text_lines, font_file=sans, size=50, vertical=False, degrade=False, columns=False):
    image = Image.new('RGB', (2480, 3508), 'white') # A4, 300 dpi
    draw = ImageDraw.Draw(image)
    font = ImageFont.truetype(str(font_file), size)
    if vertical:
        for col, line in enumerate(text_lines):
            for row, char in enumerate(line):
                draw.text((2250-col*(size+35), 200+row*(size+6)), char, font=font, fill='black')
    elif columns:
        for i, line in enumerate(text_lines):
            col, row = divmod(i, len(text_lines)//2)
            draw.text((140+col*1190, 200+row*(size+45)), line, font=font, fill='black')
    else:
        for i, line in enumerate(text_lines):
            draw.text((150, 200+i*(size+45)), line, font=font, fill='black')
    if degrade:
        image = image.resize((1240,1754), Image.Resampling.BILINEAR).resize(image.size, Image.Resampling.BILINEAR)
        image = image.filter(ImageFilter.GaussianBlur(.65)).rotate(1.2, fillcolor='white')
        buff = io.BytesIO(); image.save(buff, 'JPEG', quality=45); buff.seek(0)
        image = Image.open(buff).convert('RGB')
    path = OUT / (name+'.png'); image.save(path, dpi=(300,300))
    cases.append({'id':name, 'image':path.name, 'truth':'\n'.join(text_lines),
                  'language':'jpn_vert+eng' if vertical else 'eng' if name=='english' else 'jpn+eng',
                  'layout':'vertical' if vertical else 'two_columns' if columns else 'horizontal',
                  'font':font_file.name, 'font_size_px':size, 'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
add('horizontal_sans', lines)
add('horizontal_serif', lines, serif)
add('degraded_scan', lines, degrade=True)
add('small_print', lines, size=33)
add('mixed', ['PDF Converter OCR Evaluation', '日本語と English の混在を確認します。',
              'Invoice No. AB-12345 / Date: 2026-09-30', '小計 12,800円 / Tax 1,280円 / Total 14,080円',
              '電話 03-1234-5678 / mail@example.test', '保存先 C:/Documents/Reports/sample.pdf'])
add('vertical', ['日本語の縦書き資料を確認します', '文字認識の精度と処理速度を比較',
                 '東京都千代田区丸の内一丁目', '認識した結果は原本と照合します', '同名の資料には連番を付けて保存'], vertical=True)
add('two_columns', ['左の段落を先に読みます', '日本語の文書を保存します', '文字と数字を確認します', '担当者は佐藤健一です',
                    '右の段落を次に読みます', '外部への送信は行いません', '処理が終われば保存します', '受付番号は１２３４です'], columns=True)
add('english', ['Offline PDF Converter OCR benchmark', 'This document tests text recognition accuracy.',
                'Invoice AB-12345 dated 2026-09-30.', 'Subtotal 12,800 / Tax 1,280 / Total 14,080.',
                'Save completed files without replacing existing files.'])
add('blank', [])
(OUT/'manifest.json').write_text(json.dumps({'seed':0,'dpi':300,'cases':cases}, ensure_ascii=False, indent=2))
# PDF embeds raster pages only, so no engine can use an existing text layer.
images = [Image.open(OUT/c['image']).convert('RGB') for c in cases]
images[0].save(OUT/'evaluation.pdf', save_all=True, append_images=images[1:], resolution=300)
print(f'Created {len(cases)} cases in {OUT}')
