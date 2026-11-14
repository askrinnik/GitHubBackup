#Requires -Version 7.0
<#
.SYNOPSIS
    Publishes the WPF and the console host into one application folder.

.DESCRIPTION
    Publishes GitHubBackup.App and GitHubBackup.Cli for win-x64 as framework-dependent single-file executables into
    one folder: GitHubBackup.exe, GitHubBackup.Cli.exe and appsettings.json. The folder needs only the .NET 10
    Desktop Runtime on the target machine.

    The build runs with its own artifacts path in a temporary folder, so it neither uses nor changes the bin and obj
    folders of a developer build.

    An existing output folder that is not empty is left untouched unless -Clean is passed: an installed application
    folder also holds backup-config.json, the run history and the clones. -Clean deletes only the files a publish run
    writes and refuses a folder that holds anything else.

    The script fails when the published appsettings.json sets GitHub:Token, so a token never reaches the artifact.

.PARAMETER OutputPath
    The application folder to publish into. Defaults to artifacts\publish under the repository root.

.PARAMETER Configuration
    The build configuration. Defaults to Release.

.PARAMETER Clean
    Deletes the previous publish output in the output folder. Refuses a folder that holds other files or subfolders.

.EXAMPLE
    pwsh -File scripts/publish.ps1

.EXAMPLE
    pwsh -File scripts/publish.ps1 -OutputPath C:\Temp\GitHubBackup -Clean
#>
[CmdletBinding()]
param(
    [string] $OutputPath,
    [string] $Configuration = 'Release',
    [switch] $Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Writes a one-line error to standard error and ends the script with exit code 1.
function Stop-Publish([string] $Message) {
    [Console]::Error.WriteLine($Message)
    exit 1
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot 'artifacts\publish'
}

$OutputPath = [System.IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)

if (Test-Path -LiteralPath $OutputPath -PathType Leaf) {
    Stop-Publish "The output path '$OutputPath' is a file, not a folder."
}

# -Clean deletes only what a publish run writes, so a mistyped path never removes a repository, a drive or an
# installed application folder with its configuration, history and clones.
$publishOutputNames = @('GitHubBackup.exe', 'GitHubBackup.Cli.exe', 'GitHubBackup.App.exe', 'appsettings.json')

if (Test-Path -LiteralPath $OutputPath -PathType Container) {
    $existing = @(Get-ChildItem -LiteralPath $OutputPath -Force)
    if ($existing.Count -gt 0) {
        if (-not $Clean) {
            Stop-Publish "The output folder '$OutputPath' is not empty. Pass -Clean to delete it before publishing."
        }

        $foreign = @($existing | Where-Object { $_.PSIsContainer -or $publishOutputNames -notcontains $_.Name })
        if ($foreign.Count -gt 0) {
            Stop-Publish "The output folder '$OutputPath' holds '$($foreign[0].Name)', which a publish run does not write. -Clean deletes only publish output; choose an empty folder or empty it by hand."
        }

        $existing | Remove-Item -Force
    }
}

$artifactsPath = Join-Path ([System.IO.Path]::GetTempPath()) "GitHubBackup.Publish\$([guid]::NewGuid().ToString('N'))"
$projects = @(
    'src\GitHubBackup.Cli\GitHubBackup.Cli.csproj',
    'src\GitHubBackup.App\GitHubBackup.App.csproj'
)

try {
    foreach ($project in $projects) {
        dotnet publish (Join-Path $repositoryRoot $project) `
            --configuration $Configuration `
            --runtime win-x64 `
            --artifacts-path $artifactsPath `
            --output $OutputPath `
            -clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) {
            Stop-Publish "dotnet publish of $project failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $artifactsPath) {
        Remove-Item -LiteralPath $artifactsPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# The SDK names a bundle after its assembly; the WPF executable is the application itself, so it carries the
# application name, while the assembly keeps its project name.
Move-Item -LiteralPath (Join-Path $OutputPath 'GitHubBackup.App.exe') -Destination (Join-Path $OutputPath 'GitHubBackup.exe')

# The published folder is shared as a build artifact; a token in the source settings file must never travel with it.
$settings = Get-Content -LiteralPath (Join-Path $OutputPath 'appsettings.json') -Raw | ConvertFrom-Json
if ($settings.PSObject.Properties['GitHub'] -and $settings.GitHub.PSObject.Properties['Token'] -and $settings.GitHub.Token) {
    Remove-Item -LiteralPath (Join-Path $OutputPath 'appsettings.json') -Force
    Stop-Publish "appsettings.json sets GitHub:Token; keep the token in user secrets or Credential Manager and publish again."
}

Write-Host "Published to $OutputPath"
