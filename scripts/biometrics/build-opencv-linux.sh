#!/usr/bin/env bash
# Builds the minimal Linux x64 native runtime of the local face provider (ADR-012) from pinned sources:
# OpenCV 5.0.0 (+ only the contrib modules OpenCvSharpExtern cannot compile without) and OpenCvSharpExtern, as a matching
# pair of shared libraries. No FFmpeg, GStreamer, V4L, GTK/X11, Tesseract, videoio, highgui, text or stitching.
# Run on Debian 12 (the glibc of mcr.microsoft.com/dotnet/aspnet:9.0); the Web BFF Dockerfile runs it in its
# opencv-native stage. Native binaries are never committed.
#
# Output (OUT, default /out): libOpenCvSharpExtern.so, the libopencv_*.so.<soversion> it needs (RPATH $ORIGIN), and
# opencv-runtime-manifest.txt (pinned commits, CMake options, modules, build information, ELF dependencies, SHA-256).
# Third-party inventory: docs/legal/face-opencv-third-party.md.
#
# Usage: scripts/biometrics/build-opencv-linux.sh [out-dir] [work-dir]
set -euo pipefail

# Pinned upstream sources. OPENCV_COMMIT and OPENCV_CONTRIB_COMMIT are the submodule commits of OPENCVSHARP_COMMIT, the
# source commit declared by the OpenCvSharp5 5.0.0.20261003 packages (<repository commit> of their .nuspec).
OPENCVSHARP_COMMIT=d8fc8914e09b43fe6e2ec71c1e376aef5188acf1
OPENCV_COMMIT=40738fb16ceddb5fb3fea747585f7ce6abb0605b # tag 5.0.0
OPENCV_CONTRIB_COMMIT=755e50675d97db9b7d449d8bd6b09888646f6c6e

# OpenCV modules the face provider uses (core, imgproc, imgcodecs, dnn, objdetect) plus the contrib modules that
# OpenCvSharpExtern's include_opencv.h includes unconditionally at OPENCVSHARP_COMMIT (xobjdetect, xfeatures2d, ximgproc,
# xphoto, bgsegm, img_hash). CMake adds their own module dependencies (listed in the manifest).
BUILD_LIST="core,imgproc,imgcodecs,dnn,objdetect,xobjdetect,xfeatures2d,ximgproc,xphoto,bgsegm,img_hash"
CONTRIB_MODULES="xobjdetect xfeatures2d ximgproc xphoto bgsegm img_hash"

OUT=${1:-/out}
WORK=${2:-/tmp/opencv-build}
JOBS=${JOBS:-$(nproc)}
mkdir -p "$OUT" "$WORK"
OUT=$(cd "$OUT" && pwd)
WORK=$(cd "$WORK" && pwd)

fetch() { # name url commit
    local dir="$WORK/$1"
    # A checkout already at the pinned commit (a reused work dir) is kept; anything else is fetched again.
    if [ "$(git -C "$dir" rev-parse HEAD 2>/dev/null)" = "$3" ] && [ -z "$(git -C "$dir" status --porcelain)" ]; then
        echo "OK      $1 @ $3 (existing checkout)"
        return 0
    fi
    rm -rf "$dir"
    git init -q "$dir"
    git -C "$dir" fetch -q --depth 1 "$2" "$3"
    git -C "$dir" -c advice.detachedHead=false checkout -q FETCH_HEAD
    local actual
    actual=$(git -C "$dir" rev-parse HEAD)
    if [ "$actual" != "$3" ]; then
        echo "Commit mismatch for $1: expected $3, got $actual" >&2
        exit 1
    fi
    echo "OK      $1 @ $actual"
}

fetch opencv https://github.com/opencv/opencv.git "$OPENCV_COMMIT"
fetch opencv_contrib https://github.com/opencv/opencv_contrib.git "$OPENCV_CONTRIB_COMMIT"
fetch opencvsharp https://github.com/shimat/opencvsharp.git "$OPENCVSHARP_COMMIT"

extra_modules=""
for module in $CONTRIB_MODULES; do
    extra_modules="$extra_modules;$WORK/opencv_contrib/modules/$module"
done
extra_modules=${extra_modules#;}

PREFIX="$WORK/install"
OPENCV_OPTIONS=(
    -D CMAKE_BUILD_TYPE=Release
    -D CMAKE_INSTALL_PREFIX="$PREFIX"
    -D CMAKE_INSTALL_RPATH='$ORIGIN'
    -D BUILD_SHARED_LIBS=ON
    -D BUILD_LIST="$BUILD_LIST"
    -D OPENCV_EXTRA_MODULES_PATH="$extra_modules"
    -D BUILD_TESTS=OFF -D BUILD_PERF_TESTS=OFF -D BUILD_EXAMPLES=OFF -D BUILD_opencv_apps=OFF -D BUILD_DOCS=OFF
    -D BUILD_JAVA=OFF -D BUILD_opencv_python3=OFF -D BUILD_opencv_python_bindings_generator=OFF
    -D BUILD_opencv_java_bindings_generator=OFF -D BUILD_opencv_js_bindings_generator=OFF
    -D BUILD_opencv_objc_bindings_generator=OFF -D OPENCV_GENERATE_PKGCONFIG=OFF
    # No video, camera or GUI stack at all.
    -D BUILD_opencv_videoio=OFF -D BUILD_opencv_highgui=OFF
    -D WITH_FFMPEG=OFF -D WITH_GSTREAMER=OFF -D WITH_V4L=OFF -D WITH_1394=OFF -D WITH_OBSENSOR=OFF
    -D VIDEOIO_ENABLE_PLUGINS=OFF -D HIGHGUI_ENABLE_PLUGINS=OFF
    -D WITH_GTK=OFF -D WITH_QT=OFF -D WITH_OPENGL=OFF -D WITH_VTK=OFF -D WITH_WAYLAND=OFF -D WITH_FRAMEBUFFER=OFF
    # Codecs: PNG only (the normalized capture), from the pinned OpenCV 3rdparty sources (zlib, libpng).
    -D WITH_PNG=ON -D BUILD_PNG=ON -D BUILD_ZLIB=ON -D WITH_SPNG=OFF
    -D WITH_JPEG=OFF -D WITH_TIFF=OFF -D WITH_WEBP=OFF -D WITH_OPENJPEG=OFF -D WITH_JASPER=OFF -D WITH_OPENEXR=OFF
    -D WITH_AVIF=OFF -D WITH_JPEGXL=OFF -D WITH_GDAL=OFF -D WITH_GDCM=OFF -D WITH_IMGCODEC_HDR=OFF
    -D WITH_IMGCODEC_SUNRASTER=OFF -D WITH_IMGCODEC_PXM=OFF -D WITH_IMGCODEC_PFM=OFF -D WITH_IMGCODEC_GIF=OFF
    # dnn: ONNX through the bundled protobuf only; no accelerator back ends.
    -D WITH_PROTOBUF=ON -D BUILD_PROTOBUF=ON -D WITH_FLATBUFFERS=OFF -D WITH_OPENVINO=OFF -D WITH_VULKAN=OFF
    -D WITH_CUDA=OFF -D WITH_CANN=OFF -D WITH_TIMVX=OFF -D WITH_WEBNN=OFF -D OPENCV_DNN_OPENCL=OFF
    # No optional acceleration or third-party math libraries (Intel IPP/ITT, OpenCL, LAPACK, Eigen, TBB, OpenMP).
    -D WITH_IPP=OFF -D WITH_ITT=OFF -D WITH_OPENCL=OFF -D WITH_OPENCLAMDFFT=OFF -D WITH_OPENCLAMDBLAS=OFF
    -D WITH_VA=OFF -D WITH_VA_INTEL=OFF -D WITH_LAPACK=OFF -D WITH_EIGEN=OFF -D WITH_TBB=OFF -D WITH_OPENMP=OFF
    -D WITH_CAROTENE=OFF -D WITH_KLEIDICV=OFF -D WITH_ADE=OFF -D WITH_QUIRC=OFF -D WITH_HDF5=OFF
    # Nothing downloaded at configure time: no Unicode font (imgproc) and no VGG/BoostDesc data (xfeatures2d).
    -D WITH_UNIFONT=OFF -D OPENCV_SKIP_FEATURES2D_DOWNLOADING=ON
)

cmake -G Ninja -S "$WORK/opencv" -B "$WORK/opencv-build" "${OPENCV_OPTIONS[@]}" | tee "$WORK/opencv-configure.log"
cmake --build "$WORK/opencv-build" -j "$JOBS"
cmake --install "$WORK/opencv-build" > /dev/null

# OpenCvSharpExtern against exactly that build: bindings of modules that were not built are compiled out.
EXTERN_OPTIONS=(
    -D CMAKE_BUILD_TYPE=Release
    -D OpenCV_DIR="$(dirname "$(find "$PREFIX" -name OpenCVConfig.cmake | head -1)")"
    -D CMAKE_INSTALL_RPATH='$ORIGIN' -D CMAKE_BUILD_WITH_INSTALL_RPATH=ON
    -D NO_CONTRIB=ON -D NO_VIDEOIO=ON -D NO_HIGHGUI=ON -D NO_STITCHING=ON
)
installed() { [ -n "$(find "$PREFIX" -name "libopencv_$1.so*" | head -1)" ]; }
for pair in calib:NO_CALIB stereo:NO_STEREO ptcloud:NO_PTCLOUD video:NO_VIDEO features:NO_FEATURES flann:NO_FLANN \
            ml:NO_ML photo:NO_PHOTO geometry:NO_GEOMETRY; do
    if ! installed "${pair%%:*}"; then
        EXTERN_OPTIONS+=(-D "${pair##*:}=ON")
    fi
done
cmake -G Ninja -S "$WORK/opencvsharp/src" -B "$WORK/extern-build" "${EXTERN_OPTIONS[@]}" | tee "$WORK/extern-configure.log"
cmake --build "$WORK/extern-build" -j "$JOBS"

EXTERN="$WORK/extern-build/OpenCvSharpExtern/libOpenCvSharpExtern.so"
cp "$EXTERN" "$OUT/"
# The license texts OpenCV installs for every third-party component compiled into these libraries
# (ocv_install_3rdparty_licenses), plus OpenCV's, OpenCV contrib's and OpenCvSharp's own.
mkdir -p "$OUT/licenses"
cp -r "$(find "$PREFIX" -type d -path '*/licenses/opencv*' | head -1)/." "$OUT/licenses/"
cp "$WORK/opencv/LICENSE" "$OUT/licenses/OpenCV-LICENSE"
cp "$WORK/opencv_contrib/LICENSE" "$OUT/licenses/OpenCV-contrib-LICENSE"
cp "$WORK/opencvsharp/LICENSE" "$OUT/licenses/OpenCvSharp-LICENSE"
# MLAS (Microsoft, MIT), vendored in OpenCV's dnn and compiled in, is not part of OpenCV's installed licenses.
if grep -Eq '^--[[:space:]]*DNN MLAS:[[:space:]]*YES' "$WORK/opencv-configure.log"; then
    cp "$WORK/opencv/3rdparty/mlas/LICENSE" "$OUT/licenses/mlas-LICENSE"
fi
# Every OpenCV shared library the extern loads, under the soname the loader asks for.
for needed in $(readelf -d "$EXTERN" | sed -n 's/.*Shared library: \[\(libopencv_[^]]*\)\]/\1/p'); do
    cp -L "$PREFIX/lib/$needed" "$OUT/$needed" 2>/dev/null || cp -L "$(find "$PREFIX" -name "$needed" | head -1)" "$OUT/$needed"
done
# ...and their own OpenCV dependencies.
for lib in "$OUT"/libopencv_*; do
    for needed in $(readelf -d "$lib" | sed -n 's/.*Shared library: \[\(libopencv_[^]]*\)\]/\1/p'); do
        [ -f "$OUT/$needed" ] || cp -L "$(find "$PREFIX" -name "$needed" | head -1)" "$OUT/$needed"
    done
done

# Build information of exactly these libraries.
cat > "$WORK/buildinfo.cpp" <<'EOF'
#include <opencv2/core.hpp>
#include <iostream>
int main() { std::cout << cv::getBuildInformation() << std::endl; }
EOF
g++ -std=c++17 "$WORK/buildinfo.cpp" -I"$(dirname "$(find "$PREFIX/include" -name opencv.hpp | head -1)")/.."     -L"$PREFIX/lib" -lopencv_core -Wl,-rpath,"$OUT" -o "$WORK/buildinfo"
"$WORK/buildinfo" > "$WORK/buildinfo.txt"

# Hard gate: none of these may be configured, linked or embedded.
fail=0
check_cache() { # option expected
    local value
    value=$(sed -n "s/^$1:[A-Z]*=//p" "$WORK/opencv-build/CMakeCache.txt")
    if [ "$value" != "$2" ]; then echo "GATE: $1=$value (expected $2)" >&2; fail=1; fi
}
for option in WITH_FFMPEG WITH_GSTREAMER WITH_V4L WITH_GTK WITH_QT BUILD_opencv_videoio BUILD_opencv_highgui WITH_IPP WITH_ITT; do
    check_cache "$option" OFF
done
# The modules OpenCV actually built: its configure summary (BUILD_opencv_* stays ON in the cache for a module disabled by
# dependency) and the installed libraries.
BUILT_MODULES=$(sed -n 's/^--[[:space:]]*To be built:[[:space:]]*//p' "$WORK/opencv-configure.log")
for module in videoio highgui text stitching; do
    if [[ " $BUILT_MODULES " == *" $module "* ]] || [ -n "$(find "$PREFIX" -name "libopencv_$module.so*" | head -1)" ]; then
        echo "GATE: module $module is built" >&2; fail=1
    fi
done
for lib in "$OUT"/*.so*; do
    if readelf -d "$lib" | grep -Ei 'NEEDED.*(avcodec|avformat|avutil|swscale|gstreamer|gtk|gdk|x11|xcb|tesseract|lept|jpeg|tiff|webp|openjp|tbb|gomp)'; then
        echo "GATE: unexpected dynamic dependency in $(basename "$lib")" >&2; fail=1
    fi
    if strings -a "$lib" | grep -Eiq 'libavcodec|avcodec_open|FFmpeg version|tesseract|leptonica|gst_init|gtk_init|XOpenDisplay'; then
        echo "GATE: FFmpeg/Tesseract/GStreamer/GTK/X11 code embedded in $(basename "$lib")" >&2; fail=1
    fi
done
if grep -Eiq '^\s*(FFMPEG|GStreamer|GTK\+|Tesseract|Built-in Unicode font):\s*YES' "$WORK/buildinfo.txt"; then
    echo "GATE: build information reports a forbidden component" >&2; fail=1
fi
# Nothing may have been fetched by OpenCV's CMake (every source is the pinned checkout).
if grep -q '^#cmake_download' "$WORK/opencv-build/CMakeDownloadLog.txt" 2>/dev/null; then
    echo "GATE: OpenCV CMake downloaded files:" >&2; grep '^#cmake_download' "$WORK/opencv-build/CMakeDownloadLog.txt" >&2; fail=1
fi
[ "$fail" -eq 0 ] || exit 1

{
    echo "SCI-PH minimal OpenCV runtime for linux-x64 (ADR-012)"
    echo "built-on: $(. /etc/os-release && echo "$PRETTY_NAME"), $(ldd --version | head -1), $(gcc --version | head -1), $(cmake --version | head -1)"
    echo "opencvsharp: https://github.com/shimat/opencvsharp @ $OPENCVSHARP_COMMIT"
    echo "opencv: https://github.com/opencv/opencv @ $OPENCV_COMMIT"
    echo "opencv_contrib: https://github.com/opencv/opencv_contrib @ $OPENCV_CONTRIB_COMMIT (modules: $CONTRIB_MODULES)"
    echo
    echo "== OpenCV CMake options"
    printf '%s\n' "${OPENCV_OPTIONS[@]}" | grep -v '^-D$' | paste -sd' ' | fold -w 160
    echo
    echo "== OpenCvSharpExtern CMake options"
    printf '%s\n' "${EXTERN_OPTIONS[@]}" | grep -v '^-D$' | paste -sd' '
    echo
    echo "== Modules built (OpenCV configure summary)"
    echo "To be built: $BUILT_MODULES"
    grep -E '^--[[:space:]]*(Disabled|Disabled by dependency|Unavailable):' "$WORK/opencv-configure.log" | sed 's/^--[[:space:]]*//'
    echo
    echo "== Bundled 3rdparty (statically linked into the OpenCV libraries)"
    grep -E '^\s*(ZLib|PNG|Protobuf|Flatbuffers|JPEG|TIFF|WEBP|OpenJPEG|Lapack|Eigen|IPP|ITT|DNN MLAS|Built-in Unicode font|Custom HAL|Parallel framework|Trace|Other third-party libraries)' "$WORK/buildinfo.txt" || true
    echo
    echo "== Third-party license files installed by OpenCV (licenses/)"
    (cd "$OUT/licenses" && ls -1)
    echo
    echo "== OpenCV CMake download log"
    cat "$WORK/opencv-build/CMakeDownloadLog.txt" 2>/dev/null || echo "(none)"
    echo
    echo "== Runtime files"
    for lib in "$OUT"/*.so*; do
        echo "$(sha256sum "$lib" | cut -d' ' -f1)  $(basename "$lib")"
        readelf -d "$lib" | sed -n 's/.*Shared library: \[\(.*\)\]/    NEEDED \1/p'
    done
    echo
    echo "== cv::getBuildInformation()"
    cat "$WORK/buildinfo.txt"
} > "$OUT/opencv-runtime-manifest.txt"

echo "Minimal OpenCV runtime ready in $OUT"
