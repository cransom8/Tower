param(
    [string]$Bucket = "castle-defender-assets",
    [string]$Project = "ransom-forge-game",
    [string]$SourceDir = "server/client/Build",
    [string]$GsutilPath = "C:\Users\Crans\AppData\Local\Google\Cloud SDK\google-cloud-sdk\bin\gsutil.cmd"
)

$ErrorActionPreference = "Stop"

function Resolve-WorkspacePath {
    param([string]$PathValue)

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

function Test-IsSubPath {
    param(
        [string]$CandidatePath,
        [string]$RootPath
    )

    $normalizedCandidate = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $normalizedRoot = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    return $normalizedCandidate.StartsWith($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedSourceDir = Resolve-WorkspacePath $SourceDir
$resolvedGsutilPath = Resolve-WorkspacePath $GsutilPath

if (-not (Test-IsSubPath -CandidatePath $resolvedSourceDir -RootPath $repoRoot)) {
    throw "Build source directory must stay inside repo root '$repoRoot'. Resolved path: $resolvedSourceDir"
}

if (-not (Test-Path $resolvedSourceDir)) {
    throw "Build source directory not found: $resolvedSourceDir"
}

if (-not (Test-Path $resolvedGsutilPath)) {
    throw "gsutil not found at: $resolvedGsutilPath"
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
    & $resolvedGsutilPath @metadataArgs
}

$files = Get-ChildItem -Path $resolvedSourceDir -File
if ($files.Count -eq 0) {
    throw "No files found under $resolvedSourceDir"
}

Write-Host "Uploading WebGL build files from $resolvedSourceDir to $bucketBuildPath"
& $resolvedGsutilPath -m cp "$resolvedSourceDir/*" "$bucketBuildPath/"

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
