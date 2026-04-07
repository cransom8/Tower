[CmdletBinding()]
param(
    [string]$Project,
    [string]$Region = "us-central1",
    [string]$Service = "castle-defender",
    [string]$Source = ".",
    [string]$EnvFile = "deploy/cloudrun.env",
    [string[]]$Secret = @(),
    [string]$CloudSqlInstance,
    [string]$ServiceAccount,
    [int]$Cpu = 2,
    [string]$Memory = "2Gi",
    [int]$MinInstances = 1,
    [int]$MaxInstances = 1,
    [int]$Concurrency = 250,
    [string]$Timeout = "3600s",
    [switch]$NoTraffic
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

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

function Resolve-GcloudPath {
    $defaultSdkPath = "C:\Users\Crans\AppData\Local\Google\Cloud SDK\google-cloud-sdk\bin\gcloud.cmd"
    if (Test-Path -LiteralPath $defaultSdkPath) {
        return $defaultSdkPath
    }

    $gcloudCommand = Get-Command "gcloud.cmd" -ErrorAction SilentlyContinue
    if ($null -ne $gcloudCommand) {
        return $gcloudCommand.Source
    }

    throw "gcloud.cmd was not found. Install the Google Cloud SDK or add it to PATH."
}

function Get-DefaultProject {
    $configuredProject = & $resolvedGcloudPath config get-value core/project 2>$null
    if ($LASTEXITCODE -ne 0) {
        return ""
    }

    return ($configuredProject | Select-Object -First 1).Trim()
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedGcloudPath = Resolve-GcloudPath

if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Get-DefaultProject
}

if ([string]::IsNullOrWhiteSpace($Project)) {
    throw "Google Cloud project not set. Pass -Project or configure one with gcloud config set project."
}

$resolvedSource = Resolve-WorkspacePath $Source
if (-not (Test-Path -LiteralPath $resolvedSource)) {
    throw "Source path not found: $resolvedSource"
}

$resolvedEnvFile = Resolve-WorkspacePath $EnvFile
if (-not (Test-Path -LiteralPath $resolvedEnvFile)) {
    throw "Cloud Run env file not found: $resolvedEnvFile. Copy deploy/cloudrun.env.example to deploy/cloudrun.env and fill in the non-secret values."
}

$arguments = @(
    "run",
    "deploy",
    $Service,
    "--source=$resolvedSource",
    "--project=$Project",
    "--region=$Region",
    "--platform=managed",
    "--allow-unauthenticated",
    "--execution-environment=gen2",
    "--port=8080",
    "--cpu=$Cpu",
    "--memory=$Memory",
    "--concurrency=$Concurrency",
    "--min=$MinInstances",
    "--max=$MaxInstances",
    "--timeout=$Timeout",
    "--cpu-boost",
    "--no-cpu-throttling",
    "--env-vars-file=$resolvedEnvFile",
    "--startup-probe=httpGet.path=/health,httpGet.port=8080,timeoutSeconds=5,periodSeconds=10,failureThreshold=6"
)

if ($NoTraffic.IsPresent) {
    $arguments += "--no-traffic"
}

if (-not [string]::IsNullOrWhiteSpace($ServiceAccount)) {
    $arguments += "--service-account=$ServiceAccount"
}

if (-not [string]::IsNullOrWhiteSpace($CloudSqlInstance)) {
    $arguments += "--set-cloudsql-instances=$CloudSqlInstance"
}

if ($Secret.Count -gt 0) {
    $arguments += "--update-secrets=$([string]::Join(',', $Secret))"
}

Write-Host "Deploying Cloud Run service '$Service'"
Write-Host "  project:      $Project"
Write-Host "  region:       $Region"
Write-Host "  source:       $resolvedSource"
Write-Host "  env file:     $resolvedEnvFile"
Write-Host "  cpu / memory: $Cpu / $Memory"
Write-Host "  scaling:      min=$MinInstances max=$MaxInstances"
Write-Host "  concurrency:  $Concurrency"
if ($Secret.Count -gt 0) {
    Write-Host "  secrets:      $($Secret.Count) mapping(s)"
} else {
    Write-Host "  secrets:      none supplied by script"
}

Write-Host ""
Write-Host "This service owns live rooms, parties, reconnect tokens, and active matches in memory."
Write-Host "Keep Cloud Run pinned to a single instance until runtime state is externalized."
Write-Host ""

& $resolvedGcloudPath @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Cloud Run deploy failed."
}

$describeArgs = @(
    "run",
    "services",
    "describe",
    $Service,
    "--project=$Project",
    "--region=$Region",
    "--format=value(status.url)"
)
$serviceUrl = (& $resolvedGcloudPath @describeArgs | Select-Object -First 1).Trim()

Write-Host ""
Write-Host "Cloud Run deploy complete."
if (-not [string]::IsNullOrWhiteSpace($serviceUrl)) {
    Write-Host "Service URL: $serviceUrl"
}
