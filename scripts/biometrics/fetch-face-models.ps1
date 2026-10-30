<#
.SYNOPSIS
  Downloads the pinned face models of the local OpenCV provider (ADR-012) and verifies their SHA-256.

.DESCRIPTION
  YuNet face_detection_yunet_2023mar.onnx (MIT) and SFace face_recognition_sface_2021dec.onnx (Apache-2.0), from the
  pinned OpenCV Zoo URLs on Hugging Face. Each file is downloaded to a temporary file next to the destination, its SHA-256
  is checked against the pinned value, and only then is it moved into place. A mismatch deletes the download and fails.
  A model already present with the right hash is kept. The models are NEVER committed (.models/ is ignored) and the Web BFF
  never downloads them by itself: run this script before using FaceVerification:Provider=OpenCv.
  Keep the URLs and hashes in sync with src/Infrastructure/SCI-PH.Infrastructure/Biometrics/Face/OpenCv/FaceModels.cs.

.PARAMETER Destination
  Folder for the models. Default: .models/face at the repository root.
#>
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\..\.models\face')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$models = @(
    @{
        Name   = 'face_detection_yunet_2023mar.onnx'
        Url    = 'https://huggingface.co/opencv/face_detection_yunet/resolve/main/face_detection_yunet_2023mar.onnx'
        Sha256 = '8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4'
    },
    @{
        Name   = 'face_recognition_sface_2021dec.onnx'
        Url    = 'https://huggingface.co/opencv/face_recognition_sface/resolve/main/face_recognition_sface_2021dec.onnx'
        Sha256 = '0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79'
    }
)

function Get-Sha256([string]$Path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$Destination = (Resolve-Path -LiteralPath $Destination).Path

foreach ($model in $models) {
    $target = Join-Path $Destination $model.Name
    if ((Test-Path -LiteralPath $target) -and ((Get-Sha256 $target) -eq $model.Sha256)) {
        Write-Host "OK      $($model.Name) (already present, SHA-256 verified)"
        continue
    }

    $temporary = Join-Path $Destination ".$($model.Name).$([guid]::NewGuid().ToString('N')).download"
    try {
        Write-Host "GET     $($model.Url)"
        Invoke-WebRequest -Uri $model.Url -OutFile $temporary -UseBasicParsing
        $actual = Get-Sha256 $temporary
        if ($actual -ne $model.Sha256) {
            throw "SHA-256 mismatch for $($model.Name): expected $($model.Sha256), got $actual. Nothing was installed."
        }

        Move-Item -LiteralPath $temporary -Destination $target -Force
        Write-Host "OK      $($model.Name) (SHA-256 verified)"
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

Write-Host "Face models ready in $Destination"
