namespace AdventurePacks.Api.Configuration.Options;

/// <summary>
/// Swaps blob storage for a folder on disk, so a developer can run the whole book
/// pipeline without an Azure Storage account.
///
/// Off unless something turns it on, and nothing in the repository turns it on:
/// deployments keep resolving <c>AzureBlobStorageService</c> exactly as before.
/// Local machines switch it on through user secrets.
/// </summary>
public sealed class LocalBlobOptions
{
    public const string SectionName = "LocalBlobStorage";

    /// <summary>The switch. False everywhere except a machine that opts in.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where the files land. Relative paths resolve against the content root, so the
    /// default keeps everything inside the working tree and a single delete is enough
    /// to reclaim the space.
    /// </summary>
    public string RootPath { get; set; } = ".localblob";
}
