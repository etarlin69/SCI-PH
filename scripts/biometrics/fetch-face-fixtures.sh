#!/usr/bin/env sh
# Downloads the Face Synthetics sample used ONLY by the opt-in OpenCV face tests (SCIPH_FACE_MODELS=1), and verifies it.
#
# Microsoft Face Synthetics, dataset_100.zip (100 synthetic, rendered faces; no real person). Licensed under the Microsoft
# Research Use of Data Agreement v1.0 (non-commercial research): for local or manual academic testing only. It is NEVER
# committed, redistributed, put in a Docker image or in a normal CI artifact; .fixtures/ is ignored. If SCI-PH is
# commercialized this source must be replaced. See docs/legal/face-opencv-third-party.md.
#
# Usage: scripts/biometrics/fetch-face-fixtures.sh [destination]   (default: .fixtures/face-synthetics at the repository root)
set -eu

root=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
destination=${1:-"$root/.fixtures/face-synthetics"}
url=https://facesyntheticspubwedata.z6.web.core.windows.net/iccv-2021/dataset_100.zip
expected=df6eed9e10a2511c81467a0f9d0c762ef609e3a073978827aa0e605c71e0ab99
mkdir -p "$destination"
zip="$destination/dataset_100.zip"

sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

if ! { [ -f "$zip" ] && [ "$(sha256 "$zip")" = "$expected" ]; }; then
    temporary=$(mktemp "$destination/.dataset_100.XXXXXX")
    trap 'rm -f "$temporary"' EXIT INT TERM
    echo "GET     $url"
    curl -fsSL --retry 3 --proto '=https' --tlsv1.2 -o "$temporary" "$url"
    actual=$(sha256 "$temporary")
    if [ "$actual" != "$expected" ]; then
        echo "SHA-256 mismatch for dataset_100.zip: expected $expected, got $actual. Nothing was installed." >&2
        exit 1
    fi

    mv -f "$temporary" "$zip"
    trap - EXIT INT TERM
fi

unzip -oq "$zip" -d "$destination"
echo "Face Synthetics sample ready in $destination (research use only; never commit or redistribute)."
