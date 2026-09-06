using AdventurePacks.Api.Services.Pdf;

namespace Adventrya.Story.Tests;

/// <summary>
/// The name a downloaded book arrives under.
///
/// A file name is the only part of a book a parent sees inside their own operating system, and
/// this was the row's primary key. What is pinned here is the narrow line the sanitiser has to
/// walk: strip what a file system genuinely refuses, and nothing else — a Georgian title is a
/// perfectly legal name and it is the one on the cover.
/// </summary>
public class PdfFileNamesTests
{
    private const string Fallback = "beki-1234-book.pdf";

    [Fact]
    public void A_Georgian_title_survives_whole()
    {
        Assert.Equal(
            "ვერიკო და ღრუბლების ქალაქი.pdf",
            PdfFileNames.ForBook("ვერიკო და ღრუბლების ქალაქი", null, Fallback));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("...")]
    public void A_title_that_is_not_a_name_falls_back_to_the_spelling_the_route_always_had(string? title)
    {
        Assert.Equal(Fallback, PdfFileNames.ForBook(title, " — print", Fallback));
        Assert.Equal(Fallback, PdfFileNames.AsciiForBook(title, " — print", Fallback));
    }

    [Fact]
    public void Only_the_characters_a_file_system_refuses_are_removed()
    {
        // The reserved punctuation goes; the apostrophe, the comma and the parentheses stay,
        // because a title is allowed to contain them and a file name is allowed to keep them.
        Assert.Equal(
            "Nina's day (part 2), lost & found.pdf",
            PdfFileNames.ForBook("Nina's day: (part 2), lost & found?", null, Fallback));
    }

    [Fact]
    public void A_control_character_never_reaches_the_header()
    {
        var name = PdfFileNames.ForBook("Nina\r\nSet-Cookie: x=1", null, Fallback);

        Assert.Equal("NinaSet-Cookie x=1.pdf", name);
        Assert.DoesNotContain('\n', name);
    }

    [Fact]
    public void A_very_long_title_is_cut_short_of_the_file_system_limit()
    {
        // Georgian costs three UTF-8 bytes a letter, so a title that looks harmless in characters
        // is most of the 255-byte budget before the suffix is added.
        var name = PdfFileNames.ForBook(new string('ა', 300), " — print", Fallback);

        Assert.EndsWith(" — print.pdf", name);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(name) < 255);
    }

    [Fact]
    public void The_suffix_keeps_the_two_console_files_apart()
    {
        Assert.Equal("ზუკა — print.pdf", PdfFileNames.ForBook("ზუკა", " — print", Fallback));
        Assert.Equal(
            "ზუკა — reading copy (not print).pdf",
            PdfFileNames.ForBook("ზუკა", " — reading copy (not print)", Fallback));
    }

    [Fact]
    public void The_ascii_parameter_transliterates_rather_than_blanking_the_title()
    {
        // ASP.NET's own SetHttpFileName would write _______.pdf here, which is what this exists
        // to avoid: a client that ignores filename* still gets a name it can read.
        Assert.Equal(
            "zuka-da-dinozavrebi.pdf",
            PdfFileNames.AsciiForBook("ზუკა და დინოზავრები", null, Fallback));
    }

    [Fact]
    public void The_ascii_parameter_is_ascii_and_never_runs_separators_together()
    {
        var name = PdfFileNames.AsciiForBook("ზუკა !!! —  და 42", null, Fallback);

        Assert.Equal("zuka-da-42.pdf", name);
        Assert.All(name, character => Assert.True(char.IsAscii(character)));
    }

    [Fact]
    public void The_header_carries_both_spellings()
    {
        var header = PdfFileNames.Attachment("ზუკა.pdf", "beki-1234-book.pdf").ToString();

        Assert.StartsWith("attachment;", header);
        Assert.Contains("filename=beki-1234-book.pdf", header);
        Assert.Contains("filename*=UTF-8''", header);
        Assert.DoesNotContain('ზ', header);
    }
}
