param(
    [ValidateSet("Android")]
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

function Publish-UploadProgress {
    param(
        [string]$Phase,
        [long]$ProcessedBytes,
        [long]$TotalBytes,
        [string]$CurrentItem
    )

    $safeItemName = if ([string]::IsNullOrWhiteSpace($CurrentItem)) {
        ""
    }
    else {
        $CurrentItem.Replace('|', '/')
    }

    $clampedTotalBytes = [Math]::Max(1L, $TotalBytes)
    $percentComplete = [int][Math]::Floor(([double]$ProcessedBytes / [double]$clampedTotalBytes) * 100.0)
    $status = if ([string]::IsNullOrWhiteSpace($safeItemName)) {
        "$ProcessedBytes / $TotalBytes bytes"
    }
    else {
        "$safeItemName ($ProcessedBytes / $TotalBytes bytes)"
    }

    Write-Progress -Id 1 -Activity "Uploading $Platform addressables" -Status $status -PercentComplete $percentComplete
    Write-Output "##UPLOAD_PROGRESS|$Phase|$ProcessedBytes|$TotalBytes|$safeItemName"
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
}

if ($metadataFiles.Count -eq 0) {
    throw "No addressables catalog or settings files were found in $resolvedSourceDir"
}

$bundleFiles = Get-ChildItem -LiteralPath $resolvedSourceDir -File -Filter *.bundle
if ($bundleFiles.Count -eq 0) {
    throw "No .bundle files were found in $resolvedSourceDir"
}

$loginCinematicsDir = Resolve-WorkspacePath "unity-client/Assets/AddressableContent/LoginCinematics"
$loginCinematicFiles = @()
if (Test-Path -LiteralPath $loginCinematicsDir) {
    $loginCinematicFiles = @(Get-ChildItem -LiteralPath $loginCinematicsDir -File -Filter *.mp4)
}
else {
    Write-Host "Login cinematics directory not found; skipping video upload: $loginCinematicsDir"
}

$uploadItems = @()
foreach ($metadataFile in $metadataFiles) {
    $metadataFileName = [System.IO.Path]::GetFileName($metadataFile)
    $metadataContentType = if ($metadataFileName -eq "settings.json") {
        "application/json"
    }
    elseif ($metadataFileName -like "*.hash") {
        "text/plain"
    }
    else {
        "application/octet-stream"
    }

    $uploadItems += [PSCustomObject]@{
        Kind = "metadata"
        Label = "metadata: $metadataFileName"
        FileName = $metadataFileName
        FilePath = $metadataFile
        ObjectName = "addressables/$Platform/$metadataFileName"
        ContentType = $metadataContentType
        CacheControl = "public, max-age=0, must-revalidate"
        SizeBytes = [int64](Get-Item -LiteralPath $metadataFile).Length
    }
}

foreach ($bundleFile in $bundleFiles) {
    $uploadItems += [PSCustomObject]@{
        Kind = "bundle"
        Label = "bundle: $($bundleFile.Name)"
        FileName = $bundleFile.Name
        FilePath = $bundleFile.FullName
        ObjectName = "addressables/$Platform/$($bundleFile.Name)"
        ContentType = "application/octet-stream"
        CacheControl = "public, max-age=31536000, immutable"
        SizeBytes = [int64]$bundleFile.Length
    }
}

foreach ($loginCinematicFile in $loginCinematicFiles) {
    $uploadItems += [PSCustomObject]@{
        Kind = "login-cinematic"
        Label = "login cinematic: $($loginCinematicFile.Name)"
        FileName = $loginCinematicFile.Name
        FilePath = $loginCinematicFile.FullName
        ObjectName = "addressables/LoginCinematics/$($loginCinematicFile.Name)"
        ContentType = "video/mp4"
        CacheControl = "public, max-age=31536000, immutable"
        SizeBytes = [int64]$loginCinematicFile.Length
    }
}

[long]$totalUploadBytes = ($uploadItems | Measure-Object -Property SizeBytes -Sum).Sum
[long]$processedUploadBytes = 0L
Publish-UploadProgress -Phase "start" -ProcessedBytes $processedUploadBytes -TotalBytes $totalUploadBytes -CurrentItem "starting"

foreach ($uploadItem in $uploadItems) {
    Publish-UploadProgress -Phase "file-start" -ProcessedBytes $processedUploadBytes -TotalBytes $totalUploadBytes -CurrentItem $uploadItem.FileName
    Write-Host "Uploading $($uploadItem.Label)"
    Upload-Object -FilePath $uploadItem.FilePath -ObjectName $uploadItem.ObjectName -ContentType $uploadItem.ContentType -CacheControl $uploadItem.CacheControl
    $processedUploadBytes += $uploadItem.SizeBytes
    Publish-UploadProgress -Phase "file-complete" -ProcessedBytes $processedUploadBytes -TotalBytes $totalUploadBytes -CurrentItem $uploadItem.FileName
}

Write-Progress -Id 1 -Activity "Uploading $Platform addressables" -Completed
Write-Output "##UPLOAD_PROGRESS|end|$processedUploadBytes|$totalUploadBytes|complete"

Write-Host "Uploaded $($metadataFiles.Count) metadata file(s) and $($bundleFiles.Count) bundle(s)."
Write-Host "Uploaded $($loginCinematicFiles.Count) login cinematic file(s)."

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
