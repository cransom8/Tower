Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)

function Get-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    foreach ($rawLine in Get-Content -LiteralPath $Path) {
        $line = [string]$rawLine
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=")
        if ($separatorIndex -lt 1) {
            continue
        }

        $name = $trimmed.Substring(0, $separatorIndex).Trim()
        if (-not $name.Equals($Key, [System.StringComparison]::Ordinal)) {
            continue
        }

        $value = $trimmed.Substring($separatorIndex + 1).Trim()
        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        return $value
    }

    return $null
}

$apiKey = $env:ELEVENLABS_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    foreach ($candidate in @(
        (Join-Path $repoRoot ".env.local"),
        (Join-Path $repoRoot ".env")
    )) {
        $apiKey = Get-DotEnvValue -Path $candidate -Key "ELEVENLABS_API_KEY"
        if (-not [string]::IsNullOrWhiteSpace($apiKey)) {
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw "ELEVENLABS_API_KEY was not found in the process environment, .env.local, or .env."
}

$env:ELEVENLABS_API_KEY = $apiKey
$env:ELEVENLABS_MCP_BASE_PATH = $repoRoot
$env:ELEVENLABS_MCP_OUTPUT_MODE = "files"

$uvxCommand = Get-Command uvx -ErrorAction Stop
& $uvxCommand.Source "elevenlabs-mcp"
exit $LASTEXITCODE
