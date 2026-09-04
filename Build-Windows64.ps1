param(
    [string]$ProjectPath = $PSScriptRoot,
    [string]$UnityPath = "",
    [string]$LogPath = ""
)

$ErrorActionPreference = "Stop"

function Get-InputFiles([string]$projectFullPath)
{
    $inputPaths = @(
        (Join-Path $projectFullPath "Assets\Scripts"),
        (Join-Path $projectFullPath "Assets\EditorTools"),
        (Join-Path $projectFullPath "Assets\Scenes\bootstrapper.unity"),
        (Join-Path $projectFullPath "Assets\Scenes\empty.unity"),
        (Join-Path $projectFullPath "Packages\manifest.json"),
        (Join-Path $projectFullPath "ProjectSettings\ProjectVersion.txt")
    )

    $files = @()
    foreach ($path in $inputPaths)
    {
        if (Test-Path -LiteralPath $path -PathType Container)
        {
            $files += Get-ChildItem -LiteralPath $path -File -Recurse
        }
        elseif (Test-Path -LiteralPath $path -PathType Leaf)
        {
            $files += Get-Item -LiteralPath $path
        }
    }

    return $files | Sort-Object FullName -Unique
}

function Get-SourceFingerprint([string]$projectFullPath)
{
    $files = Get-InputFiles $projectFullPath
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        foreach ($file in $files)
        {
            $relativePath = [System.IO.Path]::GetRelativePath($projectFullPath, $file.FullName)
            $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($relativePath.ToLowerInvariant() + "`n")
            $sha256.TransformBlock($pathBytes, 0, $pathBytes.Length, $pathBytes, 0) | Out-Null

            $contentBytes = [System.IO.File]::ReadAllBytes($file.FullName)
            $sha256.TransformBlock($contentBytes, 0, $contentBytes.Length, $contentBytes, 0) | Out-Null
        }

        $sha256.TransformFinalBlock($pathBytes, 0, 0) | Out-Null
        return [System.BitConverter]::ToString($sha256.Hash).Replace("-", "").ToLowerInvariant()
    }
    finally
    {
        $sha256.Dispose()
    }
}

function Get-GitValue
{
    param([string[]]$GitArguments)
    $output = & git @GitArguments 2>$null
    if ($LASTEXITCODE -eq 0)
    {
        return ($output | Select-Object -First 1)
    }
    return $null
}

function Find-UnityExecutable([string]$projectFullPath, [string]$requestedUnityPath)
{
    if (-not [string]::IsNullOrWhiteSpace($requestedUnityPath))
    {
        if (Test-Path -LiteralPath $requestedUnityPath -PathType Leaf)
        {
            return (Resolve-Path -LiteralPath $requestedUnityPath).ProviderPath
        }
        throw "Unity executable not found: $requestedUnityPath"
    }

    $versionFile = Join-Path $projectFullPath "ProjectSettings\ProjectVersion.txt"
    $versionLine = Get-Content -LiteralPath $versionFile | Where-Object { $_.StartsWith("m_EditorVersion:") } | Select-Object -First 1
    if (-not $versionLine)
    {
        throw "Unable to read Unity version from $versionFile"
    }

    $unityVersion = $versionLine.Substring("m_EditorVersion:".Length).Trim()
    $searchRoots = @(
        (Join-Path $env:ProgramFiles "Unity\Hub\Editor"),
        (Join-Path ${env:ProgramFiles(x86)} "Unity\Hub\Editor"),
        "D:\Unity",
        (Join-Path $env:ProgramFiles "Unity")
    )

    foreach ($searchRoot in $searchRoots)
    {
        if (-not (Test-Path -LiteralPath $searchRoot -PathType Container))
        {
            continue
        }

        $candidate = Join-Path $searchRoot "$unityVersion\Editor\Unity.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf)
        {
            return (Resolve-Path -LiteralPath $candidate).ProviderPath
        }
    }

    throw "Unity $unityVersion was not found. Use -UnityPath to specify Unity.exe."
}

$projectFullPath = (Resolve-Path -LiteralPath $ProjectPath).ProviderPath
$unityExecutable = Find-UnityExecutable $projectFullPath $UnityPath
$buildStartedUtc = [System.DateTime]::UtcNow
$sourceFingerprintBefore = Get-SourceFingerprint $projectFullPath

if ([string]::IsNullOrWhiteSpace($LogPath))
{
    $LogPath = Join-Path $projectFullPath "Temp\windows64-build.log"
}

$logDirectory = New-Item -ItemType Directory -Path (Split-Path -Parent $LogPath) -Force
$logParent = (Resolve-Path -LiteralPath (Split-Path -Parent $LogPath)).ProviderPath
$logFullPath = Join-Path $logParent (Split-Path -Leaf $LogPath)
$executablePath = Join-Path $projectFullPath "Build\Windows64\FPSSample.exe"
$assemblyPath = Join-Path $projectFullPath "Build\Windows64\FPSSample_Data\Managed\Assembly-CSharp.dll"
$buildInfoPath = Join-Path $projectFullPath "Build\Windows64\BuildInfo.json"
$verificationPath = Join-Path $projectFullPath "Build\Windows64\BuildVerification.json"

Write-Host "Building Windows64 player with $unityExecutable"
Write-Host "Build started at UTC: $($buildStartedUtc.ToString('o'))"

$arguments = @(
    "-batchmode",
    "-quit",
    "-nographics",
    "-projectPath", $projectFullPath,
    "-executeMethod", "SimpleBuild.BuildWindows64",
    "-logFile", $logFullPath
)

$process = Start-Process -FilePath $unityExecutable -ArgumentList $arguments -PassThru -WindowStyle Hidden
while (-not $process.HasExited)
{
    Start-Sleep -Seconds 2
}

$unityExitCode = $process.ExitCode
if ($unityExitCode -ne 0)
{
    if (Test-Path -LiteralPath $logFullPath)
    {
        Get-Content -LiteralPath $logFullPath -Tail 120
    }
    throw "Unity build failed with exit code $unityExitCode."
}

foreach ($requiredPath in @($executablePath, $assemblyPath, $buildInfoPath))
{
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf))
    {
        throw "Required build output is missing: $requiredPath"
    }
}

$executableItem = Get-Item -LiteralPath $executablePath
$assemblyItem = Get-Item -LiteralPath $assemblyPath
$buildInfo = Get-Content -LiteralPath $buildInfoPath -Raw | ConvertFrom-Json

if ($executableItem.LastWriteTimeUtc -lt $buildStartedUtc)
{
    throw "Timestamp verification failed: FPSSample.exe was not updated by this build."
}

if ($assemblyItem.LastWriteTimeUtc -lt $buildStartedUtc)
{
    throw "Timestamp verification failed: Assembly-CSharp.dll was not updated by this build."
}

if (-not $buildInfo.timestampVerified)
{
    throw "BuildInfo.json does not have a verified Unity-side timestamp check."
}

$sourceFingerprintAfter = Get-SourceFingerprint $projectFullPath
if ($sourceFingerprintBefore -ne $sourceFingerprintAfter)
{
    throw "Source files changed during the build. Re-run the build."
}

$latestSource = Get-InputFiles $projectFullPath |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

if ($latestSource -and $latestSource.LastWriteTimeUtc -gt $assemblyItem.LastWriteTimeUtc)
{
    throw "Stale build detected: $($latestSource.FullName) is newer than Assembly-CSharp.dll."
}

$gitStatusOutput = & git status --porcelain
$verification = [PSCustomObject]@{
    verifiedAtUtc = [System.DateTime]::UtcNow.ToString("o")
    verifiedAtLocal = [System.DateTime]::Now.ToString("o")
    buildStartedUtc = $buildStartedUtc.ToString("o")
    buildStartedLocal = $buildStartedUtc.ToLocalTime().ToString("o")
    executableLastWriteTimeUtc = $executableItem.LastWriteTimeUtc.ToString("o")
    executableLastWriteTimeLocal = $executableItem.LastWriteTime.ToString("o")
    assemblyLastWriteTimeUtc = $assemblyItem.LastWriteTimeUtc.ToString("o")
    assemblyLastWriteTimeLocal = $assemblyItem.LastWriteTime.ToString("o")
    sourceFingerprint = $sourceFingerprintAfter
    commit = Get-GitValue @("rev-parse", "HEAD")
    workingTreeClean = [string]::IsNullOrWhiteSpace(($gitStatusOutput -join "`n"))
    timestampVerified = $true
    sourceFingerprintVerified = $true
}

$verification | ConvertTo-Json | Set-Content -LiteralPath $verificationPath -Encoding UTF8

Write-Host "Build verified."
Write-Host "FPSSample.exe: $($executableItem.LastWriteTimeUtc.ToString('o'))"
Write-Host "FPSSample.exe local: $($executableItem.LastWriteTime.ToString('o'))"
Write-Host "Assembly-CSharp.dll: $($assemblyItem.LastWriteTimeUtc.ToString('o'))"
Write-Host "Assembly-CSharp.dll local: $($assemblyItem.LastWriteTime.ToString('o'))"
Write-Host "Source fingerprint: $sourceFingerprintAfter"
Write-Host "Verification file: $verificationPath"
