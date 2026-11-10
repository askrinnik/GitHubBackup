<#
.SYNOPSIS
	Rules of the format hook: which edited files Format-EditedFileHook.ps1 passes to dotnet format.
#>

<#
.SYNOPSIS
	Returns $true when the file is a C# source file that lies under the src folder of the repository.

.DESCRIPTION
	"dotnet format whitespace --folder" formats only code files, and the solution's .editorconfig lives in src, so
	the hook formats only "*.cs" files under "<RepositoryRoot>/src". The file must exist: a Write or Edit that failed
	leaves nothing to format. Paths may use forward or back slashes and are compared without regard to case.
#>
function Test-FormattableFile {
	[CmdletBinding()]
	[OutputType([bool])]
	param(
		[AllowEmptyString()][AllowNull()][string]$Path,
		[Parameter(Mandatory)][string]$RepositoryRoot
	)

	if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
	if ([IO.Path]::GetExtension($Path) -ne '.cs') { return $false }

	$fullPath = [IO.Path]::GetFullPath($Path, $RepositoryRoot)
	$sourceRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'src'))) + [IO.Path]::DirectorySeparatorChar
	if (-not $fullPath.StartsWith($sourceRoot, [StringComparison]::OrdinalIgnoreCase)) { return $false }

	return [IO.File]::Exists($fullPath)
}

Export-ModuleMember -Function Test-FormattableFile
