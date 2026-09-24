#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
ocr_root="$PWD/native-build"
ocr_prefix="$ocr_root/install"
ocr_toolchain="$PWD/scripts/windows-toolchain.cmake"
common=(-G Ninja -DCMAKE_TOOLCHAIN_FILE="$ocr_toolchain" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$ocr_prefix" -DCMAKE_FIND_ROOT_PATH="$ocr_prefix" -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DZLIB_LIBRARY="$ocr_prefix/lib/libzs.a" -DZLIB_INCLUDE_DIR="$ocr_prefix/include")
cmake -S "$ocr_root/source/zlib-1.3.2" -B "$ocr_root/zlib" "${common[@]}" -DZLIB_BUILD_SHARED=OFF -DZLIB_BUILD_STATIC=ON -DZLIB_BUILD_TESTING=OFF
cmake --build "$ocr_root/zlib" --parallel 4
cmake --install "$ocr_root/zlib"
cmake -S "$ocr_root/source/libpng-1.6.58" -B "$ocr_root/png" "${common[@]}" -DPNG_SHARED=OFF -DPNG_STATIC=ON -DPNG_TESTS=OFF -DPNG_TOOLS=OFF -DZLIB_ROOT="$ocr_prefix"
cmake --build "$ocr_root/png" --parallel 4
cmake --install "$ocr_root/png"
cmake -S "$ocr_root/source/leptonica-1.87.0" -B "$ocr_root/leptonica" "${common[@]}" -DBUILD_SHARED_LIBS=OFF -DSW_BUILD=OFF -DBUILD_PROG=OFF -DENABLE_PNG=ON -DENABLE_ZLIB=ON -DENABLE_GIF=OFF -DENABLE_JPEG=OFF -DENABLE_TIFF=OFF -DENABLE_WEBP=OFF -DENABLE_OPENJPEG=OFF
cmake --build "$ocr_root/leptonica" --parallel 4
cmake --install "$ocr_root/leptonica"
python3 - <<'PATCH'
from pathlib import Path
p=Path('native-build/source/tesseract-5.5.3/CMakeLists.txt')
s=p.read_text()
if 'ADDITIONAL_OCR_RC' not in s:
 s=s.replace('add_executable(tesseract src/tesseract.cpp)', 'add_executable(tesseract src/tesseract.cpp)\nif(ADDITIONAL_OCR_RC)\n  target_sources(tesseract PRIVATE "${ADDITIONAL_OCR_RC}")\n  get_filename_component(ocr_resource_dir "${ADDITIONAL_OCR_RC}" DIRECTORY)\n  target_include_directories(tesseract PRIVATE "${ocr_resource_dir}")\nendif()')
 p.write_text(s)
PATCH
cmake -S "$ocr_root/source/tesseract-5.5.3" -B "$ocr_root/tesseract" "${common[@]}" -DBUILD_SHARED_LIBS=OFF -DBUILD_TRAINING_TOOLS=OFF -DBUILD_TESTS=OFF -DGRAPHICS_DISABLED=ON -DDISABLE_CURL=ON -DDISABLE_ARCHIVE=ON -DDISABLE_TIFF=ON -DOPENMP_BUILD=OFF -DENABLE_NATIVE=OFF -DENABLE_LTO=OFF -DENABLE_PRECOMPILED_HEADERS=OFF -DLEPT_TIFF_RESULT=1 -DADDITIONAL_OCR_RC="$PWD/scripts/ocr-windows.rc" -DLeptonica_DIR="$ocr_prefix/lib/cmake/leptonica"
cmake --build "$ocr_root/tesseract" --target tesseract --parallel 4
x86_64-w64-mingw32-strip "$ocr_root/tesseract/bin/tesseract.exe"
x86_64-w64-mingw32-objdump -p "$ocr_root/tesseract/bin/tesseract.exe" > "$ocr_root/windows-imports.txt"
