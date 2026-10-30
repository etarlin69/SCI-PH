<#
.SYNOPSIS
  Downloads the Face Synthetics sample used ONLY by the opt-in OpenCV face tests (SCIPH_FACE_MODELS=1), and verifies it.

.DESCRIPTION
  Microsoft Face Synthetics, dataset_100.zip (100 synthetic, rendered faces; no real person). Licensed under the Microsoft
  Research Use of Data Agreement v1.0 (non-commercial research): for local or manual academic testing only. It is NEVER
  committed, redistributed, put in a Docker image or in a normal CI artifact; .fixtures/ is ignored. If SCI-PH is
  commercialized this source must be replaced. See docs/legal/face-opencv-third-party.md.
  The zip is downloaded to a temporary file, its SHA-256 is checked, and only then is it moved and extracted.

.PARAMETER Destination
  Folder for the sample. Default: .fixtures/face-synthetics at the repository root.
#>
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\.fixtures\face-synthetics')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$url = 'https://facesyntheticspubwedata.z6.web.core.windows.net/iccv-2021/dataset_100.zip'
$sha256 = 'df6eed9e10a2511c81467a0f9d0c762ef609e3a073978827aa0e605c71e0ab99'

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path
$zip = Join-Path $Destination 'dataset_100.zip'

if (-not ((Test-Path -LiteralPath $zip) -and ((Get-FileHash -Algorithm SHA256 -LiteralPath $zip).Hash.ToLowerInvariant() -eq $sha256))) {
    $temporary = Join-Path $Destination ".dataset_100.$([guid]::NewGuid().ToString('N')).download"
    try {
        Write-Host "GET     $url"
        Invoke-WebRequest -Uri $url -OutFile $temporary -UseBasicParsing
        $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $temporary).Hash.ToLowerInvariant()
        if ($actual -ne $sha256) {
            throw "SHA-256 mismatch for dataset_100.zip: expected $sha256, got $actual. Nothing was installed."
        }

        Move-Item -LiteralPath $temporary -Destination $zip -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

Expand-Archive -LiteralPath $zip -DestinationPath $Destination -Force
Write-Host "Face Synthetics sample ready in $Destination (research use only; never commit or redistribute)."
