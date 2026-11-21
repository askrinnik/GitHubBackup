namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Holds the short descriptions <see cref="GitClient"/> logs before each git command, written for a reader who does
/// not know the options of git.
/// </summary>
/// <remarks>
/// Every description is a constant: branches, paths and URLs appear only in the detail entries of the run.
/// </remarks>
internal static class GitCommandDescriptions
{
    /// <summary>Describes <c>git clone --no-checkout</c>.</summary>
    public const string Clone = "Cloning the repository without a checkout";

    /// <summary>Describes <c>git fetch origin --prune --prune-tags --tags --force</c>.</summary>
    public const string Fetch = "Fetching branches and tags from the server";

    /// <summary>Describes <c>git ls-remote --symref origin HEAD refs/heads/* refs/tags/*</c> in a clone.</summary>
    public const string ReadRemoteRefs = "Reading the branches, tags and default branch of the server";

    /// <summary>Describes <c>git ls-remote --refs</c> against a URL, outside a clone.</summary>
    public const string ReadRemoteRefsWithoutClone = "Reading the branches and tags of the server without a clone";

    /// <summary>Describes <c>git ls-remote --symref origin HEAD</c>.</summary>
    public const string ReadDefaultBranch = "Reading the default branch of the server";

    /// <summary>Describes <c>git remote set-url origin</c>.</summary>
    public const string SetRemoteUrl = "Pointing origin at the repository URL";

    /// <summary>Describes <c>git config --get remote.origin.url</c>.</summary>
    public const string ReadOriginUrl = "Reading the origin URL of the clone";

    /// <summary>Describes <c>git status --porcelain=v2</c>.</summary>
    public const string ReadStatus = "Checking the working copy for changes and untracked files";

    /// <summary>Describes <c>git rev-parse --verify</c> of a local branch.</summary>
    public const string CheckLocalBranch = "Checking whether the local branch exists";

    /// <summary>Describes <c>git rev-parse --verify</c> of a remote-tracking branch.</summary>
    public const string CheckTrackingBranch = "Checking whether the remote-tracking branch exists";

    /// <summary>Describes <c>git rev-list --count</c>.</summary>
    public const string CountUnpushedCommits = "Counting commits that are not on the server";

    /// <summary>Describes <c>git ls-files --others --ignored --exclude-standard --directory</c>.</summary>
    public const string ListIgnoredFiles = "Listing ignored files";

    /// <summary>Describes <c>git ls-files --stage</c>.</summary>
    public const string ReadIndex = "Reading the index for submodules and symbolic links";

    /// <summary>Describes <c>git for-each-ref</c>.</summary>
    public const string ReadLocalRefs = "Reading the branches and tags of the clone";

    /// <summary>Describes <c>git checkout -B</c>.</summary>
    public const string Checkout = "Switching the working tree to the target branch";

    /// <summary>Describes <c>git reset --hard</c>.</summary>
    public const string Reset = "Resetting the working tree to the server state";

    /// <summary>Describes <c>git clean -ffdx</c>.</summary>
    public const string CleanAll = "Removing untracked and ignored files";

    /// <summary>Describes <c>git clean -ffd</c>.</summary>
    public const string CleanKeepingIgnored = "Removing untracked files and keeping ignored files";

    /// <summary>Describes <c>git submodule sync --recursive</c>.</summary>
    public const string SubmoduleSync = "Synchronizing submodule URLs";

    /// <summary>Describes <c>git submodule update --init --recursive --force</c>.</summary>
    public const string SubmoduleUpdate = "Checking out submodules";

    /// <summary>Describes <c>git lfs pull</c>.</summary>
    public const string LfsPull = "Downloading the LFS files of the checked-out branch";
}
