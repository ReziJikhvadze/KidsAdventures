using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;
using AdventurePacks.Api.Domain.Story;

namespace AdventurePacks.Api.Services.Story.Composite;

/// <summary>A deterministic cover brief, never a story or a supporting-cast plan.</summary>
public static class FastPreviewPlan
{
    // Persisted in MasterStoryRuns.PromptVersion (NVARCHAR(10)).
    public const string Version = "preview-v2";
    public const string Model = "gpt-image-2.5-flare";
    public static bool IsFast(MasterStoryRun? run) => run?.PromptVersion is "preview-v1" or Version;
    public static string ReceiptName(Guid id) => $"master-runs/{id:N}/preview-revision.json";
    public static string IntroName(Guid id) => $"master-runs/{id:N}/preview-intro.webp";
    public static string CoverPdfName(Guid id) => $"master-runs/{id:N}/preview-cover.pdf";
    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    // Each run starts with a random UUID. Deriving the choice from it keeps retries stable,
    // while the final revision freezes the literal title for purchase and later recovery.
    public static string Title(string name, string theme, Guid runId)
    {
        var titles = ThemeTitles(theme);
        var seed = SHA256.HashData(runId.ToByteArray());
        var index = (int)(BinaryPrimitives.ReadUInt32LittleEndian(seed) % (uint)titles.Count);
        return $"{name.Trim()} და {titles[index]}";
    }

    public static string RenameTitle(string title, string oldName, string newName)
    {
        var prefix = oldName.Trim() + " და ";
        if (!title.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The saved preview title does not match its child name.");
        return newName.Trim() + " და " + title[prefix.Length..];
    }

    public static IReadOnlyList<string> ThemeTitles(string theme) => theme switch
    {
        "clouds" => ["ღრუბლების ქალაქი", "ცისარტყელის ბილიკი", "ფუმფულა ღრუბლის საიდუმლო", "ცაში დამალული ბაღი", "მზის სხივის თავგადასავალი"],
        "space" => ["ვარსკვლავების გზა", "მთვარის პატარა საიდუმლო", "დაკარგული ვარსკვლავი", "ფერადი პლანეტების მოგზაურობა", "ცის ყველაზე ნათელი ვარსკვლავი"],
        "forest" => ["მოჯადოებული ტყის მეგობრები", "ტყის ჩუმი საიდუმლო", "ოქროსფერი ფოთლის ამბავი", "დაკარგული ბილიკი", "პატარა ტყის დიდი დღესასწაული"],
        "ocean" => ["მბრწყინავი კუნძულის საიდუმლო", "მოლაპარაკე ნიჟარა", "მარჯნის ბაღი", "ზღვის დაკარგული სიმღერა", "ტალღების საჩუქარი"],
        "magic" => ["სინათლის ქალაქის კარიბჭე", "ჯადოსნური ფარნის საიდუმლო", "ფერების დღესასწაული", "სურვილების პატარა ხიდი", "დაკარგული ცისარტყელა"],
        "dinosaurs" => ["დინოზავრების ხეობა", "პატარა დინოზავრის დიდი დღე", "იდუმალი ნაკვალევი", "ფერადი კვერცხის საიდუმლო", "დინოზავრების მეგობრობის ზეიმი"],
        _ => throw new ArgumentException("Unknown preview theme.")
    };
    public const string Outfit = "A simple golden-yellow sweater, teal trousers and cream shoes; no logos or head covering.";
    public static CompositeScenarioPlan Plan(string theme) => CompositePreviewCoverPlan.Create(new VisualScenarioV2
    {
        VisualLock = new VisualLock { ChildOutfit = Outfit, RecurringElements = [] },
        Cover = new VisualScenarioCover
        {
            FrontChildWorldScene = $"The child explores {CompositeThemeReferences.For(theme).VisualDirection} Include {Details(theme)}.",
            BackEnvironment = "The same landscape continues quietly without any characters.",
            BekiAction = "Beki welcomes the child with an inviting wave."
        },
        Spreads = []
    });
    public static string Details(string theme) => theme switch
    {
        "clouds" => "a rounded floating tower, a glowing cloud bridge and a golden rooftop",
        "space" => "two luminous stars and a softly glowing ringed planet",
        "forest" => "a glowing flower, a broad luminous leaf and a rounded mossy tree",
        "ocean" => "a pearly shell, a coral arch and a polished treasure gem",
        "magic" => "a luminous gateway, a golden lantern and a rounded tower",
        "dinosaurs" => "a dinosaur footprint, a speckled egg and a giant fern",
        _ => throw new ArgumentException("Unknown preview theme.")
    };
    public static string Prompt(int age, string gender, string theme) => $"""
        Create a premium continuous full-cover illustration for the selected BEKI theme.
        Use the supplied original child photo as the identity reference. Depict the child at numeric age {age}, gender {gender}.
        Preserve the child's likeness, face shape, eyes and hair. The child is the main hero, full body visible with breathing room from hair to shoes.
        {CompositeChildArtStyle.Instruction}
        Theme: {CompositeThemeReferences.For(theme).VisualDirection}
        Outfit: {Outfit}
        Include two or three prominent foreground or midground details: {Details(theme)}.
        These have clear shapes, attractive materials and strong lighting, suitable for later selective print varnish.
        Do not imitate varnish or generate a varnish mask.
        {BekiCoverDieline.PanelInstructions}
        {BekiCoverDieline.ReservedArtworkInstructions}
        Reserve the lower-left area of the FRONT board for the title, and its upper-right area for the white logo.
        Leave a natural clear area for a separate Beki PNG on the right, centred at 87% of the full canvas width and 45% of its height,
        occupying 30% of the full canvas height. Keep the child and important details outside that area.
        The back is environment only. Keep faces, hands and important objects away from spine, hinges and folds.
        The theme reference is for environment and style only: do not copy any character from it.
        Do not draw Beki, a substitute guide, any other character, text, title, logo, QR, frame, print marks or a varnish mask.
        """;
}

public sealed record FastPreviewRevision(
    Guid PreviewId, Guid CoverRevisionId, string ThemeId, string Title, string PhotoSha256,
    string MasterSha256, string BaseSha256, string PromptVersion, string PromptSha256,
    string Model, string ScenarioJson, string LayoutJson, string CoverPdfSha256,
    string FrontSha256, string IntroSha256, string? FrontUrl = null);
