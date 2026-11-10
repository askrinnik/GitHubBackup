<#
.SYNOPSIS
	PostToolUse hook: checks the message of a commit just made against the git-commit skill.

.DESCRIPTION
	Claude Code runs this script after every Bash or PowerShell tool call and passes the hook input as JSON on
	standard input. When the command ran "git commit", the script reads the last commit of the repository in the
	tool's working directory, or in the folder given by "git -C <path> commit", and checks its message with
	Test-CommitMessage. On a violation it prints a JSON decision "block" whose reason lists the errors, so the
	assistant fixes the commit with "git commit --amend" before any push. Everything else ends silently with exit code 0.

	Skipped on purpose:
	- merge commits (more than one parent), which GitHub creates when a pull request is merged;
	- a detached HEAD, for example during a rebase;
	- a last commit older than two minutes, which means the "git commit" command itself failed.

	The issue title comes from "gh issue view"; when gh cannot read it, line 1 is not compared with the title and a
	warning is printed instead.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module (Join-Path $PSScriptRoot 'CommitMessageRules.psm1') -Force

try {
	$hookInput = [Console]::In.ReadToEnd() | ConvertFrom-Json
}
catch {
	exit 0
}

$command = [string]$hookInput.tool_input.command
if (-not (Test-GitCommitCommand -Command $command)) { exit 0 }

$repository = if ($hookInput.cwd) { [string]$hookInput.cwd } else { (Get-Location).Path }
if ($command -match 'git(\.exe)?\s+-C\s+("(?<path>[^"]*)"|''(?<path>[^'']*)''|(?<path>\S+))(\s+\S+)*?\s+commit(?![-\w])') {
	$repository = Join-Path $repository $Matches['path']
	if ([IO.Path]::IsPathRooted($Matches['path'])) { $repository = $Matches['path'] }
}

$branch = git -C $repository branch --show-current 2>$null
if ($LASTEXITCODE -ne 0 -or -not $branch) { exit 0 }

$parents = @((git -C $repository log -1 --format=%P 2>$null) -split '\s+' | Where-Object { $_ })
if ($LASTEXITCODE -ne 0 -or $parents.Count -gt 1) { exit 0 }

$committedAt = [long](git -C $repository log -1 --format=%ct)
if ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() - $committedAt -gt 120) { exit 0 }

$message = (git -C $repository log -1 --format=%B) -join "`n"
$sha = git -C $repository log -1 --format=%h

$getTitle = {
	param([int]$Number)
	Push-Location $repository
	try {
		$title = gh issue view $Number --json title --jq .title 2>$null
		if ($LASTEXITCODE -ne 0 -or -not $title) { return $null }
		return ([string]$title).Trim()
	}
	catch {
		return $null
	}
	finally {
		Pop-Location
	}
}

$result = Test-CommitMessage -Message $message -Branch $branch -GetTitle $getTitle

foreach ($warning in $result.Warnings) {
	[Console]::Error.WriteLine("Commit message check: $warning")
}

if ($result.Errors.Count -eq 0) { exit 0 }

$reason = @(
	"Commit $sha on branch '$branch' breaks the git-commit skill (.claude/skills/_local.git-commit/SKILL.md):"
	$result.Errors | ForEach-Object { "- $_" }
	'Fix it now with "git commit --amend -F <file>" before any push. If the commit is already pushed, do not amend; tell the user.'
) -join "`n"

@{ decision = 'block'; reason = $reason } | ConvertTo-Json -Compress
exit 0
