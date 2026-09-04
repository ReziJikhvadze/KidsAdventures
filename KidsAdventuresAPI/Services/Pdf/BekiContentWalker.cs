using System.Globalization;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;

namespace AdventurePacks.Api.Services.Pdf;

/// <summary>
/// A minimal interpreter for a page's content stream: enough of one to answer the two questions the
/// audit proved nobody was asking.
///
/// **How big is that picture, really?** P0-04 found the press cover carrying a 2528×1210 raster at
/// 512×245 mm — about 125 PPI — and passing a preflight that read <c>/ColorSpace</c> and never
/// <c>/Width</c>. Correction-plan amendment A1 rules out the obvious shortcut of dividing raster
/// pixels by the page size: the credits Beki mark is a localised ~32 mm image on a 440 mm page, and
/// the page-size arithmetic would report it at forty times its real density. The only honest answer
/// is where the image was actually placed, which lives in the content stream and nowhere else: an
/// image XObject is painted into the unit square, so its placed size on paper is the current
/// transformation matrix applied to that square, and that means tracking <c>q</c>, <c>Q</c>,
/// <c>cm</c> and <c>Do</c> — recursing through Form XObjects, which carry a matrix of their own.
///
/// **What colour is that text?** P0-07 found credits text authored cream and converted to <c>0 g</c>
/// black, invisible on its own dark ground. Amendment A10a asks for the colour actually in force at
/// each text-showing operator *after* conversion, so the walker also tracks the fill colour
/// operators and reports what each <c>Tj</c>/<c>TJ</c>/<c>'</c>/<c>"</c> was painted with.
///
/// It is deliberately a lexer and a switch rather than a PDF renderer. Everything it does not
/// understand it steps over; everything it cannot resolve it reports as unresolved, because for the
/// resolution gate an image whose placement is unknown is a failure and not a pass.
/// </summary>
internal static class BekiContentWalker
{
    /// <summary>How deep Form XObjects may nest before the walk gives up and says so.</summary>
    private const int MaxFormDepth = 12;

    /// <summary>One image as it is actually painted on a page.</summary>
    /// <param name="EffectivePpiX">Raster pixels per inch of paper along the placed width.</param>
    internal sealed record PlacedImage(
        int Page,
        string Name,
        int WidthPx,
        int HeightPx,
        double PlacedWidthMm,
        double PlacedHeightMm,
        double EffectivePpiX,
        double EffectivePpiY,
        bool IsStencilMask,
        bool Inline)
    {
        /// <summary>The weaker of the two axes — the one a press sees as the limit.</summary>
        public double EffectivePpi => Math.Min(EffectivePpiX, EffectivePpiY);
    }

    /// <summary>
    /// A paint operation whose placed size could not be worked out. Reported rather than dropped:
    /// amendment A1 says unknown is not a pass.
    /// </summary>
    internal sealed record UnresolvedPlacement(int Page, string Name, string Reason);

    /// <summary>The fill colour in force at a text-showing operator, and how often it was.</summary>
    internal sealed record TextFill(
        int Page,
        string Space,
        IReadOnlyList<double> Components,
        bool IsDeviceBlack,
        int Occurrences)
    {
        public string Describe() => Components.Count == 0
            ? Space
            : $"{Space} {string.Join(" ", Components.Select(value => value.ToString("0.###", CultureInfo.InvariantCulture)))}";
    }

    /// <summary>
    /// An opaque signature of the encoded glyph sequence passed to a visible text-show operator.
    /// It deliberately records bytes, not decoded Unicode: duplicate painting is a PDF content
    /// problem and must be detectable even when a subset font has no usable ToUnicode map.
    /// </summary>
    internal sealed record TextDraw(string Signature, int Occurrences);

    /// <summary>Everything one page's content stream had to say.</summary>
    internal sealed record PageContent(
        int Page,
        IReadOnlyList<PlacedImage> Images,
        IReadOnlyList<UnresolvedPlacement> Unresolved,
        IReadOnlyList<TextFill> TextFills,
        IReadOnlyList<TextDraw> TextDraws,
        IReadOnlyList<string> ImagesNeverPlaced);

    /// <summary>
    /// Walks one page. Never throws for content reasons — a stream that cannot be read comes back
    /// as an <see cref="UnresolvedPlacement"/>, which the caller turns into a gate failure with the
    /// page named.
    /// </summary>
    public static PageContent Walk(PdfPage page, int pageNumber)
    {
        var state = new WalkState(pageNumber);

        var resources = ResourcesOf(page);
        var content = ContentBytes(page);

        if (content is null)
        {
            state.Unresolved.Add(new UnresolvedPlacement(
                pageNumber, "(page content)",
                "the page's content stream could not be decoded, so nothing on it can be measured"));
        }
        else
        {
            Execute(content, resources, state, Matrix.Identity, depth: 0);
        }

        // An XObject that is declared in the resources but never painted puts no ink on paper, so
        // it is listed rather than gated — the gate is about what the press will print.
        var placed = state.Images.Select(image => image.Name).ToHashSet(StringComparer.Ordinal);
        var declared = new List<string>();
        if (resources?.Elements.GetDictionary("/XObject") is { } xobjects)
        {
            foreach (var key in xobjects.Elements.Keys.ToList())
            {
                if (Resolve(xobjects.Elements[key]) is PdfDictionary candidate
                    && candidate.Elements.GetName("/Subtype") == "/Image"
                    && !placed.Contains(key))
                {
                    declared.Add(key);
                }
            }
        }

        var fills = state.TextFills
            .Select(pair => new TextFill(
                pageNumber, pair.Key.Space, pair.Key.Components, pair.Key.IsDeviceBlack, pair.Value))
            .ToList();

        var draws = state.TextDraws
            .Select(pair => new TextDraw(pair.Key, pair.Value))
            .ToList();

        return new PageContent(pageNumber, state.Images, state.Unresolved, fills, draws, declared);
    }

    // ---------------------------------------------------------------------------------------
    // The interpreter

    private sealed class WalkState(int page)
    {
        public int Page { get; } = page;

        public List<PlacedImage> Images { get; } = [];

        public List<UnresolvedPlacement> Unresolved { get; } = [];

        public Dictionary<FillKey, int> TextFills { get; } = [];

        public Dictionary<string, int> TextDraws { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A fill colour, compared by space and components so occurrences can be counted.</summary>
    private sealed record FillKey(string Space, IReadOnlyList<double> Components, bool IsDeviceBlack)
    {
        public bool Equals(FillKey? other) =>
            other is not null
            && Space == other.Space
            && Components.SequenceEqual(other.Components);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Space);
            foreach (var component in Components)
            {
                hash.Add(component);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>The graphics state this walk cares about: where things land and what colour they are.</summary>
    private readonly record struct GraphicsState(
        Matrix Ctm, string FillSpace, IReadOnlyList<double> FillComponents, int TextRenderMode);

    private static void Execute(
        byte[] content, PdfDictionary? resources, WalkState state, Matrix initial, int depth)
    {
        var lexer = new ContentLexer(content);
        var stack = new Stack<GraphicsState>();
        var current = new GraphicsState(initial, "/DeviceGray", [0d], 0);
        var operands = new List<object?>();

        while (lexer.TryReadToken(out var token))
        {
            if (token.Kind != TokenKind.Operator)
            {
                operands.Add(token.Value);
                if (operands.Count > 64)
                {
                    // Malformed or unknown-operator noise; keep the window bounded rather than
                    // accumulating a whole stream in memory.
                    operands.RemoveRange(0, operands.Count - 64);
                }

                continue;
            }

            var op = (string)token.Value!;

            switch (op)
            {
                case "q":
                    stack.Push(current);
                    break;

                case "Q":
                    if (stack.Count > 0)
                    {
                        current = stack.Pop();
                    }

                    break;

                case "cm":
                    if (TryMatrix(operands, out var concat))
                    {
                        current = current with { Ctm = Matrix.Concat(concat, current.Ctm) };
                    }

                    break;

                case "cs":
                    current = current with
                    {
                        FillSpace = operands.Count > 0 && operands[^1] is string name
                            ? ResolveSpaceName(name, resources)
                            : "(unknown)",
                        FillComponents = [],
                    };
                    break;

                case "g":
                    current = SetFill(current, "/DeviceGray", operands, 1);
                    break;

                case "rg":
                    current = SetFill(current, "/DeviceRGB", operands, 3);
                    break;

                case "k":
                    current = SetFill(current, "/DeviceCMYK", operands, 4);
                    break;

                case "sc":
                case "scn":
                    current = current with { FillComponents = Numbers(operands) };
                    break;

                case "Tr":
                    if (operands.Count > 0 && operands[^1] is double mode)
                    {
                        current = current with { TextRenderMode = (int)mode };
                    }

                    break;

                case "Tj":
                case "TJ":
                case "'":
                case "\"":
                    // Rendering mode 3 is invisible text; it carries no colour anybody can read,
                    // so counting it would only add noise to the colour-integrity evidence.
                    if (current.TextRenderMode != 3)
                    {
                        RecordFill(state, current);
                        RecordTextDraw(state, operands);
                    }

                    break;

                case "Do":
                    if (operands.Count > 0 && operands[^1] is string xname)
                    {
                        Paint(xname, resources, state, current, depth);
                    }

                    break;

                case "BI":
                    // An inline image carries its own dimensions in the dictionary the lexer has
                    // just collected, and it is painted into the same unit square as any XObject.
                    if (lexer.TryReadInlineImage(out var inlineWidth, out var inlineHeight, out var inlineStencil))
                    {
                        RecordImage(
                            state, "(inline image)", inlineWidth, inlineHeight, current.Ctm,
                            inlineStencil, inline: true);
                    }
                    else
                    {
                        state.Unresolved.Add(new UnresolvedPlacement(
                            state.Page, "(inline image)",
                            "an inline image was painted whose /W and /H could not be read"));
                    }

                    break;
            }

            operands.Clear();
        }
    }

    private static void Paint(
        string name, PdfDictionary? resources, WalkState state, GraphicsState current, int depth)
    {
        var xobjects = resources?.Elements.GetDictionary("/XObject");
        if (xobjects is null || Resolve(xobjects.Elements[name]) is not PdfDictionary xobject)
        {
            state.Unresolved.Add(new UnresolvedPlacement(
                state.Page, name,
                "the content stream paints this XObject but the page resources do not define it"));
            return;
        }

        var subtype = xobject.Elements.GetName("/Subtype");

        if (subtype == "/Image")
        {
            var width = xobject.Elements.GetInteger("/Width");
            var height = xobject.Elements.GetInteger("/Height");

            if (width <= 0 || height <= 0)
            {
                state.Unresolved.Add(new UnresolvedPlacement(
                    state.Page, name, "the image XObject states no usable /Width and /Height"));
                return;
            }

            RecordImage(
                state, name, width, height, current.Ctm,
                xobject.Elements.GetBoolean("/ImageMask"), inline: false);
            return;
        }

        if (subtype != "/Form")
        {
            return;
        }

        if (depth >= MaxFormDepth)
        {
            state.Unresolved.Add(new UnresolvedPlacement(
                state.Page, name,
                $"Form XObjects nest more than {MaxFormDepth} deep; the walk stopped rather than "
                + "loop, so anything below this is unmeasured"));
            return;
        }

        var form = current.Ctm;
        if (Resolve(xobject.Elements["/Matrix"]) is PdfArray matrix && matrix.Elements.Count == 6)
        {
            var values = new double[6];
            for (var index = 0; index < 6; index++)
            {
                values[index] = Real(matrix.Elements[index]);
            }

            form = Matrix.Concat(
                new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]), form);
        }

        byte[]? bytes;
        try
        {
            bytes = xobject.Stream?.UnfilteredValue;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            bytes = null;
        }

        if (bytes is null)
        {
            state.Unresolved.Add(new UnresolvedPlacement(
                state.Page, name, "the Form XObject's content stream could not be decoded"));
            return;
        }

        // A form's own /Resources shadow the page's; the fallback matters because a form is
        // allowed to omit them and inherit.
        var inner = Resolve(xobject.Elements["/Resources"]) as PdfDictionary ?? resources;
        Execute(bytes, inner, state, form, depth + 1);
    }

    private static void RecordImage(
        WalkState state, string name, int widthPx, int heightPx, Matrix ctm, bool stencil, bool inline)
    {
        // The image occupies the unit square; the placed edge lengths are that square's edges
        // through the CTM, which is |(a,b)| across and |(c,d)| down. Written this way it survives
        // a rotated placement, which the flat form (|a|, |d|) silently reports as zero.
        var widthPt = Math.Sqrt((ctm.A * ctm.A) + (ctm.B * ctm.B));
        var heightPt = Math.Sqrt((ctm.C * ctm.C) + (ctm.D * ctm.D));

        if (widthPt <= 0.0001 || heightPt <= 0.0001)
        {
            state.Unresolved.Add(new UnresolvedPlacement(
                state.Page, name,
                "the transformation in force paints this image at zero size, so no effective "
                + "resolution can be stated for it"));
            return;
        }

        state.Images.Add(new PlacedImage(
            state.Page,
            name,
            widthPx,
            heightPx,
            widthPt / 72d * 25.4d,
            heightPt / 72d * 25.4d,
            widthPx / (widthPt / 72d),
            heightPx / (heightPt / 72d),
            stencil,
            inline));
    }

    private static GraphicsState SetFill(
        GraphicsState current, string space, List<object?> operands, int expected)
    {
        var numbers = Numbers(operands);
        return current with
        {
            FillSpace = space,
            FillComponents = numbers.Count == expected ? numbers : [],
        };
    }

    private static void RecordFill(WalkState state, GraphicsState current)
    {
        var key = new FillKey(
            current.FillSpace,
            current.FillComponents,
            IsDeviceBlack(current.FillSpace, current.FillComponents));

        state.TextFills[key] = state.TextFills.TryGetValue(key, out var count) ? count + 1 : 1;
    }

    private static void RecordTextDraw(WalkState state, IReadOnlyList<object?> operands)
    {
        var encoded = new StringBuilder();
        foreach (var operand in operands)
        {
            switch (operand)
            {
                case string text:
                    encoded.Append(text).Append('\u001f');
                    break;
                case IEnumerable<object?> array:
                    foreach (var item in array.OfType<string>())
                    {
                        encoded.Append(item).Append('\u001f');
                    }
                    break;
            }
        }

        if (encoded.Length == 0)
        {
            return;
        }

        var signature = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.Latin1.GetBytes(encoded.ToString())))
            .ToLowerInvariant();
        state.TextDraws[signature] = state.TextDraws.TryGetValue(signature, out var count)
            ? count + 1
            : 1;
    }

    /// <summary>
    /// Whether this fill is the black that P0-07 describes: text that arrived light and left the
    /// conversion unreadable.
    ///
    /// The correction plan names the pure-K signature — <c>0 0 0 1 k</c>, which is exactly what the
    /// removed <c>-dBlackText=true</c> manufactured, and <c>0 g</c> or <c>0 0 0 rg</c> before any
    /// conversion. That form is checked, and so is the other one measurement turned up: authored
    /// <c>#000000</c> converted through FOGRA39 comes back as a rich black,
    /// <c>0.855 0.792 0.533 0.98 k</c>, with every plate loaded. Both print as black on a dark
    /// page, and the gate is about a page nobody can read rather than about which plates carried
    /// it, so both count. The report still records the components, so the two are told apart by
    /// anyone reading the evidence.
    /// </summary>
    public static bool IsDeviceBlack(string space, IReadOnlyList<double> components)
    {
        const double Dark = 0.05;
        const double Full = 0.85;

        return components.Count switch
        {
            1 => space is "/DeviceGray" or "/CalGray" or "/ICCBased1"
                 && components[0] <= Dark,
            3 => components.All(value => value <= Dark),
            // Pure K, or a rich black: nearly all of the black plate, or a heavy black plate under
            // a full load of the other three.
            4 => components[3] >= Full
                 || (components[3] >= 0.5d
                     && components[0] >= 0.6d && components[1] >= 0.6d && components[2] >= 0.5d),
            _ => false,
        };
    }

    // ---------------------------------------------------------------------------------------
    // Small helpers

    private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static Matrix Identity => new(1, 0, 0, 1, 0, 0);

        /// <summary>PDF's own order: the new matrix is applied before the one already in force.</summary>
        public static Matrix Concat(Matrix m, Matrix ctm) => new(
            (m.A * ctm.A) + (m.B * ctm.C),
            (m.A * ctm.B) + (m.B * ctm.D),
            (m.C * ctm.A) + (m.D * ctm.C),
            (m.C * ctm.B) + (m.D * ctm.D),
            (m.E * ctm.A) + (m.F * ctm.C) + ctm.E,
            (m.E * ctm.B) + (m.F * ctm.D) + ctm.F);
    }

    private static bool TryMatrix(List<object?> operands, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        var numbers = Numbers(operands);
        if (numbers.Count < 6)
        {
            return false;
        }

        var tail = numbers.Skip(numbers.Count - 6).ToList();
        matrix = new Matrix(tail[0], tail[1], tail[2], tail[3], tail[4], tail[5]);
        return true;
    }

    private static List<double> Numbers(List<object?> operands) =>
        operands.Where(operand => operand is double).Select(operand => (double)operand!).ToList();

    /// <summary>
    /// A named colour space resolved to something comparable. ICCBased spaces answer by component
    /// count, which is what the black test needs; anything else keeps its own name so the report
    /// says what it really was rather than guessing.
    /// </summary>
    private static string ResolveSpaceName(string name, PdfDictionary? resources)
    {
        if (name is "/DeviceGray" or "/DeviceRGB" or "/DeviceCMYK" or "/Pattern")
        {
            return name;
        }

        var spaces = resources?.Elements.GetDictionary("/ColorSpace");
        if (spaces is null || Resolve(spaces.Elements[name]) is not { } definition)
        {
            return name;
        }

        return definition switch
        {
            PdfName direct => direct.Value,
            PdfArray array when array.Elements.Count > 1
                                && array.Elements[0] is PdfName kind
                                && kind.Value == "/ICCBased"
                                && Resolve(array.Elements[1]) is PdfDictionary stream
                => $"/ICCBased{stream.Elements.GetInteger("/N")}",
            PdfArray array when array.Elements.Count > 0 && array.Elements[0] is PdfName kind
                => kind.Value,
            _ => name,
        };
    }

    /// <summary>Page resources, following /Parent — a page is allowed to inherit them.</summary>
    private static PdfDictionary? ResourcesOf(PdfPage page)
    {
        PdfItem? node = page;
        for (var hops = 0; hops < 32 && node is PdfDictionary dictionary; hops++)
        {
            if (dictionary.Elements.GetDictionary("/Resources") is { } resources)
            {
                return resources;
            }

            node = Resolve(dictionary.Elements["/Parent"]);
        }

        return null;
    }

    /// <summary>
    /// A page's content, decoded and concatenated. /Contents is one stream or an array of them,
    /// and the array is one logical stream split at arbitrary points — so they are joined with a
    /// newline rather than parsed separately, exactly as a viewer joins them.
    /// </summary>
    private static byte[]? ContentBytes(PdfPage page)
    {
        try
        {
            var contents = Resolve(page.Elements["/Contents"]);

            if (contents is PdfDictionary single)
            {
                return single.Stream?.UnfilteredValue;
            }

            if (contents is not PdfArray array)
            {
                return null;
            }

            using var joined = new MemoryStream();
            foreach (var item in array.Elements)
            {
                if (Resolve(item) is PdfDictionary part && part.Stream?.UnfilteredValue is { } bytes)
                {
                    joined.Write(bytes);
                    joined.WriteByte((byte)'\n');
                }
            }

            return joined.ToArray();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static double Real(PdfItem? item) => Resolve(item) switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => 0d,
    };

    private static PdfItem? Resolve(PdfItem? item) =>
        item is PdfReference reference ? reference.Value : item;

    // ---------------------------------------------------------------------------------------
    // The lexer

    private enum TokenKind
    {
        Number,
        Name,
        String,
        Array,
        Dictionary,
        Operator,
    }

    private readonly record struct Token(TokenKind Kind, object? Value);

    /// <summary>
    /// Enough of a PostScript-family tokeniser to read a content stream: numbers, names, strings,
    /// arrays, dictionaries and bare operators. Written over bytes rather than a decoded string
    /// because a content stream is not text — a literal string may hold any byte at all, and
    /// decoding first is how a walker starts disagreeing with the renderer about where an
    /// operator ends.
    /// </summary>
    private struct ContentLexer(byte[] content)
    {
        private readonly byte[] _content = content;
        private int _index;

        public bool TryReadToken(out Token token)
        {
            token = default;

            while (true)
            {
                SkipWhitespaceAndComments();
                if (_index >= _content.Length)
                {
                    return false;
                }

                var current = _content[_index];

                switch (current)
                {
                    case (byte)'/':
                        token = new Token(TokenKind.Name, ReadName());
                        return true;

                    case (byte)'(':
                        token = new Token(TokenKind.String, ReadLiteralString());
                        return true;

                    case (byte)'<':
                        if (_index + 1 < _content.Length && _content[_index + 1] == (byte)'<')
                        {
                            token = new Token(TokenKind.Dictionary, ReadDictionary());
                            return true;
                        }

                        token = new Token(TokenKind.String, ReadHexString());
                        return true;

                    case (byte)'[':
                        token = new Token(TokenKind.Array, ReadArray());
                        return true;

                    case (byte)']':
                    case (byte)'>':
                    case (byte)'}':
                    case (byte)'{':
                        // Stray closers and PostScript function braces: stepped over, never fatal.
                        _index++;
                        continue;

                    default:
                        if (IsNumberStart(current))
                        {
                            token = new Token(TokenKind.Number, ReadNumber());
                            return true;
                        }

                        var keyword = ReadKeyword();
                        if (keyword.Length == 0)
                        {
                            _index++;
                            continue;
                        }

                        token = new Token(TokenKind.Operator, keyword);
                        return true;
                }
            }
        }

        /// <summary>
        /// Reads the dictionary and binary payload of an inline image, leaving the cursor just past
        /// its <c>EI</c>. The abbreviations are the spec's: /W and /H, /IM for a stencil.
        /// </summary>
        public bool TryReadInlineImage(out int width, out int height, out bool stencil)
        {
            width = 0;
            height = 0;
            stencil = false;

            string? key = null;
            while (TryReadToken(out var token))
            {
                if (token.Kind == TokenKind.Operator && (string)token.Value! == "ID")
                {
                    SkipInlineImageData();
                    return width > 0 && height > 0;
                }

                if (token.Kind == TokenKind.Name)
                {
                    if (key is null)
                    {
                        key = (string)token.Value!;
                        continue;
                    }

                    key = null;
                    continue;
                }

                switch (key)
                {
                    case "/W" or "/Width" when token.Value is double w:
                        width = (int)w;
                        break;
                    case "/H" or "/Height" when token.Value is double h:
                        height = (int)h;
                        break;
                    case "/IM" or "/ImageMask" when token.Kind == TokenKind.Operator:
                        stencil = (string)token.Value! == "true";
                        break;
                }

                key = null;
            }

            return false;
        }

        private void SkipInlineImageData()
        {
            // One whitespace byte follows ID, then raw samples until a whitespace-delimited EI.
            if (_index < _content.Length && IsWhitespace(_content[_index]))
            {
                _index++;
            }

            while (_index + 1 < _content.Length)
            {
                if (_content[_index] == (byte)'E' && _content[_index + 1] == (byte)'I'
                    && (_index == 0 || IsWhitespace(_content[_index - 1]))
                    && (_index + 2 >= _content.Length || IsDelimiterOrSpace(_content[_index + 2])))
                {
                    _index += 2;
                    return;
                }

                _index++;
            }

            _index = _content.Length;
        }

        private List<object?> ReadArray()
        {
            _index++;
            var items = new List<object?>();

            while (_index < _content.Length)
            {
                SkipWhitespaceAndComments();
                if (_index < _content.Length && _content[_index] == (byte)']')
                {
                    _index++;
                    break;
                }

                if (!TryReadToken(out var token))
                {
                    break;
                }

                items.Add(token.Value);
            }

            return items;
        }

        private Dictionary<string, object?> ReadDictionary()
        {
            _index += 2;
            var entries = new Dictionary<string, object?>(StringComparer.Ordinal);
            string? key = null;

            while (_index < _content.Length)
            {
                SkipWhitespaceAndComments();
                if (_index + 1 < _content.Length
                    && _content[_index] == (byte)'>' && _content[_index + 1] == (byte)'>')
                {
                    _index += 2;
                    break;
                }

                if (!TryReadToken(out var token))
                {
                    break;
                }

                if (key is null && token.Kind == TokenKind.Name)
                {
                    key = (string)token.Value!;
                    continue;
                }

                if (key is not null)
                {
                    entries[key] = token.Value;
                    key = null;
                }
            }

            return entries;
        }

        private string ReadName()
        {
            var start = _index++;
            while (_index < _content.Length && !IsDelimiterOrSpace(_content[_index]))
            {
                _index++;
            }

            return Encoding.Latin1.GetString(_content, start, _index - start);
        }

        private string ReadKeyword()
        {
            var start = _index;
            while (_index < _content.Length && !IsDelimiterOrSpace(_content[_index]))
            {
                _index++;
            }

            return Encoding.Latin1.GetString(_content, start, _index - start);
        }

        private double ReadNumber()
        {
            var start = _index;
            while (_index < _content.Length && !IsDelimiterOrSpace(_content[_index]))
            {
                _index++;
            }

            var text = Encoding.Latin1.GetString(_content, start, _index - start);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0d;
        }

        private string ReadLiteralString()
        {
            _index++;
            var depth = 1;
            var start = _index;

            while (_index < _content.Length)
            {
                var current = _content[_index];
                if (current == (byte)'\\')
                {
                    _index += 2;
                    continue;
                }

                if (current == (byte)'(')
                {
                    depth++;
                }
                else if (current == (byte)')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        var text = Encoding.Latin1.GetString(_content, start, _index - start);
                        _index++;
                        return text;
                    }
                }

                _index++;
            }

            return string.Empty;
        }

        private string ReadHexString()
        {
            _index++;
            var start = _index;
            while (_index < _content.Length && _content[_index] != (byte)'>')
            {
                _index++;
            }

            var text = Encoding.Latin1.GetString(_content, start, Math.Max(0, _index - start));
            if (_index < _content.Length)
            {
                _index++;
            }

            return text;
        }

        private void SkipWhitespaceAndComments()
        {
            while (_index < _content.Length)
            {
                var current = _content[_index];
                if (IsWhitespace(current))
                {
                    _index++;
                    continue;
                }

                if (current == (byte)'%')
                {
                    while (_index < _content.Length
                           && _content[_index] != (byte)'\n' && _content[_index] != (byte)'\r')
                    {
                        _index++;
                    }

                    continue;
                }

                return;
            }
        }

        private static bool IsNumberStart(byte value) =>
            (value >= (byte)'0' && value <= (byte)'9')
            || value == (byte)'+' || value == (byte)'-' || value == (byte)'.';

        private static bool IsWhitespace(byte value) =>
            value is 0 or 9 or 10 or 12 or 13 or 32;

        private static bool IsDelimiterOrSpace(byte value) =>
            IsWhitespace(value)
            || value is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'['
                or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';
    }
}
