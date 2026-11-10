<#
.SYNOPSIS
	Runs the test cases of FormatHookRules.psm1 and Format-EditedFileHook.ps1 and exits with 1 when any case fails.

.DESCRIPTION
	The rule cases use a temporary repository folder. The hook cases run the hook script in a child pwsh process
	against that folder, with a copy of the solution's .editorconfig and a badly indented C# file, and need the
	.NET SDK on PATH.

.EXAMPLE
	pwsh -NoProfile -File .claude/hooks/tests/FormatHookRules.Cases.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module (Join-Path $PSScriptRoot '..' 'FormatHookRules.psm1') -Force

$hookScript = Join-Path $PSScriptRoot '..' 'Format-EditedFileHook.ps1'
$editorConfig = Join-Path $PSScriptRoot '..' '..' '..' 'src' '.editorconfig'

$failures = 0
function Assert-Case {
	param([string]$Name, [bool]$Condition)
	if ($Condition) { Write-Host "PASS  $Name" }
	else { Write-Host "FAIL  $Name"; $script:failures++ }
}

function Invoke-Hook {
	param([string]$RepositoryRoot, [string]$InputJson)
	$previous = $env:CLAUDE_PROJECT_DIR
	$env:CLAUDE_PROJECT_DIR = $RepositoryRoot
	try {
		$stdout = $InputJson | pwsh -NoProfile -File $hookScript 2>$null
		return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = (@($stdout) -join "`n") }
	}
	finally {
		$env:CLAUDE_PROJECT_DIR = $previous
	}
}

$root = Join-Path ([IO.Path]::GetTempPath()) ("format-hook-cases-" + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $root 'src'
$project = Join-Path $source 'Sample'
$badCode = "namespace Sample;`r`n`r`npublic static class Calculator`r`n{`r`n        public static int Add(int a, int b)`r`n  {`r`n      return a + b;`r`n }`r`n}`r`n"
$codeFile = Join-Path $project 'Calculator.cs'
$xamlFile = Join-Path $project 'View.xaml'
$outsideFile = Join-Path $root 'Outside.cs'

try {
	New-Item -ItemType Directory -Path $project -Force | Out-Null
	Copy-Item $editorConfig (Join-Path $source '.editorconfig')
	Set-Content -Path $codeFile -Value $badCode -NoNewline
	Set-Content -Path $xamlFile -Value '<Grid />' -NoNewline
	Set-Content -Path $outsideFile -Value $badCode -NoNewline

	# File selection
	Assert-Case 'C# file under src is formattable' (Test-FormattableFile -Path $codeFile -RepositoryRoot $root)
	Assert-Case 'forward slashes and other case are formattable' (Test-FormattableFile -Path ($codeFile.Replace('\', '/').ToUpperInvariant().Replace('.CS', '.cs')) -RepositoryRoot $root)
	Assert-Case 'path relative to the repository root is formattable' (Test-FormattableFile -Path 'src/Sample/Calculator.cs' -RepositoryRoot $root)
	Assert-Case 'XAML file is not formattable' (-not (Test-FormattableFile -Path $xamlFile -RepositoryRoot $root))
	Assert-Case 'C# file outside src is not formattable' (-not (Test-FormattableFile -Path $outsideFile -RepositoryRoot $root))
	Assert-Case 'C# file outside the repository is not formattable' (-not (Test-FormattableFile -Path (Join-Path ([IO.Path]::GetTempPath()) 'Other.cs') -RepositoryRoot $root))
	Assert-Case 'folder named like src is not formattable' (-not (Test-FormattableFile -Path (Join-Path $root 'src2' 'A.cs') -RepositoryRoot $root))
	Assert-Case 'missing C# file is not formattable' (-not (Test-FormattableFile -Path (Join-Path $project 'Missing.cs') -RepositoryRoot $root))
	Assert-Case 'empty path is not formattable' (-not (Test-FormattableFile -Path '' -RepositoryRoot $root))

	# Hook script
	$editInput = @{ hook_event_name = 'PostToolUse'; tool_name = 'Edit'; cwd = $root; tool_input = @{ file_path = $codeFile } } | ConvertTo-Json -Compress
	$result = Invoke-Hook -RepositoryRoot $root -InputJson $editInput
	$formatted = Get-Content -Path $codeFile -Raw
	Assert-Case 'hook exits with 0 after formatting' ($result.ExitCode -eq 0)
	Assert-Case 'hook prints nothing on standard output' ([string]::IsNullOrWhiteSpace($result.Output))
	Assert-Case 'hook fixes the indentation of the edited file' ($formatted -match "(?m)^    public static int Add" -and $formatted -match "(?m)^        return a \+ b;")

	$outsideInput = @{ tool_name = 'Write'; cwd = $root; tool_input = @{ file_path = $outsideFile } } | ConvertTo-Json -Compress
	$result = Invoke-Hook -RepositoryRoot $root -InputJson $outsideInput
	Assert-Case 'hook leaves a C# file outside src unchanged' ($result.ExitCode -eq 0 -and (Get-Content -Path $outsideFile -Raw) -eq $badCode)

	$result = Invoke-Hook -RepositoryRoot $root -InputJson 'not json'
	Assert-Case 'hook exits with 0 on unreadable input' ($result.ExitCode -eq 0 -and [string]::IsNullOrWhiteSpace($result.Output))
}
finally {
	if (Test-Path $root) { Remove-Item -Path $root -Recurse -Force }
}

if ($failures -gt 0) {
	Write-Host "$failures case(s) failed."
	exit 1
}
Write-Host 'All cases passed.'
exit 0
