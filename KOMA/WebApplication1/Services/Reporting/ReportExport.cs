using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KOMA.Services.Reporting
{
    // A report as plain data, so the same content can be written as PDF, Excel or CSV.
    // Reports and Revenue each build one of these; ReportExport turns it into a file.
    public class ReportDocument
    {
        public string Title { get; init; } = "";          // "Operations report"
        public string Period { get; init; } = "";         // "Week of 9–15 Nov 2026"
        public string FileName { get; init; } = "koma-report";   // without extension
        public DateTime GeneratedAt { get; init; } = DateTime.Now;
        public string GeneratedBy { get; init; } = "";
        public List<ReportFigure> Summary { get; } = new();
        public List<ReportSection> Sections { get; } = new();
        public List<string> Notes { get; } = new();       // "Basis of figures" at the end
    }

    public record ReportFigure(string Label, string Value, string? Note = null);

    public record ReportSection(string Title, string? Lead = null)
    {
        public List<ReportTable> Tables { get; } = new();
    }

    public record ReportTable(string Title, IReadOnlyList<string> Head, IReadOnlyList<string[]> Rows);

    // Model for Pages/Shared/_ExportMenu.cshtml: the page to call and the period shown.
    public record ExportMenu(string Page, IReadOnlyDictionary<string, string> Route);

    public static class ReportExport
    {
        public static readonly string[] Formats = { "pdf", "xlsx", "csv" };

        static ReportExport()
        {
            // QuestPDF is free for this project under its Community licence.
            QuestPDF.Settings.License = LicenseType.Community;
            QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
        }

        // Returns null for an unknown format.
        public static (byte[] Bytes, string ContentType, string FileName)? Build(ReportDocument doc, string? format) =>
            format?.ToLowerInvariant() switch
            {
                "pdf" => (Pdf(doc), "application/pdf", doc.FileName + ".pdf"),
                "xlsx" => (Xlsx(doc), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", doc.FileName + ".xlsx"),
                "csv" => (Csv(doc), "text/csv", doc.FileName + ".csv"),
                _ => null
            };

        private static string Stamp(ReportDocument d) =>
            $"Generated {d.GeneratedAt:d MMM yyyy, h:mm tt}" + (string.IsNullOrEmpty(d.GeneratedBy) ? "" : $" by {d.GeneratedBy}");

        // Symbols the PDF font may not have.
        private static string Clean(string s) => s.Replace("▲", "Up").Replace("▼", "Down").Replace("→", "to");

        // ---------------------------------------------------------------- CSV
        // Report-style CSV: a heading block, the summary, then each table under its own title.
        public static byte[] Csv(ReportDocument d)
        {
            var sb = new StringBuilder();
            static string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            void Line(params string[] cells) => sb.AppendLine(string.Join(",", cells.Select(Q)));

            Line($"KOMA - {d.Title}");
            Line("Period", d.Period);
            Line(Stamp(d));
            Line("Confidential - for internal management use");
            sb.AppendLine();
            Line("Summary");
            Line("Measure", "Value", "Note");
            foreach (var f in d.Summary) Line(f.Label, f.Value, f.Note ?? "");
            foreach (var s in d.Sections)
                foreach (var t in s.Tables)
                {
                    sb.AppendLine();
                    Line(t.Title == s.Title ? s.Title : $"{s.Title} - {t.Title}");
                    Line(t.Head.ToArray());
                    foreach (var r in t.Rows) Line(r);
                }
            if (d.Notes.Count > 0)
            {
                sb.AppendLine();
                Line("Basis of figures");
                foreach (var n in d.Notes) Line(n);
            }
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        // ---------------------------------------------------------------- Excel
        // Sheet 1 "Summary"; then one sheet per section. Numbers are real numbers (so they can be summed).
        public static byte[] Xlsx(ReportDocument d)
        {
            using var wb = new XLWorkbook();
            var navy = XLColor.FromHtml("#263B49");
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string SheetName(string title)
            {
                var name = Regex.Replace(title, @"[\[\]\*\?/\\:]", " ").Trim();
                if (name.Length > 28) name = name[..28];
                if (name.Length == 0) name = "Sheet";
                var n = name; var i = 2;
                while (!used.Add(n)) n = $"{name} {i++}";
                return n;
            }

            int WriteTable(IXLWorksheet ws, int row, string? title, IReadOnlyList<string> head, IEnumerable<string[]> rows)
            {
                if (!string.IsNullOrEmpty(title))
                {
                    ws.Cell(row, 1).Value = title;
                    ws.Cell(row, 1).Style.Font.Bold = true;
                    ws.Cell(row, 1).Style.Font.FontColor = navy;
                    row++;
                }
                for (var c = 0; c < head.Count; c++)
                {
                    var cell = ws.Cell(row, c + 1);
                    cell.Value = head[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Fill.BackgroundColor = navy;
                }
                row++;
                foreach (var r in rows)
                {
                    for (var c = 0; c < r.Length; c++) SetCell(ws.Cell(row, c + 1), r[c]);
                    row++;
                }
                return row + 1;
            }

            var sum = wb.Worksheets.Add(SheetName("Summary"));
            sum.Cell(1, 1).Value = $"KOMA - {d.Title}";
            sum.Cell(1, 1).Style.Font.Bold = true;
            sum.Cell(1, 1).Style.Font.FontSize = 14;
            sum.Cell(1, 1).Style.Font.FontColor = navy;
            sum.Cell(2, 1).Value = d.Period;
            sum.Cell(3, 1).Value = Stamp(d);
            sum.Cell(4, 1).Value = "Confidential - for internal management use";
            sum.Cell(4, 1).Style.Font.Italic = true;
            var next = WriteTable(sum, 6, null, new[] { "Measure", "Value", "Note" },
                d.Summary.Select(f => new[] { f.Label, f.Value, f.Note ?? "" }));
            if (d.Notes.Count > 0)
            {
                sum.Cell(next, 1).Value = "Basis of figures";
                sum.Cell(next, 1).Style.Font.Bold = true;
                foreach (var n in d.Notes) sum.Cell(++next, 1).Value = n;
            }
            sum.Columns(1, 3).AdjustToContents(8.0, 70.0);

            foreach (var s in d.Sections)
            {
                var ws = wb.Worksheets.Add(SheetName(s.Title));
                var row = 1;
                ws.Cell(row, 1).Value = s.Title;
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 1).Style.Font.FontSize = 13;
                ws.Cell(row, 1).Style.Font.FontColor = navy;
                row++;
                if (!string.IsNullOrEmpty(s.Lead)) ws.Cell(row++, 1).Value = s.Lead;
                row++;
                foreach (var t in s.Tables)
                    row = WriteTable(ws, row, t.Title == s.Title ? null : t.Title, t.Head, t.Rows);
                if (s.Tables.Count == 0) ws.Cell(row, 1).Value = "No data for this period.";
                ws.Columns().AdjustToContents(8.0, 60.0);
            }

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static readonly Regex Plain = new(@"^-?[\d,]+(\.\d+)?$", RegexOptions.Compiled);

        // "RM 1,234.50" -> 1234.5 shown as money; "45%" -> 0.45 shown as %; "12" -> 12; anything else stays text.
        private static void SetCell(IXLCell cell, string v)
        {
            var s = v.Trim();
            var inv = CultureInfo.InvariantCulture;
            if (s.StartsWith("RM ") && Plain.IsMatch(s[3..]) && double.TryParse(s[3..].Replace(",", ""), NumberStyles.Float, inv, out var rm))
            { cell.Value = rm; cell.Style.NumberFormat.Format = "\"RM\" #,##0.00"; return; }
            if (s.EndsWith('%') && Plain.IsMatch(s[..^1]) && double.TryParse(s[..^1].Replace(",", ""), NumberStyles.Float, inv, out var pct))
            { cell.Value = pct / 100; cell.Style.NumberFormat.Format = "0%"; return; }
            if (Plain.IsMatch(s) && double.TryParse(s.Replace(",", ""), NumberStyles.Float, inv, out var num))
            { cell.Value = num; cell.Style.NumberFormat.Format = s.Contains('.') ? "#,##0.00" : "#,##0"; return; }
            cell.Value = v;
        }

        // ---------------------------------------------------------------- PDF
        private const string Navy = "#263B49", Ink = "#22343D", Muted = "#59645F", Line = "#D5D5C9", Zebra = "#F4F1E9", Rust = "#974535", White = "#FFFFFF";

        public static byte[] Pdf(ReportDocument d)
        {
            return Document.Create(c => c.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.PageColor(White);
                page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

                // Every page: brand, title and period.
                page.Header().Column(h =>
                {
                    h.Item().Row(r =>
                    {
                        r.RelativeItem().Column(b =>
                        {
                            b.Item().Text("KOMA").FontSize(18).Bold().FontColor(Navy);
                            b.Item().Text("Restaurant management").FontSize(8).FontColor(Muted);
                        });
                        r.RelativeItem().AlignRight().Column(b =>
                        {
                            b.Item().AlignRight().Text(d.Title).FontSize(11).SemiBold().FontColor(Navy);
                            b.Item().AlignRight().Text(Clean(d.Period)).FontSize(8.5f).FontColor(Muted);
                        });
                    });
                    h.Item().PaddingTop(6).LineHorizontal(1.2f).LineColor(Navy);
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(4);

                    // Cover block
                    col.Item().Text(d.Title).FontSize(20).Bold().FontColor(Navy);
                    col.Item().Text(Clean(d.Period)).FontSize(11).FontColor(Ink);
                    col.Item().Text(Stamp(d)).FontSize(8.5f).FontColor(Muted);
                    col.Item().PaddingTop(6).Background(Zebra).BorderLeft(3).BorderColor(Rust).Padding(8)
                        .Text("Confidential - prepared for KOMA management. Check the figures against the system before acting on them.")
                        .FontSize(8.5f).FontColor(Ink);

                    // Key figures, four per row
                    if (d.Summary.Count > 0)
                    {
                        col.Item().PaddingTop(14).Text("Key figures").FontSize(13).Bold().FontColor(Navy);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd => { for (var i = 0; i < 4; i++) cd.RelativeColumn(); });
                            foreach (var f in d.Summary)
                            {
                                t.Cell().Padding(3).Border(0.75f).BorderColor(Line).Padding(8).Column(k =>
                                {
                                    k.Item().Text(f.Label.ToUpperInvariant()).FontSize(7).SemiBold().FontColor(Muted);
                                    k.Item().PaddingTop(2).Text(Clean(f.Value)).FontSize(14).Bold().FontColor(Navy);
                                    if (!string.IsNullOrEmpty(f.Note))
                                        k.Item().PaddingTop(2).Text(Clean(f.Note)).FontSize(7.5f).FontColor(Muted);
                                });
                            }
                        });
                    }

                    // Sections
                    for (var si = 0; si < d.Sections.Count; si++)
                    {
                        var s = d.Sections[si];
                        col.Item().EnsureSpace(120).PaddingTop(16).Text($"{si + 1}. {s.Title}").FontSize(13).Bold().FontColor(Navy);
                        col.Item().LineHorizontal(0.75f).LineColor(Line);
                        if (!string.IsNullOrEmpty(s.Lead))
                            col.Item().PaddingTop(2).Text(Clean(s.Lead)).FontColor(Ink);
                        if (s.Tables.Count == 0)
                            col.Item().PaddingTop(2).Text("No data for this period.").Italic().FontColor(Muted);
                        foreach (var tb in s.Tables)
                        {
                            if (tb.Title != s.Title)
                                col.Item().EnsureSpace(60).PaddingTop(8).Text(tb.Title).SemiBold().FontColor(Ink);
                            col.Item().PaddingTop(2).Element(e => PdfTable(e, tb));
                        }
                    }

                    if (d.Notes.Count > 0)
                    {
                        col.Item().EnsureSpace(80).PaddingTop(18).Text("Basis of figures").FontSize(10).Bold().FontColor(Navy);
                        foreach (var n in d.Notes)
                            col.Item().Row(r =>
                            {
                                r.ConstantItem(10).Text("-").FontColor(Muted);
                                r.RelativeItem().Text(Clean(n)).FontSize(8.5f).FontColor(Muted);
                            });
                    }
                });

                page.Footer().PaddingTop(6).BorderTop(0.75f).BorderColor(Line).PaddingTop(4).Row(r =>
                {
                    r.RelativeItem().Text($"KOMA · Confidential · {Stamp(d)}").FontSize(7.5f).FontColor(Muted);
                    r.AutoItem().Text(t =>
                    {
                        t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" of ");
                        t.TotalPages();
                    });
                });
            })).GeneratePdf();
        }

        private static void PdfTable(IContainer container, ReportTable tb)
        {
            if (tb.Rows.Count == 0)
            {
                container.Text("No data for this period.").Italic().FontColor(Muted);
                return;
            }
            var n = tb.Head.Count;
            // A column is right-aligned when every value in it looks like a number.
            var numeric = Enumerable.Range(0, n).Select(c => c > 0 && tb.Rows.All(r => c >= r.Length || LooksNumeric(r[c]))).ToArray();

            container.Table(t =>
            {
                t.ColumnsDefinition(cd => { for (var c = 0; c < n; c++) cd.RelativeColumn(c == 0 ? 2.2f : 1); });
                t.Header(h =>
                {
                    for (var c = 0; c < n; c++)
                    {
                        var cell = h.Cell().Background(Navy).PaddingVertical(4).PaddingHorizontal(6);
                        (numeric[c] ? cell.AlignRight() : cell).Text(tb.Head[c]).FontSize(8.5f).SemiBold().FontColor(White);
                    }
                });
                for (var r = 0; r < tb.Rows.Count; r++)
                {
                    var row = tb.Rows[r];
                    for (var c = 0; c < n; c++)
                    {
                        var cell = t.Cell().Background(r % 2 == 1 ? Zebra : White)
                            .BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3.5f).PaddingHorizontal(6);
                        (numeric[c] ? cell.AlignRight() : cell).Text(Clean(c < row.Length ? row[c] : "")).FontSize(8.5f);
                    }
                }
            });
        }

        private static bool LooksNumeric(string v)
        {
            var s = v.Trim();
            if (s is "" or "—" or "-") return true;
            if (s.StartsWith("RM ")) s = s[3..];
            s = s.TrimEnd('%');
            if (s.EndsWith(" min")) s = s[..^4].TrimStart('<');
            return Plain.IsMatch(s);
        }
    }
}
