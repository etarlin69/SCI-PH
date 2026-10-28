#!/usr/bin/env sh
# Downloads the pinned face models of the local OpenCV provider (ADR-012) and verifies their SHA-256.
#
# YuNet face_detection_yunet_2023mar.onnx (MIT) and SFace face_recognition_sface_2021dec.onnx (Apache-2.0), from the pinned
# OpenCV Zoo URLs on Hugging Face. Each file is downloaded to a temporary file next to the destination, its SHA-256 is
# checked against the pinned value, and only then is it moved into place; a mismatch deletes the download and fails. A model
# already present with the right hash is kept. The models are NEVER committed (.models/ is ignored) and the Web BFF never
# downloads them by itself. Also used by the Web BFF Dockerfile's model stage.
# Keep the URLs and hashes in sync with src/Infrastructure/SCI-PH.Infrastructure/Biometrics/Face/OpenCv/FaceModels.cs.
#
# Usage: scripts/biometrics/fetch-face-models.sh [destination]   (default: .models/face at the repository root)
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
destination=${1:-"$root/.models/face"}
mkdir -p "$destination"

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

fetch() {
    name=$1
    url=$2
    expected=$3
    target="$destination/$name"
    if [ -f "$target" ] && [ "$(sha256 "$target")" = "$expected" ]; then
        echo "OK      $name (already present, SHA-256 verified)"
        return 0
    fi

    temporary=$(mktemp "$destination/.$name.XXXXXX")
    trap 'rm -f "$temporary"' EXIT INT TERM
    echo "GET     $url"
    curl -fsSL --retry 3 --proto '=https' --tlsv1.2 -o "$temporary" "$url"
    actual=$(sha256 "$temporary")
    if [ "$actual" != "$expected" ]; then
        rm -f "$temporary"
        echo "SHA-256 mismatch for $name: expected $expected, got $actual. Nothing was installed." >&2
        exit 1
    fi

    chmod 0644 "$temporary"
    mv -f "$temporary" "$target"
    trap - EXIT INT TERM
    echo "OK      $name (SHA-256 verified)"
}

fetch face_detection_yunet_2023mar.onnx \
    https://huggingface.co/opencv/face_detection_yunet/resolve/main/face_detection_yunet_2023mar.onnx \
    8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4
fetch face_recognition_sface_2021dec.onnx \
    https://huggingface.co/opencv/face_recognition_sface/resolve/main/face_recognition_sface_2021dec.onnx \
    0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79

echo "Face models ready in $destination"
