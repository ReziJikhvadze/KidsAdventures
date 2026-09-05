using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using AdventurePacks.Api.Services.Story.Composite;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// Translates the hash-locked logo's paths and exact axial gradient to native PDF operators.
/// No rasterization, band approximation, recolouring or geometric edits are involved.
/// This deliberately supports only the approved SVG's vocabulary; new artwork fails closed.
/// </summary>
public static class BekiVectorLogo
{
    // Exact cubic-Bezier extrema of the hash-locked colored asset; not its padded viewBox.
    public const double VisibleMinX = 0.0010303793911205644;
    public const double VisibleMinY = 644.6859999999998;
    public const double VisibleWidth = 1999.999969620609;
    public const double VisibleHeight = 710.628556858726;
    public const string ApprovedSha256 = "da8f2fdedfeb203f5dbcc8911f94747713c843ee58f1155b252a219f5ce6a43f";
    /// <summary>
    /// Ghostscript rasterizes RGB shadings when converting them to a different process colour
    /// space. Convert the gradient's colour function through the very same ICC pipeline first,
    /// keeping its exact paths, clipping, axis and domain. A 16-bit sampled CMYK function is
    /// still native continuous vector shading, not a stack of painted colour bands.
    /// </summary>
    internal static byte[] PrepareForCmyk(byte[] pdf, Func<byte[], byte[]> convertColourSamples)
    {
        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var shading = document.Pages[0].Elements.GetDictionary("/Resources")?
            .Elements.GetDictionary("/Shading")?.Elements.GetDictionary("/BekiApprovedLogo");
        if (shading is null || shading.Elements.GetName("/ColorSpace") != "/DeviceRGB") return pdf;
        var source = shading.Elements.GetDictionary("/Function")!;
        var c0 = source.Elements.GetArray("/C0")!;
        var c1 = source.Elements.GetArray("/C1")!;
        const int samples = 1025;
        using var swatches = new PdfDocument();
        for (var index = 0; index < samples; index++)
        {
            var page = swatches.AddPage();
            page.Width = PdfSharp.Drawing.XUnit.FromPoint(10);
            page.Height = PdfSharp.Drawing.XUnit.FromPoint(10);
            page.Elements["/Resources"] = new PdfDictionary(swatches);
            var t = (double)index / (samples - 1);
            var rgb = Enumerable.Range(0, 3).Select(channel =>
                c0.Elements.GetReal(channel) * (1 - t) + c1.Elements.GetReal(channel) * t);
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(
                string.Join(" ", rgb.Select(F)) + " rg 0 0 10 10 re f\n"));
        }
        using var swatchBytes = new MemoryStream();
        swatches.Save(swatchBytes);
        using var convertedBytes = new MemoryStream(convertColourSamples(swatchBytes.ToArray()));
        using var converted = PdfReader.Open(convertedBytes, PdfDocumentOpenMode.Modify);
        if (converted.PageCount != samples) throw new InvalidOperationException("Logo ICC samples were lost.");
        var values = new byte[samples * 4 * 2];
        for (var index = 0; index < samples; index++)
        {
            var colour = ContentReader.ReadContent(converted.Pages[index]).OfType<COperator>()
                .LastOrDefault(op => op.OpCode.Name == "k")
                ?? throw new InvalidOperationException("Logo ICC sample is not process CMYK.");
            if (colour.Operands.Count != 4) throw new InvalidOperationException("Invalid CMYK sample.");
            for (var channel = 0; channel < 4; channel++)
            {
                var value = colour.Operands[channel] switch
                {
                    CReal real => real.Value, CInteger integer => integer.Value,
                    _ => throw new InvalidOperationException("Invalid CMYK component."),
                };
                var encoded = (ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue);
                values[(index * 4 + channel) * 2] = (byte)(encoded >> 8);
                values[(index * 4 + channel) * 2 + 1] = (byte)(encoded & 255);
            }
        }
        var function = new PdfDictionary(document);
        function.Elements.SetInteger("/FunctionType", 0);
        function.Elements.SetInteger("/BitsPerSample", 16);
        function.Elements["/Size"] = new PdfLiteral($"[{samples}]");
        function.Elements["/Domain"] = new PdfLiteral("[0 1]");
        function.Elements["/Range"] = new PdfLiteral("[0 1 0 1 0 1 0 1]");
        function.CreateStream(values);
        document.Internals.AddObject(function);
        shading.Elements.SetName("/ColorSpace", "/DeviceCMYK");
        shading.Elements["/Function"] = function.Reference!;
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    public static byte[] Apply(byte[] pdf, byte[] approvedSvg)
    {
        if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(approvedSvg))
            != ApprovedSha256.ToUpperInvariant())
            throw new InvalidOperationException("The cover logo is not the approved HiResColor.svg.");

        using var svgStream = new MemoryStream(approvedSvg);
        using var reader = XmlReader.Create(svgStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null,
        });
        var svg = XDocument.Load(reader);
        XNamespace ns = "http://www.w3.org/2000/svg";
        var gradient = svg.Descendants(ns + "linearGradient").Single();
        var matrix = Numbers((string)gradient.Attribute("gradientTransform")!).ToArray();
        // The locked gradient is (0,0) -> (1,0) in userSpaceOnUse, transformed by this matrix.
        var coords = new[] { matrix[4], matrix[5], matrix[0] + matrix[4], matrix[1] + matrix[5] };

        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var page = document.Pages[0];
        var function = new PdfDictionary(document);
        function.Elements.SetInteger("/FunctionType", 2);
        function.Elements["/Domain"] = new PdfLiteral("[0 1]");
        var stops = gradient.Elements(ns + "stop").ToArray();
        function.Elements["/C0"] = new PdfLiteral("[" + Colour((string)stops[0].Attribute("style")!) + "]");
        function.Elements["/C1"] = new PdfLiteral("[" + Colour((string)stops[1].Attribute("style")!) + "]");
        function.Elements.SetInteger("/N", 1);
        var shading = new PdfDictionary(document);
        shading.Elements.SetInteger("/ShadingType", 2);
        shading.Elements.SetName("/ColorSpace", "/DeviceRGB");
        shading.Elements["/Coords"] = new PdfLiteral("[" + string.Join(" ", coords.Select(F)) + "]");
        shading.Elements["/Function"] = function;
        shading.Elements["/Extend"] = new PdfLiteral("[true true]");
        document.Internals.AddObject(shading);
        var resources = page.Elements.GetDictionary("/Resources")
            ?? throw new InvalidOperationException("Cover page resources are missing.");
        var shadings = resources.Elements.GetDictionary("/Shading");
        if (shadings is null)
        {
            shadings = new PdfDictionary(document);
            resources.Elements["/Shading"] = shadings;
        }
        shadings.Elements["/BekiApprovedLogo"] = shading.Reference!;

        var scale = BekiCoverDieline.LogoWidthMm / 25.4d * 72d / VisibleWidth;
        var x = BekiCoverDieline.LogoLeftMm / 25.4d * 72d - VisibleMinX * scale;
        var y = page.Height.Point - BekiCoverDieline.LogoTopMm / 25.4d * 72d + VisibleMinY * scale;
        var content = new StringBuilder("Q\nq\n");
        content.AppendLine($"{F(scale)} 0 0 {F(-scale)} {F(x)} {F(y)} cm");
        foreach (var path in svg.Descendants(ns + "path"))
        {
            content.AppendLine("q");
            AppendPath(content, (string)path.Attribute("d")!);
            var style = (string)path.Attribute("style")!;
            if (style == "fill:url(#_Linear1);")
                content.AppendLine("W* n /BekiApprovedLogo sh");
            else
                content.AppendLine(Colour(style) + " rg f*");
            content.AppendLine("Q");
        }
        content.AppendLine("Q");
        page.Contents.PrependContent().CreateStream(Encoding.ASCII.GetBytes("q\n"));
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(content.ToString()));
        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static string F(double value) => value.ToString("0.##########", CultureInfo.InvariantCulture);
    private static IEnumerable<double> Numbers(string value) => Regex.Matches(value,
        @"[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?").Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture));
    private static string Colour(string style)
    {
        var hex = Regex.Match(style, @"#[0-9a-fA-F]{6}").Value;
        if (hex.Length != 7) throw new InvalidOperationException("Unsupported logo colour.");
        return string.Join(" ", Enumerable.Range(0, 3)
            .Select(i => F(Convert.ToInt32(hex.Substring(1 + i * 2, 2), 16) / 255d)));
    }

    private static void AppendPath(StringBuilder output, string data)
    {
        var tokens = Regex.Matches(data, @"[A-Za-z]|[-+]?(?:\d*\.\d+|\d+)(?:[eE][-+]?\d+)?")
            .Select(m => m.Value).ToArray();
        double x = 0, y = 0, startX = 0, startY = 0;
        char command = '\0';
        var index = 0;
        while (index < tokens.Length)
        {
            if (char.IsLetter(tokens[index][0])) command = tokens[index++][0];
            if (command is 'Z' or 'z')
            {
                output.AppendLine("h"); x = startX; y = startY; command = '\0'; continue;
            }
            var count = char.ToUpperInvariant(command) switch
            {
                'M' or 'L' => 2, 'C' => 6,
                _ => throw new InvalidOperationException($"Unsupported logo path command {command}."),
            };
            var values = tokens.Skip(index).Take(count)
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (values.Length != count) throw new InvalidOperationException("Incomplete logo path.");
            index += count;
            if (char.IsLower(command))
                for (var i = 0; i < count; i += 2) { values[i] += x; values[i + 1] += y; }
            x = values[^2]; y = values[^1];
            var op = char.ToUpperInvariant(command) switch { 'M' => "m", 'L' => "l", _ => "c" };
            output.AppendLine(string.Join(" ", values.Select(F)) + " " + op);
            if (command is 'M' or 'm')
            {
                startX = x; startY = y; command = command == 'M' ? 'L' : 'l';
            }
        }
    }
}
