using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// A heading, set the way the product sets headings: the book's own display face, in mtavruli, at
/// the heaviest weight the face can be asked for.
///
/// It exists because that ruling has four call sites in one book and had drifted at every one of
/// them. The printed A5 set its headings in Noto Serif Georgian SemiBold — a face chosen for
/// nothing but "not the body face", asked for at <c>Bold</c> that the SemiBold cut quietly
/// absorbed, in mkhedruli — while the screen, the composite book's cover and the store pages had
/// all moved to Ottia mtavruli. A reader met one book in two typographies depending on which file
/// they opened.
///
/// Three decisions, in one place so they cannot drift again:
///
/// * <b>The face.</b> <see cref="PdfFontBootstrap.TitleFamily"/>, with
///   <see cref="PdfFontBootstrap.BodyFamily"/> behind it — never alone. Ottia carries the
///   thirty-three Georgian letters and none of the punctuation a title uses, so a face named on
///   its own prints a box where the dash or the digit should be. QuestPDF walks the list per
///   glyph, so the letters stay Ottia and the dash is borrowed.
/// * <b>Mtavruli, through the font's own <c>case</c> feature</b> rather than through
///   <c>ToUpperInvariant</c>. It is the same mechanism the site uses (<c>--display-caps</c> in
///   CSS), so the two agree by construction; and it leaves a Latin title alone, where uppercasing
///   would have shouted an English book's name that the screen sets in mixed case.
/// * <b>The weight.</b> Ottia ships one cut, Regular. Skia synthesizes the rest and embeds the
///   result as Type 3 glyphs — the same treatment the composite book's cover title already ships,
///   and worth the trade: at an unchanged advance width it lays down about 27% more ink, which is
///   what a heading needs and what a 400-weight display face was not giving. Nothing above
///   <c>ExtraBold</c>: the synthesis is one fixed amount whatever is asked for, and asking for
///   <c>Black</c> would only push the borrowed punctuation off its real Bold cut onto a
///   synthesized one for no visible gain.
/// </summary>
internal static class PdfDisplayText
{
    /// <summary>
    /// The OpenType feature that turns Georgian mkhedruli into mtavruli, and the one the site's
    /// <c>--display-caps</c> sets.
    /// </summary>
    private const string MtavruliFeature = "case";

    /// <summary>Sets <paramref name="text"/> as a heading at <paramref name="fontSize"/> points.</summary>
    public static TextSpanDescriptor DisplayHeading(this TextSpanDescriptor text, float fontSize) =>
        text.FontFamily(PdfFontBootstrap.TitleFamily, PdfFontBootstrap.BodyFamily)
            .FontSize(fontSize)
            .ExtraBold()
            .EnableFontFeature(MtavruliFeature);
}
