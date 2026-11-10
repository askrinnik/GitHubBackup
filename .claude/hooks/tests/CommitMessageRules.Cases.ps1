<#
.SYNOPSIS
	Runs the test cases of CommitMessageRules.psm1 and exits with 1 when any case fails.

.EXAMPLE
	pwsh -NoProfile -File .claude/hooks/tests/CommitMessageRules.Cases.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Import-Module (Join-Path $PSScriptRoot '..' 'CommitMessageRules.psm1') -Force

$titles = @{ 63 = 'Architecture cleanup: configuration binding'; 64 = 'Second issue' }
$getTitle = { param([int]$Number) $titles[$Number] }
$noTitle = { param([int]$Number) $null }

$failures = 0
function Assert-Case {
	param([string]$Name, [bool]$Condition)
	if ($Condition) { Write-Host "PASS  $Name" }
	else { Write-Host "FAIL  $Name"; $script:failures++ }
}

function Get-Errors {
	param([string]$Message, [string]$Branch = '63-architecture-cleanup', [scriptblock]$TitleSource = $getTitle)
	return (Test-CommitMessage -Message $Message -Branch $Branch -GetTitle $TitleSource).Errors
}

$valid = "#63 Architecture cleanup: configuration binding`n`n- Move binding to Infrastructure`n- Add a test"

# Command detection
Assert-Case 'git commit -F is a commit command' (Test-GitCommitCommand 'git commit -F C:/tmp/msg.txt')
Assert-Case 'git -C <path> commit is a commit command' (Test-GitCommitCommand 'git -C "C:/repo dir" commit --amend -F m.txt')
Assert-Case 'git commit inside a compound command' (Test-GitCommitCommand 'cd repo; git add a.cs; git commit -q -F m.txt')
Assert-Case 'git.exe commit is a commit command' (Test-GitCommitCommand 'git.exe commit -m x')
Assert-Case 'git commit-tree is not a commit command' (-not (Test-GitCommitCommand 'git commit-tree abc -m x'))
Assert-Case 'git log is not a commit command' (-not (Test-GitCommitCommand 'git log -1 --format=%B'))
Assert-Case 'gitcommit word is not a commit command' (-not (Test-GitCommitCommand 'echo legit commit'))

# Branch kinds
Assert-Case 'issue branch' ((Get-BranchKind '63-architecture-cleanup').Issue -eq 63)
Assert-Case 'batch branch' ((Get-BranchKind '9-12-core-folders').Kind -eq 'batch')
Assert-Case 'main branch' ((Get-BranchKind 'main').Kind -eq 'main')
Assert-Case 'other branch' ((Get-BranchKind 'feature/x').Kind -eq 'other')

# Valid messages
Assert-Case 'valid issue commit has no errors' (@(Get-Errors $valid).Count -eq 0)
Assert-Case 'valid issue commit with CRLF and trailing newline has no errors' (@(Get-Errors (($valid -replace "`n", "`r`n") + "`r`n")).Count -eq 0)
Assert-Case 'valid batch commit has no errors' (@(Get-Errors "#64 Second issue`n`n- Add it" -Branch '63-64-batch').Count -eq 0)
Assert-Case 'valid commit without issue has no errors' (@(Get-Errors "- Fix typo in README`n- Update .gitignore" -Branch 'docs-typo').Count -eq 0)

# Line 1
$errors = @(Get-Errors "#63 Move configuration binding to Infrastructure`n`n- Move it")
Assert-Case 'custom subject instead of title is an error naming the expected title' ($errors.Count -eq 1 -and $errors[0] -like "*'#63 Architecture cleanup: configuration binding'*")
Assert-Case 'title differing only in case is an error' (@(Get-Errors "#63 architecture cleanup: configuration binding`n`n- Move it").Count -eq 1)
Assert-Case 'line 1 without issue number is an error' (@(Get-Errors "Move binding`n`n- Move it")[0] -like "Line 1 must be*")
$errors = @(Get-Errors "#64 Second issue`n`n- Add it")
Assert-Case 'issue number not matching the branch is an error' ($errors.Count -eq 1 -and $errors[0] -like '*belongs to issue #63*')
$result = Test-CommitMessage -Message "#63 Anything at all`n`n- Do it" -Branch '63-x' -GetTitle $noTitle
Assert-Case 'unreadable title gives a warning, not an error' ($result.Errors.Count -eq 0 -and $result.Warnings.Count -eq 1)

# Line 2 and action lines
Assert-Case 'missing blank line 2 is an error' (@(Get-Errors "#63 Architecture cleanup: configuration binding`n- Move it`n- Add a test")[0] -like 'Line 2 must be blank*')
Assert-Case 'title only is an error' (@(Get-Errors '#63 Architecture cleanup: configuration binding').Count -eq 1)
Assert-Case 'blank line between actions is an error' (@(Get-Errors "#63 Architecture cleanup: configuration binding`n`n- Move it`n`n- Add a test")[0] -like 'Line 4 is blank*')
Assert-Case 'action line without "- " is an error' (@(Get-Errors "#63 Architecture cleanup: configuration binding`n`n- Move it`n* Add a test")[0] -like "Line 4 must be an action*")
Assert-Case 'commit without issue with a plain line is an error' (@(Get-Errors "Fix typo" -Branch 'docs-typo')[0] -like "Line 1 must be an action*")

# Trailers and branch
$errors = @(Get-Errors "$valid`nCo-Authored-By: Someone <a@b.c>")
Assert-Case 'Co-Authored-By trailer is an error' ($errors.Count -ge 1 -and ($errors -join ' ') -like '*trailer*')
Assert-Case 'Generated with line is an error' ((@(Get-Errors "$valid`n- Generated with a tool") -join ' ') -like '*trailer*')
Assert-Case 'commit on main is an error' ((@(Get-Errors '- Fix typo' -Branch 'main') -join ' ') -like '*never to main*')

Write-Host ''
if ($failures -gt 0) {
	Write-Host "$failures case(s) failed."
	exit 1
}
Write-Host 'All cases passed.'
