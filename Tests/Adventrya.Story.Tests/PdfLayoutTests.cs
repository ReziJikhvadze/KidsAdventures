using System.Text;
using AdventurePacks.Api.Configuration.Options;
using AdventurePacks.Api.DTOs.AdventurePacks;
using AdventurePacks.Api.Services.Implementations;
using AdventurePacks.Api.Services.Interfaces;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Adventrya.Story.Tests;

/// <summary>
/// Guards the printed book's shape.
///
/// The regression these exist for: the PDF looped over
/// <c>StoryPages.Take(AdventureStoryConstants.PageCount)</c>, and that constant is the
/// *legacy* six-page alias rather than the sixteen a spread book has. Ten pages were dropped
/// silently — the illustrations were downloaded first, then discarded — and nothing failed,
/// because a book that stops after three spreads is still a valid PDF.
///
/// Page counts are read back out of the generated file rather than trusted, since the whole
/// fault was a count that looked right in code and was wrong on paper.
/// </summary>
public class PdfLayoutTests(ITestOutputHelper output)
{
    private static string? OutputDirectory => Environment.GetEnvironmentVariable("ADVENTRYA_PDF_DIR");

    [Fact]
    public void The_print_copy_pads_to_the_binding_multiple()
    {
        var pdf = Render(SpreadBook(), cover: PixelPng(), forPrint: true);

        // Front cover + 16 content pages + back cover = 18, padded to 20 so folded sheets
        // divide by four.
        Assert.Equal(20, CountPages(pdf));
    }

    /// <summary>
    /// The copy a parent downloads has no fold, so it has no blank leaves either — it ends on
    /// the back cover with nothing after it. Two empty pages before the ending are correct for
    /// a binder and read as a book that failed to finish.
    /// </summary>
    [Fact]
    public void The_reading_copy_is_not_padded()
    {
        var pdf = Render(SpreadBook(), cover: PixelPng());

        Assert.Equal(18, CountPages(pdf));
    }

    /// <summary>A vendor printing the covers itself gets the interior and nothing else.</summary>
    [Fact]
    public void The_interior_alone_needs_no_padding()
    {
        var pdf = Render(SpreadBook(), cover: null, layout =>
        {
            layout.IncludeCoverInInterior = false;
            layout.IncludeBackCover = false;
        });

        Assert.Equal(16, CountPages(pdf));
    }

    /// <summary>
    /// The blank leaves belong inside the book. Padding added after the back cover would bind
    /// the closing page in the middle and leave the reader finishing on an empty sheet.
    /// </summary>
    [Fact]
    public void The_back_cover_is_the_last_leaf_and_padding_falls_before_it()
    {
        var plan = AdventurePdfService.BuildPlan(SpreadBook(), new PrintLayoutOptions(), forPrint: true);

        Assert.Equal(PrintPageKind.Back, plan[^1].Kind);
        Assert.Equal(PrintPageKind.Blank, plan[^2].Kind);
        Assert.Single(plan, p => p.Kind == PrintPageKind.Back);
        Assert.Equal(plan.Count, plan[^1].PhysicalPage);
    }

    /// <summary>
    /// Both halves of a spread carry the same title. Printing it on each produced a book where
    /// every chapter heading appeared twice, once over an empty text block.
    /// </summary>
    [Fact]
    public void A_spread_title_is_set_once_not_on_both_halves()
    {
        var content = SpreadBook();
        var plan = AdventurePdfService.BuildPlan(content, new PrintLayoutOptions());

        // Only prose pages carry a title, so each distinct title is set exactly once.
        var titled = plan
            .Where(slot => slot.Kind == PrintPageKind.Prose)
            .Select(slot => slot.Source!.Title)
            .ToList();

        Assert.Equal(8, titled.Count);
        Assert.Equal(titled.Count, titled.Distinct().Count());
        Assert.DoesNotContain(plan, slot => slot.Kind == PrintPageKind.Picture && slot.Source is null);
    }

    /// <summary>
    /// Pictures belong on the left-hand page. That only holds if the cover takes page one, so
    /// this is really a test that the cover is not silently dropped from the interior.
    /// </summary>
    [Fact]
    public void Pictures_fall_on_verso_pages()
    {
        var plan = AdventurePdfService.BuildPlan(SpreadBook(), new PrintLayoutOptions());

        var pictures = plan.Where(slot => slot.Kind == PrintPageKind.Picture).ToList();
        var prose = plan.Where(slot => slot.Kind == PrintPageKind.Prose).ToList();

        Assert.Equal(8, pictures.Count);
        Assert.All(pictures, slot => Assert.Equal(0, slot.PhysicalPage % 2));
        Assert.All(prose, slot => Assert.Equal(1, slot.PhysicalPage % 2));
    }

    /// <summary>The whole point: sixteen pages reach the printer, not the legacy six.</summary>
    [Fact]
    public void Every_spread_reaches_the_plan()
    {
        var plan = AdventurePdfService.BuildPlan(SpreadBook(), new PrintLayoutOptions());

        Assert.Equal(16, plan.Count(slot => slot.Kind is PrintPageKind.Picture or PrintPageKind.Prose));
    }

    /// <summary>Books written before spreads existed still print, one page at a time.</summary>
    [Fact]
    public void A_legacy_book_still_prints()
    {
        var content = new AdventureContentDto
        {
            Title = "Legacy",
            ChildName = "Nino",
            StoryPages = Enumerable.Range(1, 6).Select(i => new StoryPageDto
            {
                Title = $"Chapter {i}",
                Content = $"Body text for page {i}.",
                IsTextOnlyPage = false,
                ImageBytes = PixelPng()
            }).ToList()
        };

        // 1 cover + 6 pages = 7, padded to 8.
        Assert.Equal(8, CountPages(Render(content, cover: null)));
    }

    [Fact]
    public void Geometry_follows_configuration()
    {
        var pdf = Render(SpreadBook(), cover: null, layout =>
        {
            layout.TrimWidthMm = 210f;
            layout.TrimHeightMm = 210f;
            layout.BleedMm = 3f;
        });

        // 216mm (210 trim + 3 bleed a side) is 612.28pt. QuestPDF quantises to a quarter point,
        // so the tolerance is half a point rather than an exact match.
        var (width, height) = FirstMediaBox(pdf);
        Assert.InRange(width, 611.8, 612.8);
        Assert.InRange(height, 611.8, 612.8);
    }

    /// <summary>Writes a sample to disk for eyeballing. Set ADVENTRYA_PDF_DIR to use it.</summary>
    [SkippableFact]
    public void Write_a_sample_for_inspection()
    {
        Skip.If(string.IsNullOrWhiteSpace(OutputDirectory), "Set ADVENTRYA_PDF_DIR to write a sample.");

        foreach (var (name, book, language) in new[]
                 {
                     ("sample-book.pdf", SpreadBook(), "en"),
                     ("sample-book-ka.pdf", GeorgianBook(), "ka")
                 })
        {
            var pdf = Render(book, cover: PixelPng(), language: language);
            var path = Path.Combine(OutputDirectory!, name);
            File.WriteAllBytes(path, pdf);
            output.WriteLine($"{name}: {CountPages(pdf)} pages, {pdf.Length / 1024}kB");
        }
    }

    /// <summary>
    /// A real book, verbatim: the story v4 wrote for თამარიკო in the Lost Valley.
    ///
    /// Made-up Latin filler proves the geometry and nothing about the thing most likely to
    /// break it — Georgian sets wider than English, breaks on different boundaries, and these
    /// are the actual sentence lengths the model produces.
    /// </summary>
    private static AdventureContentDto GeorgianBook()
    {
        (string Title, string Caption, string Text)[] spreads =
        [
            ("საიდუმლო რუკა", "ხეობა ელოდება",
                "თამარიკოს ხელში ძველი რუკა ეჭირა. რუკაზე ეწერა: „დაკარგული ხეობა აქ არის!“ "
                + "თამარიკომ დიდი ქვის კარიბჭე იპოვა. მან ღრმად ჩაისუნთქა და შიგნით შევიდა."),
            ("უცნაური ხმა", "ვინ ტირის ქვებს შორის?",
                "უცებ ხეობაში ტირილი გაისმა. „ჭიკ-ჭიკ... დამეხმარე!“ თამარიკო ხმას გაჰყვა. "
                + "დიდ გვიმრებს შორის პატარა დინოზავრი იპოვა."),
            ("დაკარგული ბუდე", "ბუდე ცარიელია",
                "ნუნუმ თამარიკოს მრგვალი ქვა მიუტანა. იქვე ცარიელი ბუდე იპოვეს. "
                + "ნუნუს ოჯახი არსად ჩანდა. თამარიკომ უთხრა: „ერთად მოვძებნით!“"),
            ("ქვის მდინარე", "ნაბიჯი, კიდევ ნაბიჯი",
                "ბილიკმა ხმაურიან მდინარესთან მიიყვანა ისინი. წყალში დიდი ქვები რიგად ჩანდნენ. "
                + "თამარიკომ ჯერ ერთი ქვა გამოსცადა. ნუნუ უკან მიჰყვა და ორივემ ფრთხილად გადალახა მდინარე."),
            ("ქვებზე დახატული გზა", "ნიშნები გვიჩვენებენ გზას",
                "მეორე ნაპირზე უცნაური ქვები დახვდათ. ქვებზე მზის, მთვარის და ფოთლის ნიშნები იყო ამოკვეთილი. "
                + "თამარიკომ რუკა გაშალა. ნიშნები რუკას დაემთხვა და გზა აჩვენა."),
            ("ციცინათელების ბილიკი", "შუქი ბნელში",
                "კანიონში ბინდი ჩამოწვა. თამარიკოს გზა ვეღარ დაუნახავს. მაშინ ციცინათელები აინთნენ. "
                + "მათი ოქროსფერი შუქი დამალულ ბილიკზე აციმციმდა."),
            ("ოჯახის შეხვედრა", "დინოზავრების მხიარული ღრიალი",
                "ბილიკმა ფართო ველზე გამოიყვანა ისინი. იქ ნუნუს ოჯახი ელოდებოდა. "
                + "ნუნუ სიხარულით მათკენ გაიქცა. დიდი დინოზავრები თამარიკოს თბილი ღრიალით მიესალმნენ."),
            ("მეგობრის საჩუქარი", "ნახვამდის, ნუნუ",
                "ნუნუმ თამარიკოს პატარა, ფერადი ქვა აჩუქა. „როცა მოგენატრები, ეს ქვა ნახე,“ თითქოს ეუბნებოდა. "
                + "თამარიკომ ხელი დაუქნია და კარიბჭისკენ წავიდა. დაკარგული ხეობა ახლა მის გულში ცხოვრობდა.")
        ];

        var pages = new List<StoryPageDto>();

        foreach (var (title, caption, text) in spreads)
        {
            pages.Add(new StoryPageDto
            {
                Title = title,
                Caption = caption,
                Content = string.Empty,
                IsTextOnlyPage = false,
                ImageBytes = PixelPng()
            });

            pages.Add(new StoryPageDto
            {
                Title = title,
                Caption = caption,
                Content = text,
                IsTextOnlyPage = true
            });
        }

        return new AdventureContentDto
        {
            Title = "თამარიკო დაკარგულ ხეობაში",
            ChildName = "თამარიკო",
            StoryPages = pages
        };
    }

    // ---- Helpers --------------------------------------------------------

    private static byte[] Render(
        AdventureContentDto content,
        byte[]? cover,
        Action<PrintLayoutOptions>? configure = null,
        string language = "en",
        bool forPrint = false)
    {
        var layout = new PrintLayoutOptions();
        configure?.Invoke(layout);

        var service = new AdventurePdfService(Options.Create(layout));
        return service.GeneratePdf(new PdfBookRequest
        {
            Content = content,
            ThemeName = "Dinosaurs",
            CoverImage = cover,
            Language = language,
            // Without a destination the back cover draws no QR, so the sample would show a
            // closing page that is missing the one thing the printed book needs.
            ContinueUrl = "https://adventrya.com/create",
            ForPrint = forPrint
        });
    }

    /// <summary>Eight spreads: a picture page and the prose page facing it, sharing a title.</summary>
    private static AdventureContentDto SpreadBook()
    {
        var pages = new List<StoryPageDto>();

        for (var spread = 1; spread <= 8; spread++)
        {
            var title = $"Spread {spread}";
            var caption = $"Caption {spread}";

            pages.Add(new StoryPageDto
            {
                Title = title,
                Caption = caption,
                Content = string.Empty,
                IsTextOnlyPage = false,
                ImageBytes = PixelPng()
            });

            pages.Add(new StoryPageDto
            {
                Title = title,
                Caption = caption,
                Content = $"The prose of spread {spread}, which is the half a child hears read aloud.",
                IsTextOnlyPage = true
            });
        }

        return new AdventureContentDto
        {
            Title = "The Lost Valley",
            ChildName = "Tamar",
            StoryPages = pages
        };
    }

    /// <summary>
    /// A tiny 2:3 PNG, matching the shape the image model is asked for.
    ///
    /// It was a 1x1 square, which exercised the image path but made the rendered sample lie: a
    /// square letterboxes deep on a portrait page, so the proofs showed a band of empty page
    /// colour that a real illustration does not leave.
    /// </summary>
    private static byte[] PixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAMCAIAAADQ/GvKAAAASklEQVR4nG3CIRWAQAAFwU1CAxrQgAar"
        + "MRj0aQwGfRpzBn0agyEWBf68YfCLGX1iJnvM7B2jV8xqjSmeMYd7TLXENLeY7hLzavwDJZqCUR1CwS0A");

    /// <summary>
    /// Counts pages from the page tree. The <c>/Count</c> entry on the root <c>/Pages</c> node is
    /// the document's own answer, which is the one a printer will read.
    /// </summary>
    private static int CountPages(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var counts = System.Text.RegularExpressions.Regex
            .Matches(raw, @"/Type\s*/Pages[^>]*?/Count\s+(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        if (counts.Count == 0)
        {
            // Some writers order the dictionary the other way round.
            counts = System.Text.RegularExpressions.Regex
                .Matches(raw, @"/Count\s+(\d+)[^>]*?/Type\s*/Pages")
                .Select(m => int.Parse(m.Groups[1].Value))
                .ToList();
        }

        Assert.NotEmpty(counts);
        return counts.Max();
    }

    private static (double Width, double Height) FirstMediaBox(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var match = System.Text.RegularExpressions.Regex.Match(
            raw, @"/MediaBox\s*\[\s*([\d.]+)\s+([\d.]+)\s+([\d.]+)\s+([\d.]+)\s*\]");

        Assert.True(match.Success, "No /MediaBox in the generated PDF.");

        return (
            double.Parse(match.Groups[3].Value) - double.Parse(match.Groups[1].Value),
            double.Parse(match.Groups[4].Value) - double.Parse(match.Groups[2].Value));
    }

}
