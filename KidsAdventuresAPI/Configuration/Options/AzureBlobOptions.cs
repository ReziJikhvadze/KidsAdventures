namespace AdventurePacks.Api.Configuration.Options;

public sealed class AzureBlobOptions
{
    public const string SectionName = "AzureBlobStorage";
    public string ConnectionString { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "adventurepacks";
}
