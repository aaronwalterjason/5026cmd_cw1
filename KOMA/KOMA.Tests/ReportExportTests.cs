using System.Text;
using ClosedXML.Excel;
using KOMA.Services.Reporting;

namespace KOMA.Tests;

// TC-U16..U20: the same report written as CSV, Excel and PDF.
public class ReportExportTests
{
    private static ReportDocument Sample()
    {
        var d = new ReportDocument { Title = "Sales report", Period = "November 2026", FileName = "koma-sales-2026-11", GeneratedBy = "admin@koma.test" };
        d.Summary.Add(new ReportFigure("Sales", "RM 1,234.50", "▲ RM 100.00 (9%) vs October 2026"));
        d.Summary.Add(new ReportFigure("Paid orders", "42"));
        var s = new ReportSection("Sales by dish", "Salmon Don brought in the most.");
        s.Tables.Add(new ReportTable("Sales by dish", new[] { "Dish", "Sold", "Sales", "Share" },
            new List<string[]> { new[] { "Salmon Don", "20", "RM 640.00", "52%" }, new[] { "Miso \"Soup\"", "30", "RM 594.50", "48%" } }));
        d.Sections.Add(s);
        d.Notes.Add("Only paid orders count.");
        return d;
    }

    [Fact] // TC-U16
    public void Csv_has_a_bom_heading_and_escaped_quotes()
    {
        var bytes = ReportExport.Csv(Sample());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());   // Excel opens UTF-8 correctly
        var text = Encoding.UTF8.GetString(bytes.Skip(3).ToArray());
        Assert.StartsWith("\"KOMA - Sales report\"", text);
        Assert.Contains("\"Miso \"\"Soup\"\"\"", text);
    }

    [Fact] // TC-U17
    public void Excel_has_a_summary_sheet_and_one_sheet_per_section()
    {
        using var wb = new XLWorkbook(new MemoryStream(ReportExport.Xlsx(Sample())));
        Assert.Equal(new[] { "Summary", "Sales by dish" }, wb.Worksheets.Select(w => w.Name).ToArray());
    }

    [Fact] // TC-U18
    public void Excel_stores_money_and_percent_as_numbers()
    {
        using var wb = new XLWorkbook(new MemoryStream(ReportExport.Xlsx(Sample())));
        var ws = wb.Worksheet("Sales by dish");
        var money = ws.CellsUsed().First(c => c.GetFormattedString().Contains("640"));
        Assert.True(money.DataType == XLDataType.Number);
        Assert.Equal(640d, money.GetDouble());
        var pct = ws.CellsUsed().First(c => c.DataType == XLDataType.Number && Math.Abs(c.GetDouble() - 0.52) < 1e-9);
        Assert.NotNull(pct);
    }

    [Fact] // TC-U19
    public void Pdf_is_a_valid_pdf_file()
    {
        var bytes = ReportExport.Pdf(Sample());
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Theory] // TC-U20
    [InlineData("pdf", "application/pdf", "koma-sales-2026-11.pdf")]
    [InlineData("XLSX", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "koma-sales-2026-11.xlsx")]
    [InlineData("csv", "text/csv", "koma-sales-2026-11.csv")]
    public void Build_picks_the_format_and_file_name(string format, string type, string name)
    {
        var file = ReportExport.Build(Sample(), format);
        Assert.NotNull(file);
        Assert.Equal(type, file!.Value.ContentType);
        Assert.Equal(name, file.Value.FileName);
    }

    [Fact] // TC-U21
    public void Unknown_format_is_refused() => Assert.Null(ReportExport.Build(Sample(), "docx"));
}
