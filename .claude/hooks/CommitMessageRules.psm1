<#
.SYNOPSIS
	Rules of the git-commit skill for a commit message, shared by the commit hook and its test cases.
#>

Set-StrictMode -Version Latest

<#
.SYNOPSIS
	Returns whether a shell command runs "git commit".

.DESCRIPTION
	Matches "git commit" with optional global options between "git" and "commit" (for example "git -C <path> commit"),
	anywhere in the command, so compound commands are covered too. "git commit-tree" and similar plumbing commands do
	not match.
#>
function Test-GitCommitCommand {
	[CmdletBinding()]
	[OutputType([bool])]
	param([AllowEmptyString()][string]$Command)

	return $Command -match '(^|[\s;&|(])git(\.exe)?(\s+(-C\s+("[^"]*"|''[^'']*''|\S+)|-c\s+\S+|--?[\w-]+(=\S+)?))*\s+commit(?![-\w])'
}

<#
.SYNOPSIS
	Returns the issue numbers a branch name belongs to.

.OUTPUTS
	A hashtable: Kind = 'issue' (branch "<n>-<slug>", Issue = n), 'batch' (branch "<first>-<last>-<slug>"),
	'main' (main or master) or 'other' (any other branch).
#>
function Get-BranchKind {
	[CmdletBinding()]
	param([Parameter(Mandatory)][string]$Branch)

	if ($Branch -in @('main', 'master')) { return @{ Kind = 'main' } }
	if ($Branch -match '^(\d+)-(\d+)-') { return @{ Kind = 'batch' } }
	if ($Branch -match '^(\d+)-') { return @{ Kind = 'issue'; Issue = [int]$Matches[1] } }
	return @{ Kind = 'other' }
}

<#
.SYNOPSIS
	Checks a commit message against the git-commit skill.

.PARAMETER Message
	The full commit message.

.PARAMETER Branch
	The branch the commit was made on.

.PARAMETER GetTitle
	A script block that takes an issue number and returns its exact title, or $null when the title cannot be read.

.OUTPUTS
	A hashtable with Errors (string[]) and Warnings (string[]). No errors means the message follows the skill.
#>
function Test-CommitMessage {
	[CmdletBinding()]
	param(
		[Parameter(Mandatory)][AllowEmptyString()][string]$Message,
		[Parameter(Mandatory)][string]$Branch,
		[Parameter(Mandatory)][scriptblock]$GetTitle
	)

	$errors = [System.Collections.Generic.List[string]]::new()
	$warnings = [System.Collections.Generic.List[string]]::new()
	$lines = @(($Message -replace '(\r?\n)+$', '') -split '\r?\n')
	$branchKind = Get-BranchKind -Branch $Branch

	if ($branchKind.Kind -eq 'main') {
		$errors.Add("The commit is on '$Branch'. Commits go to an issue branch '<n>-<slug>', never to $Branch.")
	}

	for ($i = 0; $i -lt $lines.Count; $i++) {
		if ($lines[$i] -match '^\s*(Co-Authored-By|Signed-off-by)\s*:' -or $lines[$i] -match 'Generated with') {
			$errors.Add("Line $($i + 1) is a trailer or signature ('$($lines[$i].Trim())'); the skill forbids trailers.")
		}
	}

	$isIssueCommit = $branchKind.Kind -in @('issue', 'batch') -or $lines[0] -match '^#\d+\s'
	if ($isIssueCommit) {
		$actionStart = 2
		if ($lines[0] -notmatch '^#(\d+) (\S.*)$') {
			$errors.Add("Line 1 must be '#<n> <exact issue title>'; found '$($lines[0])'.")
		}
		else {
			$number = [int]$Matches[1]
			$subject = $Matches[2]
			if ($branchKind.Kind -eq 'issue' -and $number -ne $branchKind.Issue) {
				$errors.Add("Line 1 names issue #$number, but branch '$Branch' belongs to issue #$($branchKind.Issue).")
			}
			$title = & $GetTitle $number
			if ($null -eq $title) {
				$warnings.Add("The title of issue #$number could not be read, so line 1 was not compared with it.")
			}
			elseif ($subject -cne $title) {
				$errors.Add("Line 1 must be the exact issue title, identical on every commit of the branch: '#$number $title'; found '$($lines[0])'. Describe what this commit does in the '- ' lines, not in line 1.")
			}
		}

		if ($lines.Count -lt 3) {
			$errors.Add('The message needs line 1, a blank line 2 and at least one action line starting with "- ".')
		}
		elseif ($lines[1] -ne '') {
			$errors.Add("Line 2 must be blank; found '$($lines[1])'.")
		}
	}
	else {
		$actionStart = 0
	}

	for ($i = $actionStart; $i -lt $lines.Count; $i++) {
		if ($lines[$i] -eq '') {
			$errors.Add("Line $($i + 1) is blank; action lines are contiguous, with no blank lines between them.")
		}
		elseif ($lines[$i] -notmatch '^- \S') {
			$errors.Add("Line $($i + 1) must be an action starting with '- '; found '$($lines[$i])'.")
		}
	}

	return @{ Errors = [string[]]$errors; Warnings = [string[]]$warnings }
}

Export-ModuleMember -Function Test-GitCommitCommand, Get-BranchKind, Test-CommitMessage
