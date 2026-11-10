<#
.SYNOPSIS
	PostToolUse hook: fixes whitespace and indentation of a C# file the assistant has just written or edited.

.DESCRIPTION
	Claude Code runs this script after every Edit, Write or MultiEdit tool call and passes the hook input as JSON on
	standard input. When the edited file passes Test-FormattableFile, the script runs
	"dotnet format whitespace src --folder --include <file>" from the repository root. Folder mode reads only
	.editorconfig and does not load MSBuild, so one file takes about a second.

	The hook never blocks the assistant: a file it does not handle, unreadable input and a failing dotnet format all
	end with exit code 0. A failure is reported as one line on standard error; the format check of the build gate
	still catches whatever was left unformatted.

	The repository root is $env:CLAUDE_PROJECT_DIR, or the "cwd" of the hook input when the variable is not set.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module (Join-Path $PSScriptRoot 'FormatHookRules.psm1') -Force

try {
	$hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
}
catch {
	exit 0
}

$repositoryRoot = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } elseif ($hookInput.cwd) { [string]$hookInput.cwd } else { exit 0 }
$filePath = [string]$hookInput.tool_input.file_path

if (-not (Test-FormattableFile -Path $filePath -RepositoryRoot $repositoryRoot)) { exit 0 }

$fullPath = [IO.Path]::GetFullPath($filePath, $repositoryRoot)

Push-Location $repositoryRoot
try {
	$output = dotnet format whitespace src --folder --include $fullPath 2>&1
	if ($LASTEXITCODE -ne 0) {
		$firstLine = @($output | Where-Object { "$_".Trim() } | Select-Object -First 1) -join ''
		[Console]::Error.WriteLine("Format hook: dotnet format exited with code $LASTEXITCODE for '$filePath'. $firstLine")
	}
}
catch {
	[Console]::Error.WriteLine("Format hook: $($_.Exception.Message)")
}
finally {
	Pop-Location
}

exit 0
