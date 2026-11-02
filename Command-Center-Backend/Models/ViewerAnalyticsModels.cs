namespace Dixon.CommandCenter.API.Models;

public sealed record ViewerAnalyticsSummary(
    decimal ProductionActual,
    decimal ProductionTarget,
    decimal Achievement,
    decimal? Fpy,
    decimal? Fty,
    decimal? Rty,
    int ActiveLines,
    int ModelsReported);

public sealed record ViewerAnalyticsTrendPoint(
    string Date,
    decimal ProductionActual,
    decimal ProductionTarget,
    decimal? Fpy,
    decimal? Fty,
    decimal? Rty);

public sealed record ViewerAnalyticsBreakdown(
    int Id,
    string Name,
    decimal ProductionActual,
    decimal ProductionTarget,
    decimal Achievement,
    decimal? Fpy,
    decimal? Fty,
    decimal? Rty);

public sealed record ViewerAnalyticsOverview(
    ViewerAnalyticsSummary Current,
    ViewerAnalyticsSummary Previous,
    string PreviousStartDate,
    string PreviousEndDate,
    IReadOnlyList<ViewerAnalyticsTrendPoint> Trend,
    IReadOnlyList<ViewerAnalyticsBreakdown> Customers,
    IReadOnlyList<ViewerAnalyticsBreakdown> Categories,
    IReadOnlyList<ViewerAnalyticsBreakdown> TopLines,
    IReadOnlyList<ViewerAnalyticsBreakdown> BottomLines);

public sealed record ViewerAnalyticsFilterOptions(
    IReadOnlyList<ViewerAnalyticsOption> Customers,
    IReadOnlyList<ViewerAnalyticsOption> Categories,
    IReadOnlyList<ViewerAnalyticsOption> Lines,
    IReadOnlyList<ViewerAnalyticsOption> Models);

public sealed record ViewerAnalyticsOption(int Id, string Name);

public sealed record ViewerAnalyticsExplorerSummary(
    long TotalRows,
    decimal ProductionActual,
    decimal ProductionTarget,
    decimal? Fpy,
    decimal? Fty,
    decimal? Rty);

public sealed record ViewerAnalyticsExplorerRow(
    string Date,
    string Customer,
    string Category,
    string ProductionLine,
    string Model,
    decimal? ProductionActual,
    decimal? ProductionTarget,
    decimal? Fpy,
    decimal? Fty,
    decimal? Rty);

public sealed record ViewerAnalyticsExplorerResponse(
    ViewerAnalyticsExplorerSummary Summary,
    IReadOnlyList<ViewerAnalyticsExplorerRow> Rows,
    int Page,
    int PageSize);

public sealed record ViewerSavedView(
    int Id,
    string Name,
    string ConfigurationJson,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record SaveViewerSavedViewRequest(string Name, string ConfigurationJson);
