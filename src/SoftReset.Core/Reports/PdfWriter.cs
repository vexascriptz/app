using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SoftReset.Core.Reports;

public enum PdfFont { Regular, Bold }

public readonly record struct PdfColor(double R, double G, double B)
{
    public static PdfColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return new PdfColor(Convert.ToInt32(hex[..2], 16) / 255.0, Convert.ToInt32(hex[2..4], 16) / 255.0, Convert.ToInt32(hex[4..6], 16) / 255.0);
    }

    internal string Fill => F(R) + " " + F(G) + " " + F(B) + " rg";
    internal string Stroke => F(R) + " " + F(G) + " " + F(B) + " RG";
    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// A small, dependency-free PDF 1.4 writer using the standard Helvetica fonts
/// (no embedding required) with WinAnsi encoding. Widths come from the Adobe
/// Helvetica AFM metrics so text wraps correctly.
/// </summary>
public sealed class PdfDocument
{
    public double PageWidth { get; }
    public double PageHeight { get; }
    public bool Compress { get; set; } = true;
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";

    private readonly List<StringBuilder> _pages = new();
    private StringBuilder? _current;

    public PdfDocument(double width = 612, double height = 792)
    {
        PageWidth = width;
        PageHeight = height;
    }

    public int PageCount => _pages.Count;

    public void NewPage()
    {
        _current = new StringBuilder();
        _pages.Add(_current);
    }

    private StringBuilder Page => _current ?? throw new InvalidOperationException("Call NewPage() first.");

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Draws text with its baseline at (x, y) measured from the top-left of the page.</summary>
    public void Text(double x, double y, string text, PdfFont font = PdfFont.Regular, double size = 10, PdfColor? color = null)
    {
        var c = color ?? new PdfColor(0, 0, 0);
        Page.Append("BT ").Append(c.Fill).Append(' ')
            .Append(font == PdfFont.Bold ? "/F2 " : "/F1 ").Append(N(size)).Append(" Tf ")
            .Append(N(x)).Append(' ').Append(N(PageHeight - y)).Append(" Td (")
            .Append(Escape(text)).Append(") Tj ET\n");
    }

    public void Rect(double x, double y, double w, double h, PdfColor? fill = null, PdfColor? stroke = null, double lineWidth = 0.75, double radius = 0)
    {
        var sb = Page;
        sb.Append("q ");
        if (fill is { } f) sb.Append(f.Fill).Append(' ');
        if (stroke is { } s) sb.Append(s.Stroke).Append(' ').Append(N(lineWidth)).Append(" w ");
        var top = PageHeight - y;
        if (radius <= 0)
        {
            sb.Append(N(x)).Append(' ').Append(N(top - h)).Append(' ').Append(N(w)).Append(' ').Append(N(h)).Append(" re ");
        }
        else
        {
            var r = Math.Min(radius, Math.Min(w, h) / 2);
            const double k = 0.5523;
            double l = x, rt = x + w, t = top, b = top - h;
            sb.Append(N(l + r)).Append(' ').Append(N(t)).Append(" m ")
              .Append(N(rt - r)).Append(' ').Append(N(t)).Append(" l ")
              .Append(N(rt - r + r * k)).Append(' ').Append(N(t)).Append(' ').Append(N(rt)).Append(' ').Append(N(t - r + r * k)).Append(' ').Append(N(rt)).Append(' ').Append(N(t - r)).Append(" c ")
              .Append(N(rt)).Append(' ').Append(N(b + r)).Append(" l ")
              .Append(N(rt)).Append(' ').Append(N(b + r - r * k)).Append(' ').Append(N(rt - r + r * k)).Append(' ').Append(N(b)).Append(' ').Append(N(rt - r)).Append(' ').Append(N(b)).Append(" c ")
              .Append(N(l + r)).Append(' ').Append(N(b)).Append(" l ")
              .Append(N(l + r - r * k)).Append(' ').Append(N(b)).Append(' ').Append(N(l)).Append(' ').Append(N(b + r - r * k)).Append(' ').Append(N(l)).Append(' ').Append(N(b + r)).Append(" c ")
              .Append(N(l)).Append(' ').Append(N(t - r)).Append(" l ")
              .Append(N(l)).Append(' ').Append(N(t - r + r * k)).Append(' ').Append(N(l + r - r * k)).Append(' ').Append(N(t)).Append(' ').Append(N(l + r)).Append(' ').Append(N(t)).Append(" c h ");
        }
        sb.Append(fill is not null && stroke is not null ? "B" : fill is not null ? "f" : "S").Append(" Q\n");
    }

    public void Line(double x1, double y1, double x2, double y2, PdfColor color, double width = 0.75)
    {
        Page.Append("q ").Append(color.Stroke).Append(' ').Append(N(width)).Append(" w ")
            .Append(N(x1)).Append(' ').Append(N(PageHeight - y1)).Append(" m ")
            .Append(N(x2)).Append(' ').Append(N(PageHeight - y2)).Append(" l S Q\n");
    }

    public static double Measure(string text, PdfFont font, double size)
    {
        var widths = font == PdfFont.Bold ? HelveticaBold : Helvetica;
        double total = 0;
        foreach (var b in ToWinAnsi(text))
            total += b is >= 32 and <= 126 ? widths[b - 32] : ExtendedWidth(b, font);
        return total * size / 1000.0;
    }

    public static List<string> Wrap(string text, PdfFont font, double size, double maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var words = paragraph.Split(' ');
            var line = new StringBuilder();
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate, font, size) <= maxWidth) { line.Clear().Append(candidate); continue; }
                if (line.Length > 0) { lines.Add(line.ToString()); line.Clear(); }
                // Break very long words (paths, hashes) across lines.
                var w = word;
                while (Measure(w, font, size) > maxWidth && w.Length > 1)
                {
                    var cut = w.Length - 1;
                    while (cut > 1 && Measure(w[..cut], font, size) > maxWidth) cut--;
                    lines.Add(w[..cut]);
                    w = w[cut..];
                }
                line.Append(w);
            }
            lines.Add(line.ToString());
        }
        return lines;
    }

    public byte[] ToBytes()
    {
        if (_pages.Count == 0) NewPage();
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b); }
        void Obj(int n, string body) { offsets.Add(ms.Position); W($"{n} 0 obj\n{body}\nendobj\n"); }

        W("%PDF-1.4\n%âãÏÓ\n");
        var pageCount = _pages.Count;
        // 1 catalog, 2 pages, 3 F1, 4 F2, 5 info, then (page, content) pairs
        var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => $"{6 + i * 2} 0 R"));
        Obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Obj(2, $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>");
        Obj(3, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Obj(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Obj(5, $"<< /Title ({Escape(Title)}) /Author ({Escape(Author)}) /Producer (softreset Technician Console) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >>");
        for (var i = 0; i < pageCount; i++)
        {
            var pageObj = 6 + i * 2;
            var contentObj = pageObj + 1;
            Obj(pageObj, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageWidth)} {N(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObj} 0 R >>");
            var content = Encoding.Latin1.GetBytes(_pages[i].ToString());
            offsets.Add(ms.Position);
            if (Compress)
            {
                using var zs = new MemoryStream();
                using (var z = new ZLibStream(zs, CompressionLevel.Optimal, leaveOpen: true)) z.Write(content);
                var data = zs.ToArray();
                W($"{contentObj} 0 obj\n<< /Length {data.Length} /Filter /FlateDecode >>\nstream\n");
                ms.Write(data);
            }
            else
            {
                W($"{contentObj} 0 obj\n<< /Length {content.Length} >>\nstream\n");
                ms.Write(content);
            }
            W("\nendstream\nendobj\n");
        }
        var xref = ms.Position;
        var sb = new StringBuilder();
        sb.Append($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
        sb.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        W(sb.ToString());
        return ms.ToArray();
    }

    // ------------------------------------------------------------ encoding

    private static readonly Dictionary<char, byte> WinAnsiExtras = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['‰'] = 0x89,
        ['‹'] = 0x8B, ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96,
        ['—'] = 0x97, ['™'] = 0x99, ['›'] = 0x9B,
    };

    internal static byte[] ToWinAnsi(string s)
    {
        var bytes = new List<byte>(s.Length);
        foreach (var ch in s)
        {
            if (ch is >= ' ' and <= '~') bytes.Add((byte)ch);
            else if (WinAnsiExtras.TryGetValue(ch, out var b)) bytes.Add(b);
            else if (ch is >= ' ' and <= 'ÿ') bytes.Add((byte)ch);
            else if (ch == '→') { bytes.Add((byte)'-'); bytes.Add((byte)'>'); }
            else if (ch == '≥') { bytes.Add((byte)'>'); bytes.Add((byte)'='); }
            else if (ch == '≤') { bytes.Add((byte)'<'); bytes.Add((byte)'='); }
            else if (ch == '✓') bytes.Add((byte)'v');
            else if (ch == '\t') bytes.Add((byte)' ');
            else if (!char.IsControl(ch)) bytes.Add((byte)'?');
        }
        return bytes.ToArray();
    }

    internal static string Escape(string text)
    {
        var sb = new StringBuilder();
        foreach (var b in ToWinAnsi(text ?? ""))
        {
            switch (b)
            {
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                default:
                    if (b < 128) sb.Append((char)b);
                    else sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    break;
            }
        }
        return sb.ToString();
    }

    private static double ExtendedWidth(byte b, PdfFont f) => b switch
    {
        0x85 or 0x97 or 0x89 => 1000,
        0x96 => 556,
        0x95 => 350,
        0x91 or 0x92 or 0x82 => f == PdfFont.Bold ? 278 : 222,
        0x93 or 0x94 or 0x84 => f == PdfFont.Bold ? 500 : 333,
        0xB0 => 400,
        0xD7 => 584,
        0xA0 => 278,
        >= 0xC0 and <= 0xDE => 722,
        _ => 556,
    };

    // Adobe Helvetica and Helvetica-Bold AFM widths for character codes 32..126.
    private static readonly int[] Helvetica =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    };

    private static readonly int[] HelveticaBold =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    };
}

/// <summary>Flowing layout on top of <see cref="PdfDocument"/>: headings, paragraphs, tables, page breaks.</summary>
public sealed class PdfFlow
{
    public static readonly PdfColor Navy = PdfColor.FromHex("0B2545");
    public static readonly PdfColor Body = PdfColor.FromHex("2B3A55");
    public static readonly PdfColor Muted = PdfColor.FromHex("5B6B84");
    public static readonly PdfColor Blue = PdfColor.FromHex("2F80ED");
    public static readonly PdfColor LightBlue = PdfColor.FromHex("EAF4FF");
    public static readonly PdfColor Border = PdfColor.FromHex("D6E4F5");
    public static readonly PdfColor White = new(1, 1, 1);

    private readonly PdfDocument _doc;
    private readonly string _headerTitle;
    public double Margin { get; } = 48;
    public double Y { get; private set; }
    public double ContentWidth => _doc.PageWidth - 2 * Margin;
    private double Bottom => _doc.PageHeight - Margin - 20;

    public PdfFlow(PdfDocument doc, string headerTitle)
    {
        _doc = doc;
        _headerTitle = headerTitle;
        NewPage();
    }

    public PdfDocument Document => _doc;

    public void NewPage()
    {
        _doc.NewPage();
        // Brand band: light-blue header strip with the softreset wordmark.
        _doc.Rect(0, 0, _doc.PageWidth, 54, fill: LightBlue);
        _doc.Rect(0, 54, _doc.PageWidth, 2, fill: PdfColor.FromHex("BFDDFF"));
        _doc.Text(Margin, 34, "softreset", PdfFont.Bold, 17, Navy);
        var w = PdfDocument.Measure(_headerTitle, PdfFont.Regular, 9);
        _doc.Text(_doc.PageWidth - Margin - w, 33, _headerTitle, PdfFont.Regular, 9, Muted);
        var page = $"Page {_doc.PageCount}";
        _doc.Text(_doc.PageWidth - Margin - PdfDocument.Measure(page, PdfFont.Regular, 8), _doc.PageHeight - 24, page, PdfFont.Regular, 8, Muted);
        _doc.Text(Margin, _doc.PageHeight - 24, "Generated by the softreset Technician Console. Measurements are point-in-time.", PdfFont.Regular, 8, Muted);
        Y = 80;
    }

    public void Ensure(double height)
    {
        if (Y + height > Bottom) NewPage();
    }

    public void Space(double h) => Y += h;

    public void Title(string text, string? subtitle = null)
    {
        Ensure(50);
        _doc.Text(Margin, Y + 18, text, PdfFont.Bold, 20, Navy);
        Y += 28;
        if (subtitle is not null)
        {
            foreach (var l in PdfDocument.Wrap(subtitle, PdfFont.Regular, 10.5, ContentWidth))
            {
                _doc.Text(Margin, Y + 10, l, PdfFont.Regular, 10.5, Muted);
                Y += 14;
            }
        }
        Y += 8;
    }

    public void Heading(string text)
    {
        Ensure(40);
        Y += 8;
        _doc.Text(Margin, Y + 13, text, PdfFont.Bold, 13.5, Navy);
        Y += 19;
        _doc.Line(Margin, Y, Margin + ContentWidth, Y, Border, 1);
        Y += 9;
    }

    public void SubHeading(string text, PdfColor? accent = null)
    {
        Ensure(30);
        _doc.Text(Margin, Y + 11, text, PdfFont.Bold, 11, accent ?? Navy);
        Y += 16;
    }

    public void Paragraph(string text, double size = 10, PdfFont font = PdfFont.Regular, PdfColor? color = null, double indent = 0)
    {
        var lineHeight = size * 1.38;
        foreach (var l in PdfDocument.Wrap(text, font, size, ContentWidth - indent))
        {
            Ensure(lineHeight);
            _doc.Text(Margin + indent, Y + size, l, font, size, color ?? Body);
            Y += lineHeight;
        }
        Y += 3;
    }

    public void Bullet(string text, double indent = 10, double size = 10)
    {
        var lines = PdfDocument.Wrap(text, PdfFont.Regular, size, ContentWidth - indent - 10);
        var lh = size * 1.36;
        for (var i = 0; i < lines.Count; i++)
        {
            Ensure(lh);
            if (i == 0) _doc.Text(Margin + indent, Y + size, "•", PdfFont.Bold, size, Blue);
            _doc.Text(Margin + indent + 10, Y + size, lines[i], PdfFont.Regular, size, Body);
            Y += lh;
        }
        Y += 1;
    }

    public void KeyValues(IEnumerable<(string Key, string Value)> rows, double keyWidth = 150)
    {
        foreach (var (k, v) in rows)
        {
            var lines = PdfDocument.Wrap(v, PdfFont.Regular, 10, ContentWidth - keyWidth);
            Ensure(14 * lines.Count);
            _doc.Text(Margin, Y + 10, k, PdfFont.Bold, 10, Navy);
            foreach (var l in lines)
            {
                _doc.Text(Margin + keyWidth, Y + 10, l, PdfFont.Regular, 10, Body);
                Y += 14;
            }
        }
        Y += 4;
    }

    public void Badge(string text, PdfColor fill, PdfColor textColor, double x, double y)
    {
        var w = PdfDocument.Measure(text, PdfFont.Bold, 8) + 12;
        _doc.Rect(x, y, w, 14, fill: fill, radius: 7);
        _doc.Text(x + 6, y + 10, text, PdfFont.Bold, 8, textColor);
    }

    /// <summary>Simple table with wrapping cells and a navy header row.</summary>
    public void Table(string[] headers, IEnumerable<string[]> rows, double[] columnFractions, double size = 9)
    {
        var widths = columnFractions.Select(f => f * ContentWidth).ToArray();
        void Header()
        {
            Ensure(22);
            _doc.Rect(Margin, Y, ContentWidth, 18, fill: Navy, radius: 3);
            var x = Margin;
            for (var i = 0; i < headers.Length; i++)
            {
                _doc.Text(x + 5, Y + 12.5, headers[i], PdfFont.Bold, size, White);
                x += widths[i];
            }
            Y += 20;
        }
        Header();
        var alt = false;
        foreach (var row in rows)
        {
            var cells = row.Select((c, i) => PdfDocument.Wrap(c ?? "", PdfFont.Regular, size, widths[i] - 10)).ToArray();
            var lh = size * 1.35;
            var h = cells.Max(c => c.Count) * lh + 6;
            if (Y + h > Bottom) { NewPage(); Header(); }
            if (alt) _doc.Rect(Margin, Y, ContentWidth, h, fill: PdfColor.FromHex("F5F9FF"));
            alt = !alt;
            var x = Margin;
            for (var i = 0; i < cells.Length; i++)
            {
                var yy = Y + 3;
                foreach (var l in cells[i])
                {
                    _doc.Text(x + 5, yy + size, l, PdfFont.Regular, size, Body);
                    yy += lh;
                }
                x += widths[i];
            }
            Y += h;
        }
        Y += 8;
    }

    /// <summary>A rounded summary card row (e.g. health score before/after).</summary>
    public void StatCards(IReadOnlyList<(string Label, string Value, string? Note)> cards)
    {
        Ensure(70);
        var gap = 10.0;
        var w = (ContentWidth - gap * (cards.Count - 1)) / cards.Count;
        for (var i = 0; i < cards.Count; i++)
        {
            var x = Margin + i * (w + gap);
            _doc.Rect(x, Y, w, 60, fill: LightBlue, stroke: Border, radius: 8);
            _doc.Text(x + 10, Y + 16, cards[i].Label, PdfFont.Regular, 8.5, Muted);
            _doc.Text(x + 10, Y + 38, cards[i].Value, PdfFont.Bold, 17, Navy);
            if (cards[i].Note is { } n) _doc.Text(x + 10, Y + 52, n, PdfFont.Regular, 7.5, Muted);
        }
        Y += 70;
    }
}
