using System.IO.Compression;
using System.Xml.Linq;

namespace Dixon.CommandCenter.API.Services;

public static class ExcelWorkbookCompatibility
{
    public static void RemoveUnsupportedInlineListValidations(MemoryStream workbookStream)
    {
        workbookStream.Position = 0;
        using var archive = new ZipArchive(workbookStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetEntries = archive.Entries
            .Where(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var entry in worksheetEntries)
        {
            XDocument document;
            using (var stream = entry.Open())
                document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);

            var unsupportedRules = document
                .Descendants(spreadsheet + "dataValidation")
                .Where(validation => validation.Elements()
                    .Where(element => element.Name == spreadsheet + "formula1"
                        || element.Name == spreadsheet + "formula2")
                    .Any(formula => formula.Value.Length > 255))
                .ToArray();
            if (unsupportedRules.Length == 0)
                continue;

            var validations = document.Descendants(spreadsheet + "dataValidations").FirstOrDefault();
            foreach (var rule in unsupportedRules)
                rule.Remove();

            if (validations is not null)
            {
                var remainingRules = validations.Elements(spreadsheet + "dataValidation").Count();
                if (remainingRules == 0)
                    validations.Remove();
                else
                    validations.SetAttributeValue("count", remainingRules);
            }

            var entryName = entry.FullName;
            entry.Delete();
            var replacement = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var output = replacement.Open();
            document.Save(output, SaveOptions.DisableFormatting);
        }

        workbookStream.Position = 0;
    }
}
