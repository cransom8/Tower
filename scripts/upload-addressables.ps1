param(
    [ValidateSet("Android", "WebGL")]
    [string]$Platform = "Android",
    [string]$Bucket,
    [string]$SourceDir,
    [switch]$StageRailwayMetadata
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Resolve-WorkspacePath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path cannot be empty."
    }

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

function Load-LocalSettings {
    param([string]$EnvFilePath)

    $settings = @{}
    if (-not (Test-Path -LiteralPath $EnvFilePath)) {
        return $settings
    }

    foreach ($rawLine in Get-Content -LiteralPath $EnvFilePath) {
        if ([string]::IsNullOrWhiteSpace($rawLine)) {
            continue
        }

        $trimmed = $rawLine.Trim()
        if ($trimmed.StartsWith('#')) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf('=')
        if ($separatorIndex -le 0) {
            continue
        }

        $key = $trimmed.Substring(0, $separatorIndex).Trim()
        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        $settings[$key] = $value
    }

    return $settings
}

function Read-Setting {
    param(
        [string[]]$Names,
        [string]$DefaultValue = ""
    )

    foreach ($name in $Names) {
        $envValue = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($envValue)) {
            return $envValue.Trim()
        }

        if ($localSettings.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace($localSettings[$name])) {
            return $localSettings[$name].Trim()
        }
    }

    return $DefaultValue
}

function Resolve-GcloudPath {
    $defaultSdkPath = "C:\Users\Crans\AppData\Local\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd"
    if (Test-Path -LiteralPath $defaultSdkPath) {
        return $defaultSdkPath
    }

    $gcloudCommand = Get-Command "gcloud" -ErrorAction SilentlyContinue
    if ($null -ne $gcloudCommand) {
        return $gcloudCommand.Source
    }

    throw "gcloud was not found. Install the Google Cloud SDK or add gcloud to PATH."
}

function Get-AccessToken {
    $token = & $resolvedGcloudPath auth print-access-token
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
        throw "Failed to obtain a Google Cloud access token."
    }

    return $token.Trim()
}

function Get-EncodedObjectName {
    param([string]$ObjectName)

    return [System.Uri]::EscapeDataString($ObjectName)
}

function Upload-Object {
    param(
        [string]$FilePath,
        [string]$ObjectName,
        [string]$ContentType,
        [string]$CacheControl
    )

    $encodedObjectName = Get-EncodedObjectName $ObjectName
    $uploadUrl = "https://storage.googleapis.com/upload/storage/v1/b/$Bucket/o?uploadType=media&name=$encodedObjectName"
    $metadataUrl = "https://storage.googleapis.com/storage/v1/b/$Bucket/o/$encodedObjectName"

    & curl.exe -4 --silent --show-error --fail `
        --connect-timeout 20 `
        --max-time 1800 `
        -X POST `
        -H "Authorization: Bearer $accessToken" `
        -H "Content-Type: $ContentType" `
        --data-binary "@$FilePath" `
        $uploadUrl | Out-Null

    $metadataBody = @{ cacheControl = $CacheControl; contentType = $ContentType } | ConvertTo-Json -Compress
    $tempMetadataPath = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tempMetadataPath, $metadataBody, $utf8NoBom)
        & curl.exe -4 --silent --show-error --fail `
            --connect-timeout 20 `
            --max-time 120 `
            -X PATCH `
            -H "Authorization: Bearer $accessToken" `
            -H "Content-Type: application/json" `
            --data-binary "@$tempMetadataPath" `
            $metadataUrl | Out-Null
    }
    finally {
        if (Test-Path -LiteralPath $tempMetadataPath) {
            Remove-Item -LiteralPath $tempMetadataPath -Force
        }
    }
}

function Get-RepoRelativePath {
    param([string]$AbsolutePath)

    $repoUri = [System.Uri]((Resolve-Path -LiteralPath $repoRoot).Path.TrimEnd('\') + '\')
    $fileUri = [System.Uri]((Resolve-Path -LiteralPath $AbsolutePath).Path)
    return [System.Uri]::UnescapeDataString($repoUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$localSettings = Load-LocalSettings (Join-Path $repoRoot ".local-secrets\forge-wars-upload.env")

if ([string]::IsNullOrWhiteSpace($SourceDir)) {
    $SourceDir = "unity-client/ServerData/$Platform"
}

if ([string]::IsNullOrWhiteSpace($Bucket)) {
    $Bucket = Read-Setting -Names @("ADDRESSABLES_GCS_BUCKET", "GCS_BUCKET") -DefaultValue "castle-defender-assets"
}

$resolvedSourceDir = Resolve-WorkspacePath $SourceDir
if (-not (Test-IsSubPath -CandidatePath $resolvedSourceDir -RootPath $repoRoot)) {
    throw "Addressables source directory must stay inside repo root '$repoRoot'. Resolved path: $resolvedSourceDir"
}

if (-not (Test-Path -LiteralPath $resolvedSourceDir)) {
    throw "Addressables source directory not found: $resolvedSourceDir"
}

$resolvedGcloudPath = Resolve-GcloudPath
$accessToken = Get-AccessToken

$bucketAddressablesPath = "gs://$Bucket/addressables/$Platform"
$catalogFiles = @("catalog.bin", "catalog.hash", "catalog_1.0.bin", "catalog_1.0.hash", "settings.json")
$metadataFiles = @()

Write-Host "Uploading $Platform addressables from $resolvedSourceDir"
Write-Host "Destination: $bucketAddressablesPath"

foreach ($catalogFile in $catalogFiles) {
    $sourcePath = Join-Path $resolvedSourceDir $catalogFile
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        continue
    }

    $metadataFiles += $sourcePath
    Write-Host "Uploading metadata: $catalogFile"
    $contentType = if ($catalogFile -eq "settings.json") {
        "application/json"
    }
    elseif ($catalogFile -like "*.hash") {
        "text/plain"
    }
    else {
        "application/octet-stream"
    }
    Upload-Object -FilePath $sourcePath -ObjectName "addressables/$Platform/$catalogFile" -ContentType $contentType -CacheControl "public, max-age=0, must-revalidate"
}

if ($metadataFiles.Count -eq 0) {
    throw "No addressables catalog or settings files were found in $resolvedSourceDir"
}

$bundleFiles = Get-ChildItem -LiteralPath $resolvedSourceDir -File -Filter *.bundle
if ($bundleFiles.Count -eq 0) {
    throw "No .bundle files were found in $resolvedSourceDir"
}

foreach ($bundleFile in $bundleFiles) {
    Write-Host "Uploading bundle: $($bundleFile.Name)"
    Upload-Object -FilePath $bundleFile.FullName -ObjectName "addressables/$Platform/$($bundleFile.Name)" -ContentType "application/octet-stream" -CacheControl "public, max-age=31536000, immutable"
}

Write-Host "Uploaded $($metadataFiles.Count) metadata file(s) and $($bundleFiles.Count) bundle(s)."

if ($StageRailwayMetadata.IsPresent) {
    $relativeMetadataFiles = @()
    foreach ($metadataFile in $metadataFiles) {
        $relativeMetadataFiles += Get-RepoRelativePath $metadataFile
    }

    & git -C $repoRoot add -- @relativeMetadataFiles
    Write-Host "Staged Railway metadata files:"
    foreach ($relativePath in $relativeMetadataFiles) {
        Write-Host "  $relativePath"
    }

    Write-Host ""
    & git -C $repoRoot status --short -- @relativeMetadataFiles
}

Write-Host ""
Write-Host "Addressables upload complete."
Write-Host "GCS URL: https://storage.googleapis.com/$Bucket/addressables/$Platform/"
