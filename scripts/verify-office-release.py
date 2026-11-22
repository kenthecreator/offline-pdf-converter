"""Compare PDF output from the same Word, Excel and PowerPoint fixtures."""
import argparse
import re
from contextlib import closing
from pathlib import Path

import pypdfium2 as pdfium

FIXTURES = {
    "日本語 サンプル.pdf": [
        ("Wordsample", "レイアウト検証", "Orange", "120", "Lemon", "340"),
        ("Pagetwomarker", "同じページにある段落"),
    ],
    "日本語 ワークブック.pdf": [
        ("Excelsample", "月別集計", "関東", "関西", "January", "February"),
    ],
    "日本語 スライド.pdf": [
        ("PowerPointsample", "位置確認"),
        ("Secondslidemarker",),
    ],
}


def inspect(file, expected):
    pages = []
    with pdfium.PdfDocument(file) as doc:
        if len(doc) != len(expected):
            raise ValueError(f"{file}: expected {len(expected)} pages, got {len(doc)}")
        for index, markers in enumerate(expected):
            with closing(doc[index]) as page, closing(page.get_textpage()) as text_page:
                raw = text_page.get_text_range()
                text = re.sub(r"\s+", "", raw)
                if any(mark not in text for mark in markers) or any(
                    mark in text.lower() for mark in ("aspose", "evaluation", "outsideprintarea")
                ):
                    raise ValueError(f"{file}, page {index + 1}: unexpected contents: {raw!r}")
                bounds = [
                    text_page.get_charbox(i)
                    for i in range(text_page.count_chars())
                    if text_page.get_text_range(i, 1).strip()
                ]
                pages.append((page.get_size(), text, bounds, graphics_bounds(page, file.name, index)))
    return pages


def graphics_bounds(page, filename, index):
    if index != 0:
        return []
    colors = {
        "日本語 サンプル.pdf": [(204, 39, 128)],
        "日本語 ワークブック.pdf": [(29, 77, 143)],
        "日本語 スライド.pdf": [(226, 72, 49), (204, 39, 128)],
    }[filename]
    with closing(page.render(scale=1)) as bitmap:
        image = bitmap.to_pil().convert("RGB")
    points = [[image.width, image.height, -1, -1] for _ in colors]
    for offset, pixel in enumerate(image.get_flattened_data()):
        for i, color in enumerate(colors):
            if max(abs(actual - desired) for actual, desired in zip(pixel, color)) < 7:
                x, y = offset % image.width, offset // image.width
                left, top, right, bottom = points[i]
                points[i] = [min(left, x), min(top, y), max(right, x), max(bottom, y)]
    if any(right < 0 for _, _, right, _ in points):
        raise ValueError(f"{filename}: missing color landmarks")
    if filename == "日本語 スライド.pdf":
        check_coordinates(points[0], [72, 144, 316, 301], 3, "PowerPoint rectangle")
        check_coordinates(points[1][:2], [504, 144], 3, "PowerPoint picture")
    if filename == "日本語 ワークブック.pdf" and image.width <= image.height:
        raise ValueError(f"{filename}: expected a landscape print area")
    return points


def check_coordinates(actual, expected, tolerance, context):
    if len(actual) != len(expected) or any(abs(a - b) > tolerance for a, b in zip(actual, expected)):
        raise ValueError(f"{context}: {actual} differs from {expected} (tolerance: {tolerance} pt)")


def compare(actual, expected, filename):
    for i, (a, b) in enumerate(zip(actual, expected), start=1):
        context = f"{filename}, page {i}"
        check_coordinates(a[0], b[0], 0.2, context)
        if a[1] != b[1] or len(a[2]) != len(b[2]):
            raise ValueError(f"{context}: text or reading order changed")
        for p, q in zip(a[2], b[2]):
            check_coordinates(p, q, 3, context + " text placement")
        for p, q in zip(a[3], b[3]):
            check_coordinates(p, q, 3, context + " graphic placement")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("pdfs", type=Path)
    parser.add_argument("--reference", type=Path)
    args = parser.parse_args()
    for name, expected in FIXTURES.items():
        actual = inspect(args.pdfs / name, expected)
        if args.reference:
            compare(actual, inspect(args.reference / name, expected), name)
        print(f"Verified {name}: {len(actual)} pages")


if __name__ == "__main__":
    main()
