using System.Globalization;
using System.Text.Json.Serialization;
using System.Xml;
using ClosedXML.Excel;
using Dixon.CommandCenter.API.Data;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Services;

public sealed class DailyReportExcelImportService(
    SqlConnectionFactory connectionFactory,
    ILogger<DailyReportExcelImportService> logger)
{
    public async Task<DailyReportExcelImportPreview> PreviewAsync(
        Stream workbookStream,
        int plantId,
        int customerId,
        int categoryId,
        DateTime selectedReportDate,
        string uploadType,
        CancellationToken cancellationToken)
    {
        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string contextSql = """
            SELECT c.CustomerName, cat.CategoryName
            FROM Customer c
            INNER JOIN Category cat
                ON cat.CustomerId = c.CustomerId
               AND cat.PlantId = @PlantId
            WHERE c.CustomerId = @CustomerId
              AND cat.CategoryId = @CategoryId
              AND c.IsActive = 1
              AND cat.IsActive = 1;
            """;

        await using var contextCommand =
            new SqlCommand(contextSql, connection);
        contextCommand.Parameters.AddWithValue("@CustomerId", customerId);
        contextCommand.Parameters.AddWithValue("@CategoryId", categoryId);
        contextCommand.Parameters.AddWithValue("@PlantId", plantId);

        string? customerName = null;
        string? categoryName = null;

        await using (var reader =
                     await contextCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                customerName = reader.GetString(0);
                categoryName = reader.GetString(1);
            }
        }

        if (customerName is null || categoryName is null)
        {
            return Failure(
                selectedReportDate,
                "The selected brand or category is not active or does not exist.");
        }

        uploadType = uploadType.Trim();

        var expectedMetric = categoryName.Trim().ToUpperInvariant() switch
        {
            "FATP" => "UPPH",
            "SMT" => "CPH",
            _ => null
        };

        if (expectedMetric is null)
        {
            return Failure(
                selectedReportDate,
                $"Excel import supports FATP and SMT categories, not '{categoryName}'.");
        }

        var catalog = await LoadCatalogAsync(
            connection,
            plantId,
            customerId,
            categoryId,
            cancellationToken);

        XLWorkbook workbook;
        await using var bufferedWorkbookStream = new MemoryStream();
        await workbookStream.CopyToAsync(bufferedWorkbookStream, cancellationToken);
        try
        {
            ExcelWorkbookCompatibility.RemoveUnsupportedInlineListValidations(bufferedWorkbookStream);
            bufferedWorkbookStream.Position = 0;
            workbook = new XLWorkbook(bufferedWorkbookStream);
        }
        catch (Exception exception) when (
            exception is InvalidDataException
                or IOException
                or ArgumentException
                or XmlException
                or FormatException
                or InvalidOperationException
                or NotSupportedException)
        {
            logger.LogWarning(
                exception,
                "Could not open Excel import workbook for PlantId {PlantId}, CustomerId {CustomerId}, CategoryId {CategoryId}.",
                plantId,
                customerId,
                categoryId);
            return Failure(
                selectedReportDate,
                "The uploaded file could not be opened as an Office Open XML workbook. Save the report in Excel as Excel Workbook (*.xlsx) and upload the saved .xlsx file; legacy .xls, renamed files, password-protected files and damaged workbooks are not supported.");
        }

        using (workbook)
        {
            var worksheets = workbook.Worksheets
                .Where(sheet => !string.Equals(
                    sheet.Name.Trim(),
                    "D-1 KPI Matrix loop Closer",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (string.Equals(uploadType, "ie", StringComparison.OrdinalIgnoreCase))
            {
                var preview = PreviewSimpleKpi(workbook.Worksheets.ToArray(), "ie", selectedReportDate, customerName, catalog);
                LogPreviewWarnings(preview, plantId, customerId, categoryId, categoryName, uploadType);
                return preview;
            }

            if (string.Equals(uploadType, "quality", StringComparison.OrdinalIgnoreCase))
            {
                var preview = PreviewSimpleKpi(workbook.Worksheets.ToArray(), "quality", selectedReportDate, customerName, catalog);
                LogPreviewWarnings(preview, plantId, customerId, categoryId, categoryName, uploadType);
                return preview;
            }

            if (string.Equals(uploadType, "woClosure", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(selectedReportDate,
                    "WO Closure uses order-level rows and must be imported through the WO Closure import endpoint. The current DailyReport matrix cannot apply it as line/model KPI rows yet.");
            }

            var dailyReportSheet = workbook.Worksheets.FirstOrDefault();
            var dailySheets = dailyReportSheet is null
                ? []
                : FindSections(dailyReportSheet).ToArray();

            if (dailySheets.Length == 0)
            {
                return Failure(
                    selectedReportDate,
                    "The workbook does not contain the expected daily operations report sheet.");
            }

            var selectedSections = dailySheets
                .Where(section => string.Equals(
                    section.Metric,
                    expectedMetric,
                    StringComparison.OrdinalIgnoreCase))
                .Where(section => !string.Equals(categoryName, "FATP", StringComparison.OrdinalIgnoreCase)
                    || section.Columns.CapacityTarget > 0
                    && Normalize(section.Worksheet.Cell(
                        section.HeaderRow + 1,
                        section.Columns.CapacityTarget).GetString()) == "INSTALLED")
                .ToArray();

            if (string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                && selectedSections.Length == 0)
            {
                selectedSections = dailySheets
                    .Where(section => string.Equals(
                        section.Metric,
                        "UPPH",
                        StringComparison.OrdinalIgnoreCase))
                    .Select(section => section with { Metric = "CPH" })
                    .ToArray();
            }

            if (selectedSections.Length == 0)
            {
                if (string.Equals(categoryName, "FATP", StringComparison.OrdinalIgnoreCase)
                    && dailySheets.Any(section => section.Metric == "UPPH"))
                {
                    return Failure(
                        selectedReportDate,
                        "The workbook has a UPPH section, but its FATP production table must use the 'Installed' subheading. The available UPPH section does not match the FATP layout.");
                }

                var otherMetric = expectedMetric == "CPH" ? "UPPH" : "CPH";
                var mismatchCategory = expectedMetric == "CPH" ? "FATP" : "SMT";
                return Failure(
                    selectedReportDate,
                    $"Selected category is {categoryName}, but the workbook contains no {expectedMetric} section for it. The available {otherMetric} section is for {mismatchCategory}.");
            }

            var sectionDates = selectedSections
                .Select(section => ReadReportDate(section.Worksheet) ?? ReadSheetDate(section.Worksheet))
                .ToArray();
            if (sectionDates.All(date => date is null))
            {
                // Complete production workbooks commonly put the date in the
                // file/sheet name instead of a Report Date cell. In that case
                // the selected report date is the explicit context supplied by
                // the contributor.
                sectionDates = Enumerable.Repeat<DateTime?>(selectedReportDate.Date, selectedSections.Length).ToArray();
            }

            if (sectionDates.Any(date => date is null)
                || sectionDates.Select(date => date!.Value.Date).Distinct().Count() != 1)
            {
                return Failure(
                    selectedReportDate,
                    $"The {categoryName} sections must each contain the same valid report date.");
            }

            var reportDate = sectionDates[0]!.Value.Date;
            var availableCustomers = GetWorkbookCustomers(selectedSections);
            if (availableCustomers.Count > 0
                && !availableCustomers.Any(brand => string.Equals(
                    NormalizeMatchName(brand),
                    NormalizeMatchName(customerName),
                    StringComparison.OrdinalIgnoreCase)))
            {
                var workbookBrands = string.Join(", ", availableCustomers.OrderBy(name => name));
                return Failure(
                    reportDate,
                    $"Selected brand '{customerName}' is not listed in the workbook. Brands found: {workbookBrands}. Select a listed brand or upload a workbook containing '{customerName}' rows.");
            }

            if (string.Equals(categoryName, "FATP", StringComparison.OrdinalIgnoreCase))
            {
                var preview = PreviewFATP(
                    selectedSections,
                    reportDate,
                    selectedReportDate,
                    customerName,
                    catalog);
                LogPreviewWarnings(preview, plantId, customerId, categoryId, categoryName, uploadType);
                return preview;
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var imported = new List<DailyReportExcelImportEntry>();

            foreach (var section in selectedSections)
            {
                var importSection = section;
                if (string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                    && section.Metric == "CPH")
                {
                    importSection = section with
                    {
                        Columns = section.Columns with
                        {
                            CapacityTarget = section.Columns.CapacityTarget > 0
                                ? section.Columns.CapacityTarget
                                : section.OtherCapacityTarget,
                            CapacityActual = section.Columns.CapacityActual > 0
                                ? section.Columns.CapacityActual
                                : section.OtherCapacityActual
                        }
                    };
                }

                for (var rowNumber = importSection.HeaderRow + 2;
                     rowNumber <= importSection.LastDataRow;
                     rowNumber++)
                {
                    var lineNo = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.LineNo);
                    if (string.IsNullOrWhiteSpace(lineNo))
                        continue;

                    if (string.Equals(lineNo, "Total", StringComparison.OrdinalIgnoreCase))
                        break;

                    var excelCustomer = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.Customer);
                    if (!IsUnspecifiedCustomer(excelCustomer))
                    {
                        availableCustomers.Add(excelCustomer!);
                        if (!string.Equals(
                                NormalizeMatchName(excelCustomer!),
                                NormalizeMatchName(customerName),
                                StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    var sapLocation = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.SapLocation);
                    var modelName = ReadText(importSection.Worksheet, rowNumber, importSection.Columns.Model);
                    var rowLabel = $"Worksheet '{importSection.Worksheet.Name}', row {rowNumber}";

                    if (string.IsNullOrWhiteSpace(sapLocation))
                        warnings.Add($"{rowLabel}: row skipped because SAP Location is blank.");
                    if (string.IsNullOrWhiteSpace(modelName))
                        warnings.Add($"{rowLabel}: row skipped because Model is blank.");
                    if (string.IsNullOrWhiteSpace(sapLocation)
                        || string.IsNullOrWhiteSpace(modelName))
                        continue;

                    var matchingOtherCategoryLine = catalog.OtherCategoryLines.Any(item =>
                        NormalizeMatchName(item.LineName) == NormalizeMatchName(lineNo)
                        && NormalizeMatchName(item.SapLocation ?? "") == NormalizeMatchName(sapLocation));
                    var matchingSelectedLine = catalog.Lines.Any(item =>
                        NormalizeMatchName(item.LineName) == NormalizeMatchName(lineNo)
                        && NormalizeMatchName(item.SapLocation ?? "") == NormalizeMatchName(sapLocation));
                    if (!matchingSelectedLine && matchingOtherCategoryLine)
                        continue;

                    var rowSection = importSection;
                    if (string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                        && section.Metric == "CPH")
                    {
                        rowSection = importSection with
                        {
                            Columns = importSection.Columns with
                            {
                                CapacityTarget = !HasNumericValue(
                                    importSection.Worksheet,
                                    rowNumber,
                                    importSection.Columns.CapacityTarget)
                                    && HasNumericValue(importSection.Worksheet, rowNumber, section.OtherCapacityTarget)
                                    ? section.OtherCapacityTarget
                                    : importSection.Columns.CapacityTarget,
                                CapacityActual = !HasNumericValue(
                                    importSection.Worksheet,
                                    rowNumber,
                                    importSection.Columns.CapacityActual)
                                    && HasNumericValue(importSection.Worksheet, rowNumber, section.OtherCapacityActual)
                                    ? section.OtherCapacityActual
                                    : importSection.Columns.CapacityActual
                            }
                        };
                    }

                    if (!string.Equals(categoryName, "SMT", StringComparison.OrdinalIgnoreCase)
                        && !HasCapacityValue(section, rowNumber)
                        && HasOtherCapacityValue(section, rowNumber))
                        continue;

                    var lineNameMatches = catalog.Lines
                        .Where(item => NormalizeMatchName(item.LineName) == NormalizeMatchName(lineNo))
                        .ToArray();
                    var lineMatches = lineNameMatches
                        .Where(item => NormalizeMatchName(item.SapLocation ?? "") == NormalizeMatchName(sapLocation))
                        .ToArray();

                    if (lineMatches.Length == 0)
                    {
                        warnings.Add(lineNameMatches.Length == 0
                            ? $"{rowLabel}: Line {lineNo} does not exist for {categoryName}."
                            : $"{rowLabel}: SAP Location '{sapLocation}' is not valid for line {lineNo}.");
                        continue;
                    }

                    if (lineMatches.Length > 1)
                    {
                        warnings.Add($"{rowLabel}: row skipped because line {lineNo} and SAP Location '{sapLocation}' match more than one active {categoryName} line.");
                        continue;
                    }

                    var line = lineMatches[0];
                    var modelMatches = catalog.Models
                        .Where(item => Normalize(item.Name) == Normalize(modelName))
                        .ToArray();

                    if (modelMatches.Length == 0)
                    {
                        warnings.Add($"{rowLabel}: row skipped because model '{modelName}' does not exist for brand '{customerName}'.");
                        continue;
                    }

                    if (modelMatches.Length > 1)
                    {
                        warnings.Add($"{rowLabel}: row skipped because model '{modelName}' is ambiguous in the selected brand.");
                        continue;
                    }

                    var model = modelMatches[0];

                    // Models are selected by customer, not by LineModel mapping.
                    // A model may be reported on any active line in the selected category.
                    var values = ReadValues(rowSection, rowNumber, rowLabel, warnings);
                    if (values is null)
                        continue;

                    imported.Add(new DailyReportExcelImportEntry(
                        line.Id,
                        line.LineName,
                        line.SapLocation,
                        model.Id,
                        model.Name,
                        values,
                        rowNumber));
                }
            }

            if (imported.Count == 0 && errors.Count == 0)
            {
                var customers = string.Join(", ", availableCustomers.OrderBy(name => name));
                errors.Add(
                    $"No {categoryName} rows matched selected brand '{customerName}'."
                    + (customers.Length == 0 ? "" : $" Brands in the workbook: {customers}."));
                errors.AddRange(warnings);
            }

            var duplicates = imported
                .GroupBy(entry => (entry.LineId, entry.ModelId))
                .Where(group => group.Count() > 1);
            foreach (var duplicate in duplicates)
            {
                var entry = duplicate.First();
                errors.Add(
                    $"The workbook contains duplicate rows for model '{entry.ModelName}' on line {entry.LineNo}.");
            }

            if (errors.Count > 0)
            {
                var invalidPreview = new DailyReportExcelImportPreview(
                    false,
                    reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    0,
                    0,
                    errors,
                    []);
                LogPreviewWarnings(invalidPreview, plantId, customerId, categoryId, categoryName, uploadType);
                return invalidPreview;
            }

            var result = new DailyReportExcelImportPreview(
                true,
                reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                imported.Select(entry => entry.LineId).Distinct().Count(),
                imported.Count,
                warnings,
                imported);
            LogPreviewWarnings(result, plantId, customerId, categoryId, categoryName, uploadType);
            return result;
        }
    }

    private void LogPreviewWarnings(
        DailyReportExcelImportPreview preview,
        int plantId,
        int customerId,
        int categoryId,
        string categoryName,
        string uploadType)
    {
        var invalidRows = preview.Rows?.Count(row => row.Status == "invalid") ?? 0;
        var actionableNotes = preview.Errors
            .Count(error => !error.StartsWith("Import summary:", StringComparison.Ordinal));
        var rowNotes = preview.Rows?
            .Where(row => row.Status != "matched" && !string.IsNullOrWhiteSpace(row.Reason))
            .ToArray() ?? [];
        if (invalidRows == 0 && actionableNotes == 0 && rowNotes.Length == 0)
            return;

        logger.LogWarning(
            "Excel import preview for PlantId {PlantId}, CustomerId {CustomerId}, CategoryId {CategoryId}, Category {CategoryName}, UploadType {UploadType} contains {InvalidRowCount} invalid row(s), {NoteCount} preview note(s), and {RowNoteCount} row diagnostic(s).",
            plantId,
            customerId,
            categoryId,
            categoryName,
            uploadType,
            invalidRows,
            actionableNotes,
            rowNotes.Length);

        foreach (var note in preview.Errors.Where(
                     error => !error.StartsWith("Import summary:", StringComparison.Ordinal)))
        {
            logger.LogWarning(
                "Excel import preview validation note for PlantId {PlantId}, CustomerId {CustomerId}, CategoryId {CategoryId}, Category {CategoryName}, UploadType {UploadType}: {ValidationNote}",
                plantId,
                customerId,
                categoryId,
                categoryName,
                uploadType,
                note);
        }

        foreach (var row in rowNotes)
        {
            logger.LogWarning(
                "Excel import preview row diagnostic for PlantId {PlantId}, CustomerId {CustomerId}, CategoryId {CategoryId}, Category {CategoryName}, UploadType {UploadType}, Worksheet {Worksheet}, Row {RowNumber}, Status {RowStatus}: {Reason}",
                plantId,
                customerId,
                categoryId,
                categoryName,
                uploadType,
                row.WorksheetName,
                row.RowNumber,
                row.Status,
                row.Reason);
        }
    }

    private static async Task<ImportCatalog> LoadCatalogAsync(
        SqlConnection connection,
        int plantId,
        int customerId,
        int categoryId,
        CancellationToken cancellationToken)
    {
        const string lineSql = """
            SELECT LineId, LineName, SAPLocation
            FROM ProductionLine
            WHERE CategoryId = @CategoryId
              AND PlantId = @PlantId
              AND IsActive = 1;
            """;

        await using var lineCommand = new SqlCommand(lineSql, connection);
        lineCommand.Parameters.AddWithValue("@CategoryId", categoryId);
        lineCommand.Parameters.AddWithValue("@PlantId", plantId);

        var lines = new List<CatalogLine>();
        await using (var lineReader =
                     await lineCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await lineReader.ReadAsync(cancellationToken))
            {
                lines.Add(new CatalogLine(
                    lineReader.GetInt32(0),
                    lineReader.GetString(1),
                    lineReader.IsDBNull(2) ? null : lineReader.GetString(2)));
            }
        }

        // Models belong to the customer. Any active line in the selected
        // category may run any active model for that customer.
        const string modelSql = """
            SELECT ModelId, ModelName
            FROM Model
            WHERE CustomerId = @CustomerId
              AND IsActive = 1;
            """;

        await using var modelCommand = new SqlCommand(modelSql, connection);
        modelCommand.Parameters.AddWithValue("@CustomerId", customerId);

        var models = new List<CatalogModel>();
        await using (var modelReader =
                     await modelCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await modelReader.ReadAsync(cancellationToken))
            {
                models.Add(new CatalogModel(
                    modelReader.GetInt32(0),
                    modelReader.GetString(1)));
            }
        }

        const string otherCategoryLineSql = """
            SELECT pl.LineName, pl.SAPLocation, cat.CategoryName
            FROM ProductionLine pl
            INNER JOIN Category cat
                ON cat.CategoryId = pl.CategoryId
               AND cat.PlantId = pl.PlantId
               AND cat.IsActive = 1
            WHERE pl.PlantId = @PlantId
              AND cat.CustomerId = @CustomerId
              AND cat.CategoryId <> @CategoryId
              AND pl.IsActive = 1;
            """;

        await using var otherLineCommand = new SqlCommand(otherCategoryLineSql, connection);
        otherLineCommand.Parameters.AddWithValue("@PlantId", plantId);
        otherLineCommand.Parameters.AddWithValue("@CustomerId", customerId);
        otherLineCommand.Parameters.AddWithValue("@CategoryId", categoryId);

        var otherCategoryLines = new List<CatalogOtherLine>();
        await using (var otherLineReader =
                     await otherLineCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await otherLineReader.ReadAsync(cancellationToken))
            {
                otherCategoryLines.Add(new CatalogOtherLine(
                    otherLineReader.GetString(0),
                    otherLineReader.IsDBNull(1) ? null : otherLineReader.GetString(1),
                    otherLineReader.GetString(2)));
            }
        }

        return new ImportCatalog(lines, models, otherCategoryLines);
    }

    private static void ValidateFATPColumns(
        DailySection section,
        List<string> errors)
    {
        var columns = section.Columns;
        var required = new (string Name, int Column)[]
        {
            ("Line No.", columns.LineNo),
            ("SAP Location", columns.SapLocation),
            ("Customer", columns.Customer),
            ("Model", columns.Model),
            ("Month Plan / Plan", columns.MonthPlan),
            ("Month Plan / Actual", columns.MonthActual),
            ("Production / Plan", columns.ProductionPlan),
            ("Production / Actual", columns.ProductionActual),
            ("UPH / Target", columns.UphTarget),
            ("UPH / Actual", columns.UphActual),
            ("UPPH / Installed", columns.CapacityTarget),
            ("UPPH / Actual", columns.CapacityActual),
            ("OS&D Reporting Date / Value (INR)", columns.OsdReportingDateValue),
            ("OS&D Reporting Date / %", columns.OsdReportingDatePercent),
            ("OS&D MTD / Value (INR)", columns.OsdMtdValue),
            ("OS&D MTD / %", columns.OsdMtdPercent),
            ("Issue description", columns.IssueDescription)
        };

        var missing = required.Where(item => item.Column == 0)
            .Select(item => item.Name)
            .ToArray();
        if (missing.Length > 0)
            errors.Add($"The FATP production table is missing expected columns: {string.Join(", ", missing)}.");
    }

    private static DailyReportExcelImportPreview PreviewFATP(
        IReadOnlyList<DailySection> sections,
        DateTime workbookDate,
        DateTime selectedReportDate,
        string customerName,
        ImportCatalog catalog)
    {
        var errors = new List<string>();
        foreach (var section in sections)
            ValidateFATPColumns(section, errors);

        if (errors.Count > 0)
            return Failure(workbookDate, string.Join(" ", errors));

        var rows = new List<DailyReportExcelImportRow>();
        foreach (var section in sections)
        {
            for (var rowNumber = section.HeaderRow + 2;
                 rowNumber <= section.LastDataRow;
                 rowNumber++)
            {
                var lineNo = ReadSourceText(section.Worksheet, rowNumber, section.Columns.LineNo);
                var sapLocation = ReadSourceText(section.Worksheet, rowNumber, section.Columns.SapLocation);
                var rowCustomer = ReadSourceText(section.Worksheet, rowNumber, section.Columns.Customer);
                var modelName = ReadSourceText(section.Worksheet, rowNumber, section.Columns.Model);
                if (string.IsNullOrWhiteSpace(lineNo)
                    && string.IsNullOrWhiteSpace(sapLocation)
                    && string.IsNullOrWhiteSpace(rowCustomer)
                    && string.IsNullOrWhiteSpace(modelName)
                    && !section.Worksheet.Row(rowNumber).CellsUsed().Any(cell => !cell.IsEmpty()))
                    continue;
                if (string.Equals(lineNo?.Trim(), "Total", StringComparison.OrdinalIgnoreCase))
                    break;

                if (!IsUnspecifiedCustomer(rowCustomer)
                    && NormalizeMatchName(rowCustomer!) != NormalizeMatchName(customerName))
                {
                    continue;
                }

                var selectedLineMatches = catalog.Lines.Any(line =>
                    NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo ?? "")
                    && NormalizeMatchName(line.SapLocation ?? "") == NormalizeMatchName(sapLocation ?? ""));
                var otherCategoryLineMatches = catalog.OtherCategoryLines.Any(line =>
                    !string.Equals(line.CategoryName, "FATP", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(sapLocation)
                    && !string.IsNullOrWhiteSpace(line.SapLocation)
                    && NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo ?? "")
                    && NormalizeMatchName(line.SapLocation ?? "") == NormalizeMatchName(sapLocation!));

                if (!selectedLineMatches && otherCategoryLineMatches)
                {
                    continue;
                }

                if (!HasCapacityValue(section, rowNumber)
                    && HasOtherCapacityValue(section, rowNumber))
                    continue;

                var reasonParts = new List<string>();
                int? lineId = null;
                string? mappedLineName = null;
                if (string.IsNullOrWhiteSpace(lineNo))
                {
                    reasonParts.Add("Line number is blank.");
                }
                else
                {
                    var lineMatches = catalog.Lines
                        .Where(line => NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo ?? ""))
                        .ToArray();
                    if (lineMatches.Length == 0)
                        reasonParts.Add($"Line '{lineNo}' is not an active line in this plant and FATP category.");
                    else if (lineMatches.Length > 1)
                        reasonParts.Add($"Line '{lineNo}' matches more than one active FATP line.");
                    else
                    {
                        lineId = lineMatches[0].Id;
                        mappedLineName = lineMatches[0].LineName;
                        if (string.IsNullOrWhiteSpace(sapLocation)
                            || NormalizeMatchName(lineMatches[0].SapLocation ?? "") != NormalizeMatchName(sapLocation!))
                        {
                            reasonParts.Add($"SAP location '{sapLocation ?? "(blank)"}' does not match line '{lineMatches[0].LineName}'.");
                        }
                    }
                }

                int? modelId = null;
                string? mappedModelName = null;
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    reasonParts.Add("Model is blank.");
                }
                else
                {
                    var modelMatches = catalog.Models
                        .Where(model => NormalizeMatchName(model.Name) == NormalizeMatchName(modelName))
                        .ToArray();
                    if (modelMatches.Length == 0)
                        reasonParts.Add($"Model '{modelName}' is not active for the selected brand.");
                    else if (modelMatches.Length > 1)
                        reasonParts.Add($"Model '{modelName}' matches more than one active model for the selected brand.");
                    else
                    {
                        modelId = modelMatches[0].Id;
                        mappedModelName = modelMatches[0].Name;
                    }
                }

                var valueErrors = new List<string>();
                var values = ReadFATPValues(section, rowNumber, valueErrors);
                reasonParts.AddRange(valueErrors);

                var status = valueErrors.Count > 0
                    ? "invalid"
                    : reasonParts.Count > 0
                        ? "unmatched"
                        : "matched";

                rows.Add(new DailyReportExcelImportRow(
                    rowNumber,
                    lineNo ?? "",
                    sapLocation,
                    rowCustomer,
                    modelName,
                    status,
                    reasonParts.Count == 0
                        ? null
                        : $"Worksheet '{section.Worksheet.Name}', row {rowNumber}: {string.Join(" ", reasonParts)}",
                    lineId,
                    mappedLineName,
                    modelId,
                    mappedModelName,
                    values)
                {
                    WorksheetName = section.Worksheet.Name
                });
            }
        }

        var duplicateGroups = rows
            .Where(row => row.Status != "invalid")
            .Where(row => row.LineId.HasValue && row.ModelId.HasValue
                || !string.IsNullOrWhiteSpace(row.LineNo)
                && !string.IsNullOrWhiteSpace(row.ModelName))
            .GroupBy(row => row.LineId.HasValue && row.ModelId.HasValue
                ? $"ID:{row.LineId}:{row.ModelId}"
                : $"NAME:{NormalizeMatchName(row.LineNo)}:{NormalizeMatchName(row.ModelName ?? "")}")
            .Where(group => group.Count() > 1);
        foreach (var duplicateGroup in duplicateGroups)
        {
            var duplicateRows = duplicateGroup.Select(row => row.RowNumber).ToHashSet();
            for (var index = 0; index < rows.Count; index++)
            {
                if (duplicateRows.Contains(rows[index].RowNumber))
                {
                    rows[index] = rows[index] with
                    {
                        Status = "duplicate",
                        Reason = "More than one Excel row refers to the same line and model. Include at most one of these rows."
                    };
                }
            }
        }

        var mismatch = workbookDate.Date != selectedReportDate.Date;
        if (workbookDate.Date > DateTime.Today)
            return Failure(workbookDate, "The workbook report date is in the future and cannot be applied.");

        var matched = rows.Where(row => row.Status == "matched").ToArray();
        errors.Add(
            $"Import summary: {rows.Count} row(s) processed, {matched.Length} matched for import, {rows.Count - matched.Length} skipped or rejected.");
        return new DailyReportExcelImportPreview(
            true,
            workbookDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            matched.Select(row => row.LineId!.Value).Distinct().Count(),
            matched.Length,
            mismatch
                ? [$"The workbook date is {workbookDate:yyyy-MM-dd}; the selected report date is {selectedReportDate:yyyy-MM-dd}. Confirm the workbook date before applying."]
                : [],
            matched.Select(row => new DailyReportExcelImportEntry(
                row.LineId!.Value,
                row.LineNo,
                row.SapLocation,
                row.ModelId!.Value,
                row.MappedModelName!,
                row.Form!)).ToArray(),
            rows);
    }

    private static DailyReportExcelImportPreview PreviewSimpleKpi(
        IReadOnlyList<IXLWorksheet> worksheets,
        string uploadType,
        DateTime selectedReportDate,
        string customerName,
        ImportCatalog catalog)
    {
        var rows = new List<DailyReportExcelImportRow>();
        var errors = new List<string>();
        var isQuality = string.Equals(uploadType, "quality", StringComparison.OrdinalIgnoreCase);

        foreach (var worksheet in worksheets)
        {
            var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
            var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            var headerRow = 0;
            for (var candidate = 1; candidate <= Math.Min(lastRow, 8); candidate++)
            {
                if (FindColumn(worksheet, candidate, lastColumn, "SAP Location") > 0
                    && FindColumn(worksheet, candidate, lastColumn, "Customer") > 0
                    && FindColumn(worksheet, candidate, lastColumn, "Model") > 0)
                {
                    headerRow = candidate;
                    break;
                }
            }

            if (headerRow == 0)
                continue;

            var subHeaderRow = headerRow + 1;
            var sapColumn = FindColumn(worksheet, headerRow, lastColumn, "SAP Location");
            var customerColumn = FindColumn(worksheet, headerRow, lastColumn, "Customer");
            var modelColumn = FindColumn(worksheet, headerRow, lastColumn, "Model");
            var lineColumn = FindColumn(worksheet, headerRow, lastColumn, "Line No.");
            var cphColumn = FindColumn(worksheet, headerRow, lastColumn, "CPH");

            int Pair(string group, string sub) =>
                FindSubColumn(worksheet, subHeaderRow,
                    FindColumn(worksheet, headerRow, lastColumn, group), sub);

            var cphTarget = Pair("CPH", "Target");
            var cphActual = Pair("CPH", "Actual");
            var fpyTarget = FindColumn(worksheet, headerRow, lastColumn, "FPY Target (%)");
            var fpyActual = FindColumn(worksheet, headerRow, lastColumn, "FPY Actual (%)");
            var ftyTarget = FindColumn(worksheet, headerRow, lastColumn, "FTY Target (%)");
            var ftyActual = FindColumn(worksheet, headerRow, lastColumn, "FTY Actual (%)");
            var rtyTarget = FindColumn(worksheet, headerRow, lastColumn, "RTY Target (%)");
            var rtyActual = FindColumn(worksheet, headerRow, lastColumn, "RTY Actual (%)");

            if (!isQuality && cphColumn > 0 && cphTarget == 0)
                cphTarget = cphColumn;
            if (!isQuality && cphColumn > 0 && cphActual == 0)
                cphActual = cphColumn + 1;

            var requiredValueColumns = isQuality
                ? new[]
                {
                    ("FPY Target (%)", fpyTarget),
                    ("FPY Actual (%)", fpyActual),
                    ("FTY Target (%)", ftyTarget),
                    ("FTY Actual (%)", ftyActual),
                    ("RTY Target (%)", rtyTarget),
                    ("RTY Actual (%)", rtyActual)
                }
                : new[]
                {
                    ("CPH Target", cphTarget),
                    ("CPH Actual", cphActual)
                };
            var missingColumns = requiredValueColumns
                .Where(column => column.Item2 <= 0)
                .Select(column => column.Item1)
                .ToArray();
            if (missingColumns.Length > 0)
            {
                errors.Add(
                    $"Worksheet '{worksheet.Name}' is missing required columns: {string.Join(", ", missingColumns)}.");
                continue;
            }

            for (var rowNumber = subHeaderRow + (isQuality ? 0 : 1); rowNumber <= lastRow; rowNumber++)
            {
                var sap = ReadSourceText(worksheet, rowNumber, sapColumn);
                var excelCustomer = ReadSourceText(worksheet, rowNumber, customerColumn);
                var modelName = ReadSourceText(worksheet, rowNumber, modelColumn);
                var lineNo = ReadSourceText(worksheet, rowNumber, lineColumn) ?? sap ?? "";
                if (string.IsNullOrWhiteSpace(sap)
                    && string.IsNullOrWhiteSpace(excelCustomer)
                    && string.IsNullOrWhiteSpace(modelName))
                    continue;
                if (string.Equals(lineNo.Trim(), "Total", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(excelCustomer?.Trim(), customerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var reason = new List<string>();
                var lineMatches = catalog.Lines.Where(line =>
                    (!string.IsNullOrWhiteSpace(lineNo)
                        && NormalizeMatchName(line.LineName) == NormalizeMatchName(lineNo))
                    || (!string.IsNullOrWhiteSpace(sap)
                        && NormalizeMatchName(line.SapLocation ?? "") == NormalizeMatchName(sap))).ToArray();
                if (lineMatches.Length == 0)
                    reason.Add($"SAP location '{sap ?? "(blank)"}' does not match an active line in the selected category.");
                else if (lineMatches.Length > 1)
                    reason.Add($"SAP location '{sap}' matches more than one active line.");

                    var modelMatches = catalog.Models
                        .Where(model => NormalizeMatchName(model.Name) == NormalizeMatchName(modelName ?? ""))
                    .ToArray();
                if (modelMatches.Length == 0)
                    reason.Add($"Model '{modelName ?? "(blank)"}' is not active for the selected brand.");
                else if (modelMatches.Length > 1)
                    reason.Add($"Model '{modelName}' matches more than one active model.");

                var valueErrors = new List<string>();
                var nonNumericValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var values = new DailyReportExcelImportValues(
                    null, null, null, null, null, null, null, null,
                    isQuality ? null : ReadSimpleNumber(worksheet, rowNumber, cphTarget, "CPH Target", "cphTarget", valueErrors, nonNumericValues),
                    isQuality ? null : ReadSimpleNumber(worksheet, rowNumber, cphActual, "CPH Actual", "cphActual", valueErrors, nonNumericValues),
                    isQuality ? ReadSimpleNumber(worksheet, rowNumber, fpyTarget, "FPY Target", "fpyTarget", valueErrors, nonNumericValues) : null,
                    isQuality ? ReadSimpleNumber(worksheet, rowNumber, fpyActual, "FPY Actual", "fpyActual", valueErrors, nonNumericValues) : null,
                    isQuality ? ReadSimpleNumber(worksheet, rowNumber, ftyTarget, "FTY Target", "ftyTarget", valueErrors, nonNumericValues) : null,
                    isQuality ? ReadSimpleNumber(worksheet, rowNumber, ftyActual, "FTY Actual", "ftyActual", valueErrors, nonNumericValues) : null,
                    isQuality ? ReadSimpleNumber(worksheet, rowNumber, rtyTarget, "RTY Target", "rtyTarget", valueErrors, nonNumericValues) : null,
                    isQuality ? ReadSimpleNumber(worksheet, rowNumber, rtyActual, "RTY Actual", "rtyActual", valueErrors, nonNumericValues) : null,
                    null, null, null, null,
                    null, null, null,
                    null, null, null, null,
                    null, null,
                    null, null, null,
                    isQuality
                        ? ["fpyTarget", "fpyActual", "ftyTarget", "ftyActual", "rtyTarget", "rtyActual"]
                        : ["cphTarget", "cphActual"],
                    nonNumericValues);

                var matched = reason.Count == 0 && valueErrors.Count == 0;
                rows.Add(new DailyReportExcelImportRow(
                    rowNumber, lineNo, sap, excelCustomer, modelName,
                    valueErrors.Count > 0 ? "invalid" : matched ? "matched" : "unmatched",
                    reason.Count == 0 && valueErrors.Count == 0
                        ? null
                    : $"Worksheet '{worksheet.Name}', row {rowNumber}: {string.Join(" ", reason.Concat(valueErrors))}",
                    matched ? lineMatches[0].Id : null,
                    matched ? lineMatches[0].LineName : null,
                    matched ? modelMatches[0].Id : null,
                    matched ? modelMatches[0].Name : null,
                values)
                {
                    WorksheetName = worksheet.Name
                });
            }
        }

        var matchedRows = rows.Where(row => row.Status == "matched").ToArray();
        if (matchedRows.Length == 0)
            errors.Add($"No {uploadType} rows matched selected brand '{customerName}'.");
        var invalidCount = rows.Count(row => row.Status == "invalid");
        if (invalidCount > 0)
            errors.Add($"{invalidCount} row(s) contain invalid numeric values and were excluded.");
        errors.Add(
            $"Import summary: {rows.Count} row(s) processed, {matchedRows.Length} matched for import, {rows.Count - matchedRows.Length} skipped or rejected.");

        return new DailyReportExcelImportPreview(
            true,
            selectedReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            matchedRows.Select(row => row.LineId!.Value).Distinct().Count(),
            matchedRows.Length,
            errors,
            matchedRows.Select(row => new DailyReportExcelImportEntry(
                row.LineId!.Value, row.LineNo, row.SapLocation,
                row.ModelId!.Value, row.MappedModelName!, row.Form!)).ToArray(),
            rows);
    }

    internal static bool TryReadImportNumber(
        IXLCell cell,
        string field,
        out decimal? value,
        out string? nonNumericValue)
    {
        var isPercentageField = field is "fpyTarget" or "fpyActual"
            or "ftyTarget" or "ftyActual"
            or "rtyTarget" or "rtyActual"
            or "osdReportingDatePercent" or "osdMtdPercent"
            or "trcOverallFailureInflowPercent";
        var parsed = isPercentageField
            ? ExcelImportCellReader.TryReadPercentageOrStatus(
                cell,
                out value,
                out nonNumericValue)
            : ExcelImportCellReader.TryReadNumberOrStatus(
                cell,
                out value,
                out nonNumericValue);

        if (parsed
            && value.HasValue
            && (field is "upphInstalled" or "upphActual"))
        {
            value = decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);
        }

        return parsed;
    }

    private static decimal? ReadSimpleNumber(
        IXLWorksheet worksheet,
        int row,
        int column,
        string field,
        string fieldKey,
        List<string> errors,
        IDictionary<string, string> nonNumericValues)
    {
        if (column <= 0)
            return null;
        if (TryReadImportNumber(
                worksheet.Cell(row, column),
            fieldKey,
                out var value,
                out var nonNumericValue))
        {
            if (nonNumericValue is not null)
                nonNumericValues[fieldKey] = nonNumericValue;
            return value;
        }

        errors.Add(
            $"Worksheet '{worksheet.Name}', row {row}, column {worksheet.Column(column).ColumnLetter()} ('{field}') contains an invalid number.");
        return null;
    }

    private static DailyReportExcelImportValues? ReadFATPValues(
        DailySection section,
        int row,
        List<string> errors)
    {
        var columns = section.Columns;
        var decimals = new Dictionary<string, decimal?>();
        var nonNumericValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var decimalFields = new (string Name, string Field, int Column)[]
        {
            ("Month Plan", "monthPlan", columns.MonthPlan),
            ("Month Actual", "monthActual", columns.MonthActual),
            ("Production Plan", "productionPlan", columns.ProductionPlan),
            ("Production Actual", "productionActual", columns.ProductionActual),
            ("UPH Target", "uphTarget", columns.UphTarget),
            ("UPH Actual", "uphActual", columns.UphActual),
            ("UPPH Installed", "upphInstalled", columns.CapacityTarget),
            ("UPPH Actual", "upphActual", columns.CapacityActual),
            ("FPY Target", "fpyTarget", columns.FpyTarget),
            ("FPY Actual", "fpyActual", columns.FpyActual),
            ("FTY Target", "ftyTarget", columns.FtyTarget),
            ("FTY Actual", "ftyActual", columns.FtyActual),
            ("RTY Target", "rtyTarget", columns.RtyTarget),
            ("RTY Actual", "rtyActual", columns.RtyActual),
            ("OS&D reporting-date value", "osdReportingDateValue", columns.OsdReportingDateValue),
            ("OS&D reporting-date percent", "osdReportingDatePercent", columns.OsdReportingDatePercent),
            ("OS&D MTD value", "osdMtdValue", columns.OsdMtdValue),
            ("OS&D MTD percent", "osdMtdPercent", columns.OsdMtdPercent),
            ("Planned OT", "plannedOTManhours", columns.PlannedOt),
            ("Unplanned OT", "unplannedOTManhours", columns.UnplannedOt),
            ("TRC Overall Failure Inflow %", "trcOverallFailureInflowPercent", columns.TrcOverallFailureInflowPercent),
            ("TRC Lying >3 Days (Cr)", "trcLyingOver3DaysCr", columns.TrcLyingOver3DaysCr)
        };

        foreach (var (name, field, column) in decimalFields)
        {
            if (column <= 0)
            {
                decimals[name] = null;
                continue;
            }

                if (!TryReadImportNumber(
                    section.Worksheet.Cell(row, column),
                    field,
                    out var value,
                    out var nonNumericValue))
            {
                errors.Add(
                    $"Worksheet '{section.Worksheet.Name}', row {row}, column {section.Worksheet.Column(column).ColumnLetter()} ('{name}') is not a valid number (Excel errors are not imported).");
                decimals[name] = null;
            }
            else
            {
                decimals[name] = value;
                if (nonNumericValue is not null)
                    nonNumericValues[field] = nonNumericValue;
            }
        }

        var integers = new Dictionary<string, int?>();
        var integerFields = new (string Name, string Field, int Column)[]
        {
            ("Open WO quantity", "openWOQty", columns.OpenWoQty),
            (">7 Days WO quantity", "over7DaysWOBalanceQty", columns.Over7DaysWoQty),
            ("WIP Lying >3 Days quantity", "wipLyingGreater3Days", columns.WipLyingGreater3Days),
            ("WIP Lying >7 Days quantity", "wipLyingGreater7Days", columns.WipLyingGreater7Days),
            ("Daily TRC In quantity", "dailyTRCInQty", columns.DailyTrcInQty),
            ("Daily TRC Out quantity", "dailyTRCOutQty", columns.DailyTrcOutQty)
        };
        foreach (var (name, field, column) in integerFields)
        {
            if (column <= 0)
            {
                integers[name] = null;
                continue;
            }

            if (!ExcelImportCellReader.TryReadNumberOrStatus(
                    section.Worksheet.Cell(row, column),
                    out var value,
                    out var nonNumericValue)
                || nonNumericValue is null
                && value.HasValue
                && (decimal.Truncate(value.Value) != value.Value
                    || value.Value < int.MinValue
                    || value.Value > int.MaxValue))
            {
                errors.Add(
                    $"Worksheet '{section.Worksheet.Name}', row {row}, column {section.Worksheet.Column(column).ColumnLetter()} ('{name}') is not a valid whole number (Excel errors are not imported).");
                integers[name] = null;
            }
            else
            {
                integers[name] = value.HasValue ? decimal.ToInt32(value.Value) : null;
                if (nonNumericValue is not null)
                    nonNumericValues[field] = nonNumericValue;
            }
        }

        decimal? Get(string name) => decimals[name];
        int? GetInt(string name) => integers.GetValueOrDefault(name);
        var plannedOt = Get("Planned OT");
        var unplannedOt = Get("Unplanned OT");
        decimal? actualOt = plannedOt.HasValue || unplannedOt.HasValue
            ? (plannedOt ?? 0) + (unplannedOt ?? 0)
            : null;
        return new DailyReportExcelImportValues(
            Get("Month Plan"),
            Get("Month Actual"),
            Get("Production Plan"),
            Get("Production Actual"),
            Get("UPH Target"),
            Get("UPH Actual"),
            Get("UPPH Installed"),
            Get("UPPH Actual"),
            null,
            null,
            Get("FPY Target"),
            Get("FPY Actual"),
            Get("FTY Target"),
            Get("FTY Actual"),
            Get("RTY Target"),
            Get("RTY Actual"),
            Get("OS&D reporting-date value"),
            Get("OS&D reporting-date percent"),
            Get("OS&D MTD value"),
            Get("OS&D MTD percent"),
            plannedOt,
            unplannedOt,
            actualOt,
            GetInt("Open WO quantity"),
            GetInt(">7 Days WO quantity"),
            GetInt("WIP Lying >3 Days quantity"),
            GetInt("WIP Lying >7 Days quantity"),
            GetInt("Daily TRC In quantity"),
            GetInt("Daily TRC Out quantity"),
            Get("TRC Overall Failure Inflow %"),
            Get("TRC Lying >3 Days (Cr)"),
            ReadText(section.Worksheet, row, columns.IssueDescription),
            GetPresentFields(columns, section.Metric),
            nonNumericValues);
    }

    private static IReadOnlyList<DailySection> FindSections(IXLWorksheet worksheet)
    {
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var sections = new List<DailySection>();

        for (var row = 1; row <= lastRow; row++)
        {
            var lineNoColumn = FindColumn(worksheet, row, lastColumn, "Line No.");
            var sapColumn = FindColumn(worksheet, row, lastColumn, "SAP Location");
            var customerColumn = FindColumn(worksheet, row, lastColumn, "Customer");
            var modelColumn = FindColumn(worksheet, row, lastColumn, "Model");
            var metrics = new[] { "UPPH", "CPH" }
                .Where(metric => FindColumn(worksheet, row, lastColumn, metric) > 0)
                .ToArray();

            if (lineNoColumn == 0
                || sapColumn == 0
                || customerColumn == 0
                || modelColumn == 0
                || metrics.Length == 0
                || row >= lastRow)
                continue;

            var nextHeader = Enumerable.Range(row + 1, lastRow - row)
                .FirstOrDefault(candidate =>
                    FindColumn(worksheet, candidate, lastColumn, "Line No.") > 0);
            var lastDataRow = nextHeader > 0 ? nextHeader - 1 : lastRow;

            foreach (var metric in metrics)
            {
                var columns = BuildColumns(
                    worksheet,
                    row,
                    row + 1,
                    lastColumn,
                    metric,
                    lineNoColumn,
                    sapColumn,
                    customerColumn,
                    modelColumn);
                var otherMetric = metric == "UPPH" ? "CPH" : "UPPH";
                var otherCapacityTarget = FindCapacityTarget(
                    worksheet,
                    row + 1,
                    FindColumn(worksheet, row, lastColumn, otherMetric),
                    otherMetric);

                var otherCapacityActual = FindSubColumn(
                    worksheet,
                    row + 1,
                    FindColumn(worksheet, row, lastColumn, otherMetric),
                    "Actual");

                sections.Add(new DailySection(
                    worksheet,
                    row,
                    lastDataRow,
                    metric,
                    columns,
                    otherCapacityTarget,
                    otherCapacityActual));
            }
        }

        return sections;
    }

    private static DailyReportExcelColumns BuildColumns(
        IXLWorksheet worksheet,
        int headerRow,
        int subHeaderRow,
        int lastColumn,
        string metric,
        int lineNo,
        int sapLocation,
        int customer,
        int model)
    {
        int Group(string label) => FindColumn(worksheet, headerRow, lastColumn, label);
        int Pair(string group, string sub) =>
            FindSubColumn(worksheet, subHeaderRow, Group(group), sub);
        int CapacityTarget() =>
            FindCapacityTarget(worksheet, subHeaderRow, Group(metric), metric);

        return new DailyReportExcelColumns(
            lineNo,
            sapLocation,
            customer,
            model,
            Pair("Month Plan", "Plan"),
            Pair("Month Plan", "Actual"),
            Pair("Production", "Plan"),
            Pair("Production", "Actual"),
            Pair("UPH", "Target"),
            Pair("UPH", "Actual"),
            CapacityTarget(),
            Pair(metric, "Actual"),
            Pair("FPY", "Target"),
            Pair("FPY", "Actual"),
            Pair("FTY", "Target"),
            Pair("FTY", "Actual"),
            Pair("RTY", "Target"),
            Pair("RTY", "Actual"),
            Pair("OS&D (Reporting Date)", "Value (INR)"),
            Pair("OS&D (Reporting Date)", "%"),
            Pair("OS&D MTD", "Value (INR)"),
            Pair("OS&D MTD", "%"),
            Pair("Actual OT (Manhours)", "Planned"),
            Pair("Actual OT (Manhours)", "Unplanned"),
            FindOperationalColumn(
                worksheet,
                headerRow,
                lastColumn,
                "Open WO (Qty)"),
            FindOperationalColumn(
                worksheet,
                headerRow,
                lastColumn,
                ">7 Days WO (Qty)",
                ">7 Days WO (Sum of Bal Qty)"),
            0,
            0,
            Group("Daily TRC In (Qty)"),
            Group("Daily TRC Out (Qty)"),
            Group("TRC Overall Failure Inflow %"),
            Group("TRC Lying >3 Days (Cr)"),
            Group("Issue description"));
    }

    private static DailyReportExcelImportValues? ReadValues(
        DailySection section,
        int row,
        string rowLabel,
        List<string> warnings)
    {
        var columns = section.Columns;
        var values = new Dictionary<string, decimal?>();
        var nonNumericValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var skippedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decimalColumns = new (string Name, string Field, int Column)[]
        {
            ("Month Plan", "monthPlan", columns.MonthPlan),
            ("Month Actual", "monthActual", columns.MonthActual),
            ("Production Plan", "productionPlan", columns.ProductionPlan),
            ("Production Actual", "productionActual", columns.ProductionActual),
            ("UPH Target", "uphTarget", columns.UphTarget),
            ("UPH Actual", "uphActual", columns.UphActual),
            (section.Metric == "UPPH" ? "UPPH Installed" : "CPH Target",
                section.Metric == "UPPH" ? "upphInstalled" : "cphTarget",
                columns.CapacityTarget),
            (section.Metric == "UPPH" ? "UPPH Actual" : "CPH Actual",
                section.Metric == "UPPH" ? "upphActual" : "cphActual",
                columns.CapacityActual),
            ("FPY Target", "fpyTarget", columns.FpyTarget),
            ("FPY Actual", "fpyActual", columns.FpyActual),
            ("FTY Target", "ftyTarget", columns.FtyTarget),
            ("FTY Actual", "ftyActual", columns.FtyActual),
            ("RTY Target", "rtyTarget", columns.RtyTarget),
            ("RTY Actual", "rtyActual", columns.RtyActual),
            ("OS&D Reporting Date", "osdReportingDateValue", columns.OsdReportingDateValue),
            ("OS&D Reporting Date %", "osdReportingDatePercent", columns.OsdReportingDatePercent),
            ("OS&D MTD", "osdMtdValue", columns.OsdMtdValue),
            ("OS&D MTD %", "osdMtdPercent", columns.OsdMtdPercent),
            ("Planned OT", "plannedOTManhours", columns.PlannedOt),
            ("Unplanned OT", "unplannedOTManhours", columns.UnplannedOt),
            ("TRC Overall Failure Inflow %", "trcOverallFailureInflowPercent", columns.TrcOverallFailureInflowPercent),
            ("TRC Lying >3 Days (Cr)", "trcLyingOver3DaysCr", columns.TrcLyingOver3DaysCr)
        };

        foreach (var (name, field, column) in decimalColumns)
        {
            if (column <= 0)
                continue;

                if (!TryReadImportNumber(
                    section.Worksheet.Cell(row, column),
                    field,
                    out var value,
                    out var nonNumericValue))
            {
                skippedFields.Add(field);
                warnings.Add(
                    $"{rowLabel}, column {section.Worksheet.Column(column).ColumnLetter()} ('{name}') was skipped because it does not contain a valid number.");
                continue;
            }

            values[name] = value;
            if (nonNumericValue is not null)
                nonNumericValues[field] = nonNumericValue;
        }

        var integerColumns = new (string Name, string Field, int Column)[]
        {
            ("Open WO (Qty)", "openWOQty", columns.OpenWoQty),
            (">7 Days WO (Qty)", "over7DaysWOBalanceQty", columns.Over7DaysWoQty),
            ("WIP Lying >3 Days (Qty)", "wipLyingGreater3Days", columns.WipLyingGreater3Days),
            ("WIP Lying >7 Days (Qty)", "wipLyingGreater7Days", columns.WipLyingGreater7Days),
            ("Daily TRC In (Qty)", "dailyTRCInQty", columns.DailyTrcInQty),
            ("Daily TRC Out (Qty)", "dailyTRCOutQty", columns.DailyTrcOutQty)
        };

        var integers = new Dictionary<string, int?>();
        foreach (var (name, field, column) in integerColumns)
        {
            if (column <= 0)
                continue;

            if (!ExcelImportCellReader.TryReadNumberOrStatus(
                    section.Worksheet.Cell(row, column),
                    out var value,
                    out var nonNumericValue))
            {
                skippedFields.Add(field);
                warnings.Add(
                    $"{rowLabel}, column {section.Worksheet.Column(column).ColumnLetter()} ('{name}') was skipped because it does not contain a valid whole number.");
                continue;
            }

            if (nonNumericValue is not null)
            {
                nonNumericValues[field] = nonNumericValue;
                integers[name] = null;
                continue;
            }

            if (value.HasValue
                && (decimal.Truncate(value.Value) != value.Value
                    || value.Value < int.MinValue
                    || value.Value > int.MaxValue))
            {
                skippedFields.Add(field);
                warnings.Add($"{rowLabel}: '{name}' was skipped because it does not contain a valid whole number.");
                continue;
            }

            integers[name] = value.HasValue ? decimal.ToInt32(value.Value) : null;
        }

        decimal? Get(string name) => values.GetValueOrDefault(name);
        int? GetInt(string name) => integers.GetValueOrDefault(name);
        var plannedOt = Get("Planned OT");
        var unplannedOt = Get("Unplanned OT");
        decimal? actualOt = plannedOt.HasValue || unplannedOt.HasValue
            ? (plannedOt ?? 0) + (unplannedOt ?? 0)
            : null;

        return new DailyReportExcelImportValues(
            Get("Month Plan"),
            Get("Month Actual"),
            Get("Production Plan"),
            Get("Production Actual"),
            Get("UPH Target"),
            Get("UPH Actual"),
            section.Metric == "UPPH" ? Get("UPPH Installed") : null,
            section.Metric == "UPPH" ? Get("UPPH Actual") : null,
            section.Metric == "CPH" ? Get("CPH Target") : null,
            section.Metric == "CPH" ? Get("CPH Actual") : null,
            Get("FPY Target"),
            Get("FPY Actual"),
            Get("FTY Target"),
            Get("FTY Actual"),
            Get("RTY Target"),
            Get("RTY Actual"),
            Get("OS&D Reporting Date"),
            Get("OS&D Reporting Date %"),
            Get("OS&D MTD"),
            Get("OS&D MTD %"),
            plannedOt,
            unplannedOt,
            actualOt,
            GetInt("Open WO (Qty)"),
            GetInt(">7 Days WO (Qty)"),
            GetInt("WIP Lying >3 Days (Qty)"),
            GetInt("WIP Lying >7 Days (Qty)"),
            GetInt("Daily TRC In (Qty)"),
            GetInt("Daily TRC Out (Qty)"),
            Get("TRC Overall Failure Inflow %"),
            Get("TRC Lying >3 Days (Cr)"),
            ReadText(section.Worksheet, row, columns.IssueDescription),
            GetPresentFields(columns, section.Metric)
                .Where(field => !skippedFields.Contains(field))
                .Where(field => field != "actualOTManhours"
                    || values.ContainsKey("Planned OT")
                    || values.ContainsKey("Unplanned OT"))
                .ToArray(),
            nonNumericValues);
    }

    private static DateTime? ReadReportDate(IXLWorksheet worksheet)
    {
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var row = 1; row <= Math.Min(lastRow, 10); row++)
        {
            for (var column = 1; column < lastColumn; column++)
            {
                var label = Normalize(worksheet.Cell(row, column).GetString());
                if (label != "REPORTDATE")
                    continue;

                var dateCell = worksheet.Cell(row, column + 1);
                if (dateCell.TryGetValue<DateTime>(out var date))
                    return date.Date;

                if (dateCell.TryGetValue<double>(out var serial)
                    && serial >= 1
                    && serial <= 2958465)
                {
                    return DateTime.FromOADate(serial).Date;
                }

                if (DateTime.TryParse(
                        dateCell.GetString(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out date))
                {
                    return date.Date;
                }

                return null;
            }
        }

        return null;
    }

    private static DateTime? ReadSheetDate(IXLWorksheet worksheet)
    {
        var name = worksheet.Name.Trim();
        var formats = new[] { "dd.MM.yyyy", "d.M.yyyy", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        return DateTime.TryParseExact(
                name,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var date)
            ? date.Date
            : null;
    }

    private static string? ReadText(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return null;
        var value = worksheet.Cell(row, column).GetString().Trim();
        return value.Length == 0 ? null : value;
    }

    private static string? ReadSourceText(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return null;
        var value = worksheet.Cell(row, column).GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int FindColumn(
        IXLWorksheet worksheet,
        int row,
        int lastColumn,
        string label)
    {
        var expected = Normalize(label);
        for (var column = 1; column <= lastColumn; column++)
        {
            if (Normalize(worksheet.Cell(row, column).GetString()) == expected)
                return column;
        }

        return 0;
    }

    internal static int FindOperationalColumn(
        IXLWorksheet worksheet,
        int row,
        int lastColumn,
        params string[] labels)
    {
        foreach (var label in labels)
        {
            var column = FindColumn(worksheet, row, lastColumn, label);
            if (column > 0)
                return column;
        }

        return 0;
    }

    private static int FindSubColumn(
        IXLWorksheet worksheet,
        int row,
        int groupColumn,
        string label)
    {
        if (groupColumn <= 0)
            return 0;

        var expected = Normalize(label);
        for (var column = groupColumn; column <= groupColumn + 1; column++)
        {
            if (Normalize(worksheet.Cell(row, column).GetString()) == expected)
                return column;
        }

        return 0;
    }

    private static int FindCapacityTarget(
        IXLWorksheet worksheet,
        int subHeaderRow,
        int groupColumn,
        string metric)
    {
        var labels = metric == "UPPH"
            ? new[] { "Installed", "Ins", "Target" }
            : new[] { "Target", "Installed", "Ins" };
        foreach (var label in labels)
        {
            var column = FindSubColumn(worksheet, subHeaderRow, groupColumn, label);
            if (column > 0)
                return column;
        }

        return 0;
    }

    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private static string NormalizeMatchName(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();

    private static bool IsUnspecifiedCustomer(string? customer)
    {
        if (string.IsNullOrWhiteSpace(customer))
            return true;

        return NormalizeMatchName(customer) is "-" or "–" or "—" or "NA" or "N/A" or "N.A.";
    }

    private static HashSet<string> GetWorkbookCustomers(
        IReadOnlyList<DailySection> sections)
    {
        var customers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sections)
        {
            for (var rowNumber = section.HeaderRow + 2;
                 rowNumber <= section.LastDataRow;
                 rowNumber++)
            {
                var lineNo = ReadSourceText(section.Worksheet, rowNumber, section.Columns.LineNo);
                if (string.Equals(lineNo, "Total", StringComparison.OrdinalIgnoreCase))
                    break;

                var customer = ReadSourceText(section.Worksheet, rowNumber, section.Columns.Customer);
                if (!IsUnspecifiedCustomer(customer))
                    customers.Add(customer!);
            }
        }

        return customers;
    }

    private static bool HasCapacityValue(DailySection section, int row)
    {
        var target = section.Columns.CapacityTarget;
        var actual = section.Columns.CapacityActual;
        return (target > 0
                && !ExcelImportCellReader.ResolveMergedCell(section.Worksheet.Cell(row, target)).IsEmpty())
            || (actual > 0
                && !ExcelImportCellReader.ResolveMergedCell(section.Worksheet.Cell(row, actual)).IsEmpty());
    }

    private static bool HasNumericValue(
        IXLWorksheet worksheet,
        int row,
        int column)
    {
        if (column <= 0)
            return false;

        return ExcelImportCellReader.TryReadNumberOrStatus(
                worksheet.Cell(row, column),
                out var value,
                out var nonNumericValue)
            && (value.HasValue || nonNumericValue is not null);
    }

    private static bool HasOtherCapacityValue(DailySection section, int row)
    {
        return (section.OtherCapacityTarget > 0
                && !ExcelImportCellReader.ResolveMergedCell(
                    section.Worksheet.Cell(row, section.OtherCapacityTarget)).IsEmpty())
            || (section.OtherCapacityActual > 0
                && !ExcelImportCellReader.ResolveMergedCell(
                    section.Worksheet.Cell(row, section.OtherCapacityActual)).IsEmpty());
    }

    private static IReadOnlyList<string> GetPresentFields(
        DailyReportExcelColumns columns,
        string metric)
    {
        var fields = new List<string>();
        void Add(string field, int column)
        {
            if (column > 0)
                fields.Add(field);
        }

        Add("monthPlan", columns.MonthPlan);
        Add("monthActual", columns.MonthActual);
        Add("productionPlan", columns.ProductionPlan);
        Add("productionActual", columns.ProductionActual);
        Add("uphTarget", columns.UphTarget);
        Add("uphActual", columns.UphActual);
        Add(metric == "UPPH" ? "upphInstalled" : "cphTarget", columns.CapacityTarget);
        Add(metric == "UPPH" ? "upphActual" : "cphActual", columns.CapacityActual);
        Add("fpyTarget", columns.FpyTarget);
        Add("fpyActual", columns.FpyActual);
        Add("ftyTarget", columns.FtyTarget);
        Add("ftyActual", columns.FtyActual);
        Add("rtyTarget", columns.RtyTarget);
        Add("rtyActual", columns.RtyActual);
        Add("osdReportingDateValue", columns.OsdReportingDateValue);
        Add("osdReportingDatePercent", columns.OsdReportingDatePercent);
        Add("osdMtdValue", columns.OsdMtdValue);
        Add("osdMtdPercent", columns.OsdMtdPercent);
        Add("plannedOTManhours", columns.PlannedOt);
        Add("unplannedOTManhours", columns.UnplannedOt);
        Add("actualOTManhours", columns.PlannedOt > 0 || columns.UnplannedOt > 0 ? 1 : 0);
        Add("openWOQty", columns.OpenWoQty);
        Add("over7DaysWOBalanceQty", columns.Over7DaysWoQty);
        Add("dailyTRCInQty", columns.DailyTrcInQty);
        Add("dailyTRCOutQty", columns.DailyTrcOutQty);
        Add("trcOverallFailureInflowPercent", columns.TrcOverallFailureInflowPercent);
        Add("trcLyingOver3DaysCr", columns.TrcLyingOver3DaysCr);
        Add("issueDescription", columns.IssueDescription);
        return fields;
    }

    private static DailyReportExcelImportPreview Failure(
        DateTime reportDate,
        string error) =>
        new(
            false,
            reportDate == default
                ? null
                : reportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            0,
            0,
            [error],
            []);

    private sealed record CatalogLine(int Id, string LineName, string? SapLocation);
    private sealed record CatalogModel(int Id, string Name);
    private sealed record ImportCatalog(
        IReadOnlyList<CatalogLine> Lines,
        IReadOnlyList<CatalogModel> Models,
        IReadOnlyList<CatalogOtherLine> OtherCategoryLines);
    private sealed record CatalogOtherLine(
        string LineName,
        string? SapLocation,
        string CategoryName);
    private sealed record DailySection(
        IXLWorksheet Worksheet,
        int HeaderRow,
        int LastDataRow,
        string Metric,
        DailyReportExcelColumns Columns,
        int OtherCapacityTarget,
        int OtherCapacityActual);
}

public sealed record DailyReportExcelColumns(
    int LineNo,
    int SapLocation,
    int Customer,
    int Model,
    int MonthPlan,
    int MonthActual,
    int ProductionPlan,
    int ProductionActual,
    int UphTarget,
    int UphActual,
    int CapacityTarget,
    int CapacityActual,
    int FpyTarget,
    int FpyActual,
    int FtyTarget,
    int FtyActual,
    int RtyTarget,
    int RtyActual,
    int OsdReportingDateValue,
    int OsdReportingDatePercent,
    int OsdMtdValue,
    int OsdMtdPercent,
    int PlannedOt,
    int UnplannedOt,
    int OpenWoQty,
    int Over7DaysWoQty,
    int WipLyingGreater3Days,
    int WipLyingGreater7Days,
    int DailyTrcInQty,
    int DailyTrcOutQty,
    int TrcOverallFailureInflowPercent,
    int TrcLyingOver3DaysCr,
    int IssueDescription);

public sealed record DailyReportExcelImportPreview(
    bool Valid,
    string? ReportDate,
    int Lines,
    int Models,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DailyReportExcelImportEntry> Data,
    IReadOnlyList<DailyReportExcelImportRow>? Rows = null);

public sealed record DailyReportExcelImportRow(
    int RowNumber,
    string LineNo,
    string? SapLocation,
    string? Customer,
    string? ModelName,
    string Status,
    string? Reason,
    int? LineId,
    string? MappedLineName,
    int? ModelId,
    string? MappedModelName,
    DailyReportExcelImportValues? Form)
{
    [JsonIgnore]
    public string? WorksheetName { get; init; }
}

public sealed record DailyReportExcelImportEntry(
    int LineId,
    string LineNo,
    string? SapLocation,
    int ModelId,
    string ModelName,
    DailyReportExcelImportValues Form,
    int? RowNumber = null);

public sealed record DailyReportExcelImportValues(
    decimal? MonthPlan,
    decimal? MonthActual,
    decimal? ProductionPlan,
    decimal? ProductionActual,
    decimal? UphTarget,
    decimal? UphActual,
    decimal? UpphInstalled,
    decimal? UpphActual,
    decimal? CphTarget,
    decimal? CphActual,
    decimal? FpyTarget,
    decimal? FpyActual,
    decimal? FtyTarget,
    decimal? FtyActual,
    decimal? RtyTarget,
    decimal? RtyActual,
    decimal? OsdReportingDateValue,
    decimal? OsdReportingDatePercent,
    decimal? OsdMtdValue,
    decimal? OsdMtdPercent,
    decimal? PlannedOTManhours,
    decimal? UnplannedOTManhours,
    decimal? ActualOTManhours,
    int? OpenWOQty,
    int? Over7DaysWOBalanceQty,
    int? WipLyingGreater3Days,
    int? WipLyingGreater7Days,
    int? DailyTRCInQty,
    int? DailyTRCOutQty,
    decimal? TRCOverallFailureInflowPercent,
    decimal? TRCLyingOver3DaysCr,
    string? IssueDescription,
    IReadOnlyList<string> PresentFields,
    IReadOnlyDictionary<string, string>? NonNumericValues = null);
