param(
    [string]$Bucket = "castle-defender-assets",
    [string]$Project = "ransom-forge-game",
    [string]$SourceDir = "server/client/Build",
    [string]$GsutilPath = "C:\Users\Crans\AppData\Local\Google\Cloud SDK\google-cloud-sdk\bin\gsutil.cmd"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $SourceDir)) {
    throw "Build source directory not found: $SourceDir"
}

if (-not (Test-Path $GsutilPath)) {
    throw "gsutil not found at: $GsutilPath"
}

$bucketBuildPath = "gs://$Bucket/client/Build"
$cacheControl = "public, max-age=0, must-revalidate"

function Set-ObjectMetadata {
    param(
        [string]$ObjectPath,
        [string]$ContentType,
        [string]$ContentEncoding
    )

    $metadataArgs = @(
        "setmeta",
        "-h", "Content-Type:$ContentType",
        "-h", "Cache-Control:$cacheControl"
    )

    if (-not [string]::IsNullOrWhiteSpace($ContentEncoding)) {
        $metadataArgs += @("-h", "Content-Encoding:$ContentEncoding")
    }

    $metadataArgs += $ObjectPath
    & $GsutilPath @metadataArgs
}

$files = Get-ChildItem -Path $SourceDir -File
if ($files.Count -eq 0) {
    throw "No files found under $SourceDir"
}

Write-Host "Uploading WebGL build files from $SourceDir to $bucketBuildPath"
& $GsutilPath -m cp "$SourceDir/*" "$bucketBuildPath/"

foreach ($file in $files) {
    $objectPath = "$bucketBuildPath/$($file.Name)"

    switch -Wildcard ($file.Name) {
        "*.framework.js.br" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/javascript" -ContentEncoding "br"
            continue
        }
        "*.wasm.br" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/wasm" -ContentEncoding "br"
            continue
        }
        "*.data.br" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/octet-stream" -ContentEncoding "br"
            continue
        }
        "*.framework.js.unityweb" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/javascript" -ContentEncoding "br"
            continue
        }
        "*.wasm.unityweb" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/wasm" -ContentEncoding "br"
            continue
        }
        "*.data.unityweb" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/octet-stream" -ContentEncoding "br"
            continue
        }
        "*.loader.js" {
            Set-ObjectMetadata -ObjectPath $objectPath -ContentType "application/javascript" -ContentEncoding ""
            continue
        }
    }
}

Write-Host ""
Write-Host "Upload complete."
Write-Host "Build URL: https://storage.googleapis.com/$Bucket/client/Build/"
