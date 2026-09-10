<#
.SYNOPSIS
    Builds a release copy of DropShelf.

.DESCRIPTION
    Two shapes are available.

    Self-contained, the default, bundles the .NET runtime into the executable.
    It is around 150MB and runs on a machine with no .NET installed, which is the
    right trade for something a person downloads once and forgets about.

    Framework-dependent produces a couple of megabytes instead, but the machine
    needs the .NET 9 Desktop Runtime already.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -FrameworkDependent
#>

[CmdletBinding()]
param(
    [switch] $FrameworkDependent,
    [string] $Runtime = 'win-x64',
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptRoot '..')
$project = Join-Path $repoRoot 'src\DropShelf\DropShelf.csproj'

if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'artifacts\publish'
}

$arguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', $Runtime,
    '--output', $OutputPath,
    '-p:PublishSingleFile=true',

    # WPF keeps some native components that cannot live inside the bundle
    # unmodified. This unpacks them beside the executable at first run instead of
    # leaving a folder of loose DLLs next to it.
    '-p:IncludeNativeLibrariesForSelfExtract=true',

    # Deliberately not enabling PublishTrimmed. WPF resolves a great deal through
    # reflection and XAML, and the trimmer removes types it cannot see being used,
    # which produces an app that builds cleanly and then fails at run time.
    '-p:PublishTrimmed=false'
)

if ($FrameworkDependent) {
    $arguments += '--self-contained:false'
} else {
    $arguments += '--self-contained:true'
}

Write-Host "Publishing to $OutputPath"
& dotnet @arguments

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$executable = Join-Path $OutputPath 'DropShelf.exe'
if (-not (Test-Path $executable)) {
    throw "Publish reported success but $executable is missing."
}

$sizeMb = [Math]::Round((Get-Item $executable).Length / 1MB, 1)
Write-Host "Built $executable ($sizeMb MB)"
