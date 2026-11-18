#Requires -Version 7.0
<#
.SYNOPSIS
    Starts a temporary copy of the desktop app against a stubbed GitHub API, by scenario.

.DESCRIPTION
    Builds GitHubBackup.TestBench (which builds GitHubBackup.App first) and runs it. The bench copies the build output
    of the desktop app into a new folder under the temp folder, starts a local stub of the GitHub API with the chosen
    scenario, writes appsettings.json with the stub address and a test token that grants nothing, writes
    backup-config.json and, when asked, ui-settings.json, and starts the app from that folder. git is redirected to an
    empty local folder, so nothing contacts GitHub.

    The bench stops when the app exits, Enter is pressed or Ctrl+C is pressed: it stops the app and the stub and deletes
    the folder. The real installation, its settings and the token in Windows Credential Manager are never read or
    changed.

.PARAMETER Scenario
    The scenario: missing-repository, renamed-repository, new-repositories, discovery-review or many-rows.

.PARAMETER Theme
    The theme of the app: light, dark or system. By default the app follows Windows.

.PARAMETER SummaryPanelHeight
    The saved height of the results panel in device-independent pixels, such as its minimum 80.

.PARAMETER NoLaunch
    Prepares the folder and the stub and prints the paths without starting the app, for example to start it from
    the netwright MCP server by its path.

.PARAMETER Keep
    Leaves the temporary folder in place when the bench stops.

.PARAMETER List
    Lists the scenarios and exits.

.PARAMETER NoBuild
    Runs the bench without building it first.

.PARAMETER Configuration
    The build configuration. Defaults to Debug.

.EXAMPLE
    pwsh -File scripts/test-bench.ps1 -Scenario missing-repository

.EXAMPLE
    pwsh -File scripts/test-bench.ps1 -Scenario discovery-review -Theme dark -NoLaunch
#>
[CmdletBinding()]
param(
    [ValidateSet('missing-repository', 'renamed-repository', 'new-repositories', 'discovery-review', 'many-rows')]
    [string] $Scenario = 'missing-repository',
    [ValidateSet('light', 'dark', 'system')]
    [string] $Theme,
    [double] $SummaryPanelHeight,
    [switch] $NoLaunch,
    [switch] $Keep,
    [switch] $List,
    [switch] $NoBuild,
    [string] $Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\GitHubBackup.TestBench\GitHubBackup.TestBench.csproj'
$executable = Join-Path $repositoryRoot "src\GitHubBackup.TestBench\bin\$Configuration\net10.0-windows\GitHubBackup.TestBench.exe"

if (-not $NoBuild) {
    # The build output is shown only when the build fails, so the paths the bench prints stay on screen.
    $buildOutput = dotnet build $project --configuration $Configuration -nologo -clp:ErrorsOnly 2>&1
    if ($LASTEXITCODE -ne 0) {
        $buildOutput | Write-Host
        [Console]::Error.WriteLine("The build of the test bench failed.")
        exit 1
    }
}

$arguments = @()
if ($List) {
    $arguments += '--list'
}
else {
    $arguments += '--scenario', $Scenario
    if ($Theme) { $arguments += '--theme', $Theme }
    if ($PSBoundParameters.ContainsKey('SummaryPanelHeight')) {
        $arguments += '--summary-height', $SummaryPanelHeight.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    }
    if ($NoLaunch) { $arguments += '--no-launch' }
    if ($Keep) { $arguments += '--keep' }
}

& $executable @arguments
exit $LASTEXITCODE
