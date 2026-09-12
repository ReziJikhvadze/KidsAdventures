using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;

namespace AdventurePacks.Api.Services.Story;

/// <summary>
/// Expands Ottia's already-shaped synthetic-bold glyph programs into ordinary page paths.
/// Georgian shaping, line breaks, advances and outlines remain exactly those authored by QuestPDF.
/// No font interpretation is left to the customer's PDF viewer.
/// </summary>
internal static class BekiTitlePaths
{
    internal static void Expand(PdfPage page, IReadOnlySet<string> titleFonts)
    {
        var fonts = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        var output = new StringBuilder();
        PdfDictionary? font = null;
        double size = 0, advance = 0;
        double[] matrix = [1, 0, 0, 1, 0, 0];
        var inText = false;
        var glyphCount = 0;

        foreach (var op in ContentReader.ReadContent(page).OfType<COperator>())
        {
            var args = op.Operands;
            switch (op.Name)
            {
                case "BT":
                    inText = true;
                    font = null;
                    advance = 0;
                    matrix = [1, 0, 0, 1, 0, 0];
                    output.AppendLine("q");
                    break;
                case "ET":
                    inText = false;
                    output.AppendLine("Q");
                    break;
                case "Tr": // The rim is already in each glyph program; paths have no text mode.
                    break;
                case "Tf" when inText:
                    var name = ((CName)args[0]).Name;
                    font = fonts.Elements.GetDictionary(name);
                    if (!titleFonts.Contains(name) || font?.Elements.GetName("/Subtype") != "/Type3")
                        throw new InvalidOperationException("Cover outlines require the shaped synthetic-bold Ottia glyph programs; a live font cannot be exported.");
                    size = Number(args[1]);
                    break;
                case "Tm" when inText:
                    matrix = args.Select(Number).ToArray();
                    advance = 0;
                    break;
                case "Td" when inText:
                    var x = Number(args[0]);
                    var y = Number(args[1]);
                    matrix[4] += matrix[0] * x + matrix[2] * y;
                    matrix[5] += matrix[1] * x + matrix[3] * y;
                    advance = 0;
                    break;
                case "Tj" when inText:
                    Show((CString)args[0]);
                    break;
                case "TJ" when inText:
                    foreach (var part in (CArray)args[0])
                        if (part is CString text) Show(text);
                        else advance -= Number(part) * size / 1000d;
                    break;
                default:
                    if (inText)
                        throw new InvalidOperationException($"Unsupported cover text operator {op.Name}; refusing to change shaped title placement.");
                    output.Append(Serialize(op));
                    break;
            }
        }

        if (glyphCount == 0) throw new InvalidOperationException("No cover title glyphs were converted to paths.");
        page.Contents.Elements.Clear();
        page.Contents.AppendContent().CreateStream(Encoding.Latin1.GetBytes(output.ToString()));
        // A fresh resource dictionary avoids altering font resources shared with interior pages.
        var resources = page.Elements.GetDictionary("/Resources")!;
        var ownResources = new PdfDictionary(page.Owner);
        foreach (var key in resources.Elements.Keys) ownResources.Elements[key] = resources.Elements[key];
        ownResources.Elements.Remove("/Font");
        page.Elements["/Resources"] = ownResources;

        void Show(CString text)
        {
            if (font is null) throw new InvalidOperationException("Cover text has no Ottia font.");
            var encoding = font.Elements.GetDictionary("/Encoding")!.Elements.GetArray("/Differences")!;
            var names = new Dictionary<int, string>();
            var code = 0;
            foreach (var entry in encoding.Elements)
                if (entry is PdfInteger number) code = number.Value;
                else if (entry is PdfName name) names[code++] = name.Value;
            var fm = font.Elements.GetArray("/FontMatrix")!;
            var transform = Enumerable.Range(0, 6).Select(fm.Elements.GetReal).ToArray();
            var widths = font.Elements.GetArray("/Widths")!;
            var first = font.Elements.GetInteger("/FirstChar");
            var procs = font.Elements.GetDictionary("/CharProcs")!;
            foreach (var character in text.Value)
            {
                if (!names.TryGetValue(character, out var glyphName))
                    throw new InvalidOperationException("The cover title references a missing glyph.");
                var glyph = procs.Elements.GetDictionary(glyphName)!;
                // PDFsharp's general content lexer reads d0/d1 as d + a number. Remove
                // the glyph-only metrics before parsing ordinary path operators.
                var source = Encoding.Latin1.GetString(glyph.Stream.UnfilteredValue);
                var metrics = Regex.Match(source, @"\A\s*(?:[-+.\deE]+\s+){2,6}d[01]\b");
                if (!metrics.Success) throw new InvalidOperationException("Cover glyph has no valid metrics header.");
                var program = ContentReader.ReadContent(Encoding.Latin1.GetBytes(source[metrics.Length..]));
                output.AppendLine("/BekiTitleGlyph BMC");
                output.AppendLine("q");
                output.AppendLine(string.Join(" ", matrix.Select(F)) + " cm");
                output.AppendLine($"{F(size)} 0 0 {F(size)} {F(advance)} 0 cm");
                output.AppendLine(string.Join(" ", transform.Select(F)) + " cm");
                foreach (var command in program.OfType<COperator>())
                {
                    if (command.Name is "d0" or "d1") continue;
                    if (command.Name is not ("m" or "l" or "c" or "v" or "y" or "h" or "re"
                        or "f" or "f*" or "S" or "s" or "B" or "B*" or "b" or "b*" or "n"
                        or "q" or "Q" or "cm" or "w" or "j" or "J" or "M" or "d"
                        or "rg" or "RG" or "g" or "G" or "k" or "K"))
                        throw new InvalidOperationException($"Cover glyph contains non-path operator {command.Name}.");
                    output.Append(Serialize(command));
                }
                output.AppendLine("Q\nEMC");
                advance += widths.Elements.GetReal(character - first) * transform[0] * size;
                glyphCount++;
            }
        }
    }

    private static string Serialize(COperator op)
    {
        var sequence = new CSequence { op };
        return Encoding.Latin1.GetString(sequence.ToContent());
    }
    private static double Number(CObject value) => value switch
    {
        CInteger integer => integer.Value,
        CReal real => real.Value,
        _ => throw new InvalidOperationException("Expected a numeric cover text operand.")
    };
    private static string F(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
}
