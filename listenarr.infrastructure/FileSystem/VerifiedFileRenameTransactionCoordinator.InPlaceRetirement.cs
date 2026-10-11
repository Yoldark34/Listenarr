namespace Listenarr.Infrastructure.FileSystem;

public sealed partial class VerifiedFileRenameTransactionCoordinator
{
    // Local patch: FUSE mounts such as rclone reject RENAME_NOREPLACE (EINVAL/ENOSYS/EOPNOTSUPP), so the source
    // cannot enter the retirement namespace. Re-verify both ends and delete the source in place instead of
    // retaining a full duplicate after every organize. Any other quarantine failure still retains the source.
    private async Task<VerifiedFileRenameRetirementOutcome> HandleUnpublishedQuarantineAsync(
        PinnedDirectoryCreation.PinnedRenameAttempt quarantine,
        Guid operationId,
        PinnedDirectoryCreation.PinnedDirectoryAnchor sourceParent,
        PinnedDirectoryCreation.PinnedFileEntry sourceEntry,
        Func<CancellationToken, Task<bool>> targetStillMatchesAsync,
        Func<CancellationToken, Task<bool>> sourceStillMatchesAsync,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || quarantine.NativeErrorCode is not (22 or 38 or 95))
        {
            await MarkSourceRetainedAsync(
                operationId,
                "Owner metadata was committed, but the source could not enter the operation-owned retirement namespace without replacement. The old source was retained.",
                CancellationToken.None);
            return VerifiedFileRenameRetirementOutcome.SourceRetained;
        }

        await AdvanceAsync(operationId, VerifiedFileRenameState.SourceQuarantined, error: null, CancellationToken.None);
        if (!await targetStillMatchesAsync(cancellationToken) || !await sourceStillMatchesAsync(cancellationToken))
        {
            await MarkSourceRetainedAsync(
                operationId,
                "Owner metadata was committed, but the source or target changed before in-place retirement. The old source was retained.",
                CancellationToken.None);
            return VerifiedFileRenameRetirementOutcome.SourceRetained;
        }

        sourceEntry.Delete(immediateWindows: true);
        sourceParent.FlushDirectoryEntry();
        await AdvanceAsync(operationId, VerifiedFileRenameState.SourceDeleted, error: null, CancellationToken.None);
        await AdvanceAsync(operationId, VerifiedFileRenameState.Completed, error: null, CancellationToken.None);
        return VerifiedFileRenameRetirementOutcome.Completed;
    }
}
