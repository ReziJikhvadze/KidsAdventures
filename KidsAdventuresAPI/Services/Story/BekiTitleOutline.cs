using System.Globalization;
using System.Text;
using AdventurePacks.Api.Services.Pdf;
using AdventurePacks.Api.Services.Story.Composite;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// The rim on the cover title, as a real stroked outline rather than a stack of offset copies.
///
/// Owner ruling 2026-09-01, rule 3 — "text must have a STRONGER border so it is readable on all
/// backgrounds; when in doubt thicken the rim" — and the feedback that reopened it: cream
/// (<c>#FFF8EB</c>) Ottia straight onto a pale sky is a title nobody can read. The rim it asks for
/// used to be drawn by painting the same glyphs sixteen more times on a small circle, which the
/// supplier's <c>SINGLE_TEXT_LAYER</c> gate now refuses by name, so
/// <see cref="BekiPdfComposer"/> was left setting the title with no border at all.
///
/// PDF has had the right answer since 1.0 and QuestPDF simply cannot ask for it: text rendering
/// mode 2 fills the glyph AND strokes its outline, from one text object and one set of glyphs. So
/// the treatment is applied where the plumbing to apply it already exists — after QuestPDF is
/// finished, on the finished page, exactly as <see cref="BekiVectorLogo"/> puts the approved logo
/// on as native paths. The title's text object is wrapped in <c>q … Q</c> and given
/// <c>2 Tr</c>, a pen width, the rim ink and round joins and caps; nothing inside the object moves,
/// and the state cannot leak past the <c>Q</c>.
///
/// Two properties of that treatment are what make it shippable where the offset stack was not:
///
/// * <b>One text layer.</b> The glyphs are shown once. <c>SINGLE_TEXT_LAYER</c> counts text-showing
///   operators and finds exactly what the layout receipt budgeted.
/// * <b>The fill is untouched.</b> <c>2 Tr</c> strokes in ADDITION to filling, and the fill colour
///   in force is still the cream QuestPDF authored — which is the colour
///   <c>TEXT_COLOR_INTEGRITY</c> reads out of the content stream. A rim that had replaced the fill
///   would have hidden audit P0-07's defect instead of the previous rim's.
/// </summary>
public static class BekiTitleOutline
{
    /// <summary>
    /// The licensed display face the cover title is set in, by file name — the same entry
    /// <see cref="PdfFontBootstrap.BekiFontWhitelist"/> permits.
    ///
    /// A font's PostScript name is what a page's font resource carries, and for these subsets it is
    /// this file's stem behind a six-letter subset tag (<c>AAAAAA+Ottia-v01-Regular</c>). Naming the
    /// file rather than the family is deliberate: <see cref="PdfFontBootstrap.TitleFamily"/> is the
    /// name QuestPDF was asked for, and it does not survive into the PDF.
    /// </summary>
    public const string TitleFaceFileName = "Ottia-v01-Regular.ttf";

    /// <summary>
    /// Strokes the cover title on page 1 of <paramref name="pdf"/> and returns the new document.
    ///
    /// Fails closed. A cover page with no title set in the licensed face is not a cover this method
    /// should quietly hand back unchanged — it is either a layout that stopped using the face or a
    /// caller pointing this at the wrong document, and both are worth stopping for.
    /// </summary>
    /// <param name="outlineInkHex">The rim ink, <c>RRGGBB</c> with or without its hash.</param>
    /// <param name="strokeWidthPt">
    /// The pen width on the finished page, in points, centred on the glyph outline — so half of it
    /// reaches outside the letter and half of it eats into the fill.
    /// </param>
    public static byte[] Apply(byte[] pdf, string outlineInkHex, double strokeWidthPt)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        if (!(strokeWidthPt > 0d))
        {
            return pdf;
        }

        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var titleFonts = TitleFontResourceNames(page);

        if (titleFonts.Count == 0)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The cover page carries no font resource for {TitleFaceFileName}, so the title's "
                + "rim cannot be stroked onto it. A cover whose title is not set in the licensed "
                + "display face is a defect in its own right.");
        }

        // One buffer for the whole page, because a q/Q pair is free to open in one content stream
        // and close in the next: the array is a single stream cut into pieces, and the graphics
        // state this walks does not restart at the cuts.
        var content = Encoding.Latin1.GetString(page.Contents.CreateSingleContent().Stream.UnfilteredValue);
        var stroked = Stroke(content, titleFonts, Ink(outlineInkHex), strokeWidthPt, page);

        if (stroked is null)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                "The cover page's content stream shows no text in the licensed display face, so "
                + "the title's rim was not applied. The owner's readability ruling is not "
                + "something to fail silently on.");
        }

        // The array is emptied and one stream put back rather than each piece being edited in
        // place: a text object may straddle two of the pieces, and the page is the concatenation
        // either way. The objects left behind are unreferenced and do not reach the saved file.
        page.Contents.Elements.Clear();
        page.Contents.AppendContent().CreateStream(Encoding.Latin1.GetBytes(stroked));

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    /// <summary>
    /// The page's font resource keys — <c>/F6</c> and the like — that resolve to the title face.
    /// </summary>
    private static HashSet<string> TitleFontResourceNames(PdfPage page)
    {
        var face = Path.GetFileNameWithoutExtension(TitleFaceFileName);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var fonts = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font");

        if (fonts is null)
        {
            return names;
        }

        foreach (var key in fonts.Elements.Keys)
        {
            var font = fonts.Elements.GetDictionary(key);
            var baseFont = font?.Elements.GetName("/BaseFont") ?? string.Empty;
            if (string.IsNullOrEmpty(baseFont))
                baseFont = font?.Elements.GetDictionary("/FontDescriptor")?.Elements.GetName("/FontName") ?? string.Empty;

            // "/AAAAAA+Ottia-v01-Regular" — the subset tag is the embedder's, and it changes.
            var plus = baseFont.IndexOf('+');
            var stem = plus >= 0 ? baseFont[(plus + 1)..] : baseFont.TrimStart('/');

            if (stem.Equals(face, StringComparison.OrdinalIgnoreCase))
            {
                names.Add(key);
            }
        }

        return names;
    }

    /// <summary>
    /// The same content stream with every text object set in <paramref name="titleFonts"/> wrapped
    /// in a stroking graphics state, or null if there was no such object to wrap.
    ///
    /// The wrapper goes OUTSIDE the <c>BT … ET</c>, not inside it: <c>q</c> and <c>Q</c> are special
    /// graphics state operators and PDF 32000-1 table 51 forbids them inside a text object. Every
    /// operator that does go in — <c>2 Tr</c>, the pen, the ink, the joins — is legal in both
    /// places, and putting the save around the whole object is what guarantees the state is exactly
    /// as QuestPDF left it by the time the next object is painted.
    /// </summary>
    private static string? Stroke(
        string content, HashSet<string> titleFonts, string ink, double strokeWidthPt, PdfPage page)
    {
        var operands = new List<string>();
        var saved = new Stack<double>();
        var edits = new List<(int Offset, string Text)>();
        var scale = 1d;
        var inText = false;
        var isTitle = false;
        var textStart = -1;
        var textScale = 1d;
        var blockScale = 1d;
        var titleFont = string.Empty;
        var fontSize = 1d;
        var strokedGlyphFonts = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (start, end, token) in Tokenize(content))
        {
            if (IsOperand(token))
            {
                operands.Add(token);
                continue;
            }

            switch (token)
            {
                case "q":
                    saved.Push(scale);
                    break;

                case "Q":
                    if (saved.Count > 0) scale = saved.Pop();
                    break;

                case "cm":
                    scale *= MatrixScale(operands);
                    break;

                case "BT":
                    inText = true;
                    isTitle = false;
                    textStart = start;
                    blockScale = scale;
                    textScale = 1d;
                    break;

                case "Tf":
                    if (inText && operands.Count >= 2 && titleFonts.Contains(operands[^2]))
                    {
                        isTitle = true;
                        titleFont = operands[^2];
                        fontSize = double.Parse(operands[^1], CultureInfo.InvariantCulture);
                    }

                    break;

                case "Tm":
                    // The pen is transformed by the text matrix as well as by the CTM, so a title
                    // set through a scaling Tm would be rimmed at the wrong weight. Today both
                    // covers set 1 0 0 -1 — a flip, scale one — and this arithmetic is an identity;
                    // it is here so that it stays an identity if a layout starts scaling.
                    if (inText) textScale = MatrixScale(operands);
                    break;

                case "ET":
                    if (inText && isTitle)
                    {
                        var pen = strokeWidthPt / Math.Max(blockScale * textScale, 1e-9d);
                        if (strokedGlyphFonts.Add(titleFont))
                            StrokeSynthesizedGlyphs(page, titleFont, ink, pen / fontSize);
                        edits.Add((textStart,
                            $"q 2 Tr {F(pen)} w {ink} RG 1 j 1 J\n"));
                        edits.Add((end, "\nQ"));
                    }

                    inText = false;
                    break;
            }

            operands.Clear();
        }

        if (edits.Count == 0)
        {
            return null;
        }

        var output = new StringBuilder(content.Length + (edits.Count * 48));
        var cursor = 0;

        foreach (var (offset, text) in edits)
        {
            output.Append(content, cursor, offset - cursor).Append(text);
            cursor = offset;
        }

        return output.Append(content, cursor, content.Length - cursor).ToString();
    }

    // Skia's synthetic bold retains the licensed outlines as embedded Type 3 glyph programs.
    // Type 3 ignores Tr, so stroke each glyph's existing path instead of drawing the title twice.
    private static void StrokeSynthesizedGlyphs(PdfPage page, string name, string ink, double penPerEm)
    {
        var font = page.Elements.GetDictionary("/Resources")?.Elements.GetDictionary("/Font")?.Elements.GetDictionary(name);
        if (font?.Elements.GetName("/Subtype") != "/Type3") return;
        var matrix = font.Elements.GetArray("/FontMatrix")
            ?? throw new InvalidOperationException("The synthesized cover font has no matrix.");
        var scale = Math.Sqrt(Math.Abs(matrix.Elements.GetReal(0) * matrix.Elements.GetReal(3)
                                       - matrix.Elements.GetReal(1) * matrix.Elements.GetReal(2)));
        if (!(scale > 0)) throw new InvalidOperationException("The synthesized cover font has an invalid matrix.");
        var glyphs = font.Elements.GetDictionary("/CharProcs")
            ?? throw new InvalidOperationException("The synthesized cover font has no embedded glyphs.");
        foreach (var key in glyphs.Elements.Keys.ToList())
        {
            var glyph = glyphs.Elements.GetDictionary(key);
            if (glyph?.Stream is null) continue;
            var source = Encoding.Latin1.GetString(glyph.Stream.UnfilteredValue);
            var result = new StringBuilder();
            var cursor = 0;
            var pathStart = 0;
            foreach (var (start, end, token) in Tokenize(source))
            {
                if (token == "d1")
                {
                    // d1 is a single-colour stencil: viewers ignore the dark rim's ink.
                    // d0 permits the glyph's cream fill and dark stroke, retaining its advance.
                    var metrics = source[..start].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    result.Append(metrics[0]).Append(' ').Append(metrics[1]).Append(" d0");
                    cursor = end;
                    pathStart = end;
                    continue;
                }
                if (token is not ("f" or "f*")) continue;
                result.Append(source, cursor, start - cursor);
                result.Append($"q {F(penPerEm / scale)} w {ink} RG 1 j 1 J ");
                // Synthetic bold contains overlapping contours. Stroke first, then fill the
                // complete glyph so internal contour edges cannot make it look hollow again.
                result.Append("S Q\n").Append(source, pathStart, start - pathStart).Append(token);
                cursor = end;
                pathStart = end;
            }
            result.Append(source, cursor, source.Length - cursor);
            glyph.Stream.Value = Encoding.Latin1.GetBytes(result.ToString());
            glyph.Elements.Remove("/Filter");
            glyph.Elements.Remove("/DecodeParms");
        }
    }

    /// <summary>
    /// How much a <c>cm</c> or <c>Tm</c> operand list scales lengths: the square root of its
    /// determinant, which is the one number a pen width can be divided by when the matrix is a
    /// rotation, a flip or a uniform scale — all this book's matrices ever are.
    /// </summary>
    private static double MatrixScale(List<string> operands)
    {
        if (operands.Count < 6) return 1d;

        var values = new double[4];
        for (var index = 0; index < 4; index++)
        {
            if (!double.TryParse(
                    operands[operands.Count - 6 + index],
                    NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]))
            {
                return 1d;
            }
        }

        var determinant = Math.Abs((values[0] * values[3]) - (values[1] * values[2]));
        return determinant > 0d ? Math.Sqrt(determinant) : 1d;
    }

    /// <summary>The rim ink as PDF operands: three components, zero to one.</summary>
    private static string Ink(string hex)
    {
        var value = hex.TrimStart('#');

        if (value.Length != 6)
        {
            throw new BekiLayoutException(
                CompositeFailureCodes.LayoutFailed,
                $"The cover title's rim ink '{hex}' is not an RRGGBB colour.");
        }

        return string.Join(" ", Enumerable.Range(0, 3)
            .Select(channel => F(Convert.ToInt32(value.Substring(channel * 2, 2), 16) / 255d)));
    }

    private static string F(double value) =>
        value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a token is something an operator consumes rather than the operator itself. Names,
    /// numbers and the array and dictionary brackets are operands; everything else is an operator,
    /// which is the same rule a content stream's own grammar uses.
    /// </summary>
    private static bool IsOperand(string token) =>
        token.Length > 0 && (token[0] is '/' or '[' or ']' or '<' or '>' or '+' or '-' or '.'
                             || char.IsAsciiDigit(token[0]));

    /// <summary>
    /// The content stream's tokens, with the offsets an edit needs — enough of a lexer to never
    /// mistake the bytes inside a string for an operator, and no more than that.
    ///
    /// Strings, comments and inline-image data yield no token: nothing this walks reads a string
    /// operand, and a token that is never looked at is a token that cannot be misread.
    /// </summary>
    private static IEnumerable<(int Start, int End, string Token)> Tokenize(string content)
    {
        var index = 0;

        while (index < content.Length)
        {
            var character = content[index];

            if (IsWhitespace(character))
            {
                index++;
                continue;
            }

            if (character == '%')
            {
                while (index < content.Length && content[index] is not ('\n' or '\r')) index++;
                continue;
            }

            if (character == '(')
            {
                index = SkipLiteralString(content, index);
                continue;
            }

            if (character == '<')
            {
                if (index + 1 < content.Length && content[index + 1] == '<')
                {
                    yield return (index, index + 2, "<<");
                    index += 2;
                    continue;
                }

                while (index < content.Length && content[index] != '>') index++;
                index++;
                continue;
            }

            if (character == '>')
            {
                if (index + 1 < content.Length && content[index + 1] == '>')
                {
                    yield return (index, index + 2, ">>");
                    index += 2;
                    continue;
                }

                index++;
                continue;
            }

            if (character is '[' or ']' or '{' or '}')
            {
                yield return (index, index + 1, content[index].ToString());
                index++;
                continue;
            }

            var start = index;
            if (character == '/') index++;
            while (index < content.Length
                   && !IsWhitespace(content[index])
                   && !IsDelimiter(content[index]))
            {
                index++;
            }

            if (index == start) index++;
            yield return (start, index, content[start..index]);
        }
    }

    /// <summary>The index just past a balanced <c>( … )</c> string, escapes and nesting included.</summary>
    private static int SkipLiteralString(string content, int index)
    {
        var depth = 0;

        while (index < content.Length)
        {
            var character = content[index];

            if (character == '\\')
            {
                index += 2;
                continue;
            }

            index++;

            if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0)
            {
                return index;
            }
        }

        return index;
    }

    private static bool IsWhitespace(char character) =>
        character is ' ' or '\t' or '\r' or '\n' or '\f' or '\0';

    private static bool IsDelimiter(char character) =>
        character is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';
}
