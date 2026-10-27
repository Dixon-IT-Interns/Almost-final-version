using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Approver")]
[Route("api/approvals")]
public sealed class ApprovalController(
    SqlConnectionFactory connectionFactory,
    ILogger<ApprovalController> logger,
    IWebHostEnvironment environment) : ControllerBase
{
    // =========================================================
    // SUMMARY
    // =========================================================

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();
        var plantId = GetPlantId();
        if (plantId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                aq.Status,
                COUNT(1) AS ItemCount
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = dr.PlantId
               AND pc.CustomerId = dr.CustomerId
            WHERE dr.PlantId = @PlantId
            GROUP BY aq.Status;
            """;

        await using var command = new SqlCommand(sql, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);

        var counts = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return Ok(new
        {
            pending = counts.GetValueOrDefault("PENDING"),
            approved = counts.GetValueOrDefault("APPROVED"),
            rejected = counts.GetValueOrDefault("REJECTED"),
            reviewed =
                counts.GetValueOrDefault("APPROVED") +
                counts.GetValueOrDefault("REJECTED")
        });
    }


    // =========================================================
    // PENDING APPROVALS
    // =========================================================

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();
        var plantId = GetPlantId();
        if (plantId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT
                aq.ApprovalQueueId,
                aq.DailyReportId,
                aq.Status,
                aq.SubmittedAt,
                dr.ReportDate,
                c.CustomerName,
                cat.CategoryName,
                pl.LineName,
                m.ModelName,
                u.FullName AS SubmittedByName,
                dr.SubmissionBatchId,
                CASE WHEN dr.SubmissionBatchEnforced=1 THEN
                    (SELECT COUNT(1) FROM DailyReport other
                     WHERE other.PlantId=dr.PlantId AND other.ReportDate=dr.ReportDate AND other.CustomerId=dr.CustomerId
                       AND other.CreatedBy=dr.CreatedBy AND other.IsActive=1 AND other.ModelId IS NOT NULL
                       AND ((EXISTS(SELECT 1 FROM DailyReport batchPeer WHERE batchPeer.SubmissionBatchId=dr.SubmissionBatchId AND batchPeer.CategoryId<>dr.CategoryId)
                             AND other.CategoryId IN (SELECT scopeCategory.CategoryId FROM Category scopeCategory
                                 WHERE scopeCategory.PlantId=dr.PlantId AND scopeCategory.CustomerId=dr.CustomerId
                                   AND scopeCategory.IsActive=1 AND UPPER(scopeCategory.CategoryName) IN ('FATP','SMT')))
                         OR (NOT EXISTS(SELECT 1 FROM DailyReport batchPeer WHERE batchPeer.SubmissionBatchId=dr.SubmissionBatchId AND batchPeer.CategoryId<>dr.CategoryId)
                             AND other.CategoryId=dr.CategoryId))
                       AND other.Status NOT IN ('APPROVED','INACTIVE')
                       AND (other.Status<>'SUBMITTED' OR other.SubmissionBatchId<>dr.SubmissionBatchId))
                    + (SELECT COUNT(1) FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
                       WHERE pl.PlantId=dr.PlantId AND pl.IsActive=1 AND cat.PlantId=dr.PlantId AND cat.CustomerId=dr.CustomerId
                         AND cat.IsActive=1
                         AND ((EXISTS(SELECT 1 FROM DailyReport batchPeer WHERE batchPeer.SubmissionBatchId=dr.SubmissionBatchId AND batchPeer.CategoryId<>dr.CategoryId)
                               AND UPPER(cat.CategoryName) IN ('FATP','SMT'))
                           OR (NOT EXISTS(SELECT 1 FROM DailyReport batchPeer WHERE batchPeer.SubmissionBatchId=dr.SubmissionBatchId AND batchPeer.CategoryId<>dr.CategoryId)
                               AND cat.CategoryId=dr.CategoryId))
                         AND NOT EXISTS(SELECT 1 FROM DailyReport lineState WHERE lineState.PlantId=dr.PlantId AND lineState.ReportDate=dr.ReportDate
                           AND lineState.CustomerId=dr.CustomerId AND lineState.CategoryId=cat.CategoryId AND lineState.LineId=pl.LineId
                           AND lineState.CreatedBy=dr.CreatedBy AND ((lineState.ModelId IS NULL AND lineState.IsActive=0) OR (lineState.ModelId IS NOT NULL AND lineState.IsActive=1))))
                    ELSE 0 END AS IncompleteCount
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN Customer c
                ON c.CustomerId = dr.CustomerId
            INNER JOIN Category cat
                ON cat.CategoryId = dr.CategoryId
            INNER JOIN ProductionLine pl
                ON pl.LineId = dr.LineId
            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId
            INNER JOIN [User] u
                ON u.UserId = aq.SubmittedBy
            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = dr.PlantId
               AND pc.CustomerId = dr.CustomerId
            WHERE aq.Status = 'PENDING'
              AND dr.Status = 'SUBMITTED'
              AND dr.PlantId = @PlantId
            ORDER BY aq.SubmittedAt ASC;
            """;

        await using var command = new SqlCommand(sql, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);

        var rows = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                approvalQueueId = reader.GetInt32(0),
                dailyReportId = reader.GetInt32(1),
                status = reader.GetString(2),
                submittedAt = reader.GetDateTime(3),
                reportDate = reader.GetDateTime(4).Date,

                customerName = reader.GetString(5),
                categoryName = reader.GetString(6),
                lineName = reader.GetString(7),

                modelName =
                    reader.IsDBNull(8)
                        ? null
                        : reader.GetString(8),

                submittedBy = reader.GetString(9),
                submissionBatchId = reader.IsDBNull(10) ? (Guid?)null : reader.GetGuid(10),
                incompleteCount = reader.GetInt32(11)
            });
        }

        return Ok(rows);
    }


    // =========================================================
    // APPROVAL HISTORY
    // =========================================================

    [HttpGet("history")]
    public async Task<IActionResult> History(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();
        var plantId = GetPlantId();
        if (plantId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        const string sql = """
            SELECT TOP (100)
                aq.ApprovalQueueId,
                aq.DailyReportId,
                aq.Status,
                aq.SubmittedAt,
                aq.ReviewedAt,
                aq.Comments,
                c.CustomerName,
                cat.CategoryName,
                pl.LineName,
                m.ModelName,
                reviewer.FullName
            FROM ApprovalQueue aq
            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId
            INNER JOIN Customer c
                ON c.CustomerId = dr.CustomerId
            INNER JOIN Category cat
                ON cat.CategoryId = dr.CategoryId
            INNER JOIN ProductionLine pl
                ON pl.LineId = dr.LineId
            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId
            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = dr.PlantId
               AND pc.CustomerId = dr.CustomerId
            LEFT JOIN [User] reviewer
                ON reviewer.UserId = aq.ReviewedBy
            WHERE aq.Status <> 'PENDING'
              AND dr.PlantId = @PlantId
            ORDER BY COALESCE(
                aq.ReviewedAt,
                aq.SubmittedAt
            ) DESC;
            """;

        await using var command = new SqlCommand(sql, connection);

        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);

        var rows = new List<object>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new
            {
                approvalQueueId = reader.GetInt32(0),
                dailyReportId = reader.GetInt32(1),
                status = reader.GetString(2),

                submittedAt = reader.GetDateTime(3),

                reviewedAt =
                    reader.IsDBNull(4)
                        ? (DateTime?)null
                        : reader.GetDateTime(4),

                comments =
                    reader.IsDBNull(5)
                        ? null
                        : reader.GetString(5),

                customerName = reader.GetString(6),
                categoryName = reader.GetString(7),
                lineName = reader.GetString(8),

                modelName =
                    reader.IsDBNull(9)
                        ? null
                        : reader.GetString(9),

                reviewer =
                    reader.IsDBNull(10)
                        ? null
                        : reader.GetString(10)
            });
        }

        return Ok(rows);
    }


    // =========================================================
    // APPROVAL DETAIL
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(
        int id,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();
        var plantId = GetPlantId();
        if (plantId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        /*
         * IMPORTANT:
         *
         * Approval Detail is now completely based on DailyReport.
         *
         * No KPI_Master.
         * No monthly default/target lookup.
         *
         * Contributor enters all values manually every day.
         */

        const string sql = """
            SELECT
                aq.ApprovalQueueId,
                aq.DailyReportId,
                aq.Status,
                aq.SubmittedAt,
                aq.Comments,

                c.CustomerName,
                cat.CategoryName,
                pl.LineName,
                pl.SAPLocation,
                m.ModelName,

                dr.ReportDate,
                creator.FullName,

                dr.MonthPlan,
                dr.MonthActual,

                dr.ProductionPlan,
                dr.ProductionActual,

                dr.UPHTarget,
                dr.UPHActual,

                dr.UPPHInstalled,
                dr.UPPHActual,

                dr.CPHTarget,
                dr.CPHActual,

                dr.FPYTarget,
                dr.FPYActual,

                dr.FTYTarget,
                dr.FTYActual,

                dr.RTYTarget,
                dr.RTYActual,

                dr.OSDReportingDateValue,
                dr.OSDReportingDatePercent,

                dr.OSDMTDValue,
                dr.OSDMTDPercent,

                dr.PlannedOTManhours,
                dr.UnplannedOTManhours,
                dr.ActualOTManhours,

                dr.OpenWOQty,
                dr.Over7DaysWOBalanceQty,

                dr.DailyTRCInQty,
                dr.DailyTRCOutQty,

                dr.TRCOverallFailureInflowPercent,
                dr.TRCLyingOver3DaysCr,

                dr.IssueDescription,

                dr.UPPHTarget,
                dr.OSDTarget,
                dr.OTTarget,
                dr.wip_lying_greater_3_days,
                dr.wip_lying_greater_7_days,
                dr.TextValuesJson

            FROM ApprovalQueue aq

            INNER JOIN DailyReport dr
                ON dr.DailyReportId = aq.DailyReportId

            INNER JOIN Customer c
                ON c.CustomerId = dr.CustomerId

            INNER JOIN Category cat
                ON cat.CategoryId = dr.CategoryId

            INNER JOIN ProductionLine pl
                ON pl.LineId = dr.LineId

            LEFT JOIN Model m
                ON m.ModelId = dr.ModelId

            INNER JOIN [User] creator
                ON creator.UserId = dr.CreatedBy

            INNER JOIN UserCustomer uc
                ON uc.CustomerId = dr.CustomerId
               AND uc.UserId = @UserId
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = dr.PlantId
               AND pc.CustomerId = dr.CustomerId

            WHERE aq.ApprovalQueueId = @Id
              AND dr.PlantId = @PlantId;
            """;

        await using var command =
            new SqlCommand(sql, connection);

        Add(command, "@Id", SqlDbType.Int, id);
        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return NotFound(new
            {
                message =
                    "Approval item not found or not assigned to you."
            });
        }

        return Ok(new
        {
            approvalQueueId = reader.GetInt32(0),
            dailyReportId = reader.GetInt32(1),
            status = reader.GetString(2),
            submittedAt = reader.GetDateTime(3),

            comments =
                reader.IsDBNull(4)
                    ? null
                    : reader.GetString(4),

            customerName = reader.GetString(5),
            categoryName = reader.GetString(6),
            lineName = reader.GetString(7),

            sapLocation =
                reader.IsDBNull(8)
                    ? null
                    : reader.GetString(8),

            modelName =
                reader.IsDBNull(9)
                    ? null
                    : reader.GetString(9),

            reportDate =
                reader.GetDateTime(10)
                    .ToString("yyyy-MM-dd"),

            submittedBy = reader.GetString(11),

            metrics = new
            {
                monthPlan = GetDecimal(reader, 12),
                monthActual = GetDecimal(reader, 13),

                productionPlan = GetDecimal(reader, 14),
                productionActual = GetDecimal(reader, 15),

                uphTarget = GetDecimal(reader, 16),
                uphActual = GetDecimal(reader, 17),

                upphInstalled = GetDecimal(reader, 18),
                upphActual = GetDecimal(reader, 19),

                cphTarget = GetDecimal(reader, 20),
                cphActual = GetDecimal(reader, 21),

                fpyTarget = GetDecimal(reader, 22),
                fpyActual = GetDecimal(reader, 23),

                ftyTarget = GetDecimal(reader, 24),
                ftyActual = GetDecimal(reader, 25),

                rtyTarget = GetDecimal(reader, 26),
                rtyActual = GetDecimal(reader, 27),

                osdReportingDateValue =
                    GetDecimal(reader, 28),

                osdReportingDatePercent =
                    GetDecimal(reader, 29),

                osdMtdValue =
                    GetDecimal(reader, 30),

                osdMtdPercent =
                    GetDecimal(reader, 31),

                plannedOTManhours =
                    GetDecimal(reader, 32),

                unplannedOTManhours =
                    GetDecimal(reader, 33),

                actualOTManhours =
                    GetDecimal(reader, 34),

                openWOQty =
                    GetInt(reader, 35),

                over7DaysWOBalanceQty =
                    GetInt(reader, 36),

                dailyTRCInQty =
                    GetInt(reader, 37),

                dailyTRCOutQty =
                    GetInt(reader, 38),

                trcOverallFailureInflowPercent =
                    GetDecimal(reader, 39),

                trcLyingOver3DaysCr =
                    GetDecimal(reader, 40),

                issueDescription =
                    GetString(reader, 41),
                wipLyingGreater3Days =
                    GetDecimal(reader, 45),
                wipLyingGreater7Days =
                    GetDecimal(reader, 46),
                textValues =
                    ParseTextValues(reader.IsDBNull(47) ? null : reader.GetString(47)),

                plan = new
                {
                    productionPlan = GetDecimal(reader, 14),
                    uphTarget = GetDecimal(reader, 16),
                    upphTarget = GetDecimal(reader, 42),
                    cphTarget = GetDecimal(reader, 20),
                    fpyTarget = GetDecimal(reader, 22),
                    ftyTarget = GetDecimal(reader, 24),
                    rtyTarget = GetDecimal(reader, 26),
                    osdTarget = GetDecimal(reader, 43),
                    otTarget = GetDecimal(reader, 44)
                }
            }
        });
    }


    // =========================================================
    // BATCH REVIEW
    // =========================================================

    [HttpGet("batch/{batchId:guid}")]
    public async Task<IActionResult> BatchDetail(Guid batchId, CancellationToken cancellationToken)
    {
        var userId = GetUserId(); var plantId = GetPlantId();
        if (userId <= 0 || plantId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT aq.ApprovalQueueId, dr.DailyReportId, aq.Status, aq.SubmittedAt,
                   dr.ReportDate, c.CustomerName, cat.CategoryName, pl.LineName, pl.SAPLocation,
                   m.ModelName, submitter.FullName, dr.ProductionPlan, dr.ProductionActual,
                   dr.UPHTarget, dr.UPHActual, dr.UPPHTarget, dr.UPPHActual, dr.CPHTarget, dr.CPHActual,
                   dr.FPYTarget, dr.FPYActual, dr.FTYTarget, dr.FTYActual, dr.RTYTarget, dr.RTYActual,
                   dr.MonthPlan, dr.MonthActual, dr.OSDReportingDateValue, dr.OSDReportingDatePercent,
                   dr.OSDMTDValue, dr.OSDMTDPercent, dr.PlannedOTManhours, dr.UnplannedOTManhours,
                   dr.ActualOTManhours, dr.OpenWOQty, dr.Over7DaysWOBalanceQty, dr.DailyTRCInQty,
                   dr.DailyTRCOutQty, dr.TRCOverallFailureInflowPercent, dr.TRCLyingOver3DaysCr,
                   dr.IssueDescription, dr.RejectionReason, dr.UPPHInstalled, dr.OSDTarget, dr.OTTarget,
                   dr.wip_lying_greater_3_days, dr.wip_lying_greater_7_days, dr.TextValuesJson
            FROM DailyReport dr
            INNER JOIN ApprovalQueue aq ON aq.DailyReportId=dr.DailyReportId
            INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
            INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId
            LEFT JOIN Model m ON m.ModelId=dr.ModelId
            INNER JOIN [User] submitter ON submitter.UserId=dr.CreatedBy
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            WHERE dr.SubmissionBatchId=@BatchId AND dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.ModelId IS NOT NULL
            ORDER BY pl.LineName, m.ModelName;
            """;
        await using var command = new SqlCommand(sql, connection);
        Add(command,"@BatchId",SqlDbType.UniqueIdentifier,batchId); Add(command,"@PlantId",SqlDbType.Int,plantId); Add(command,"@UserId",SqlDbType.Int,userId);
        var rows = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var reportId = reader.GetInt32(1);
            rows.Add(new {
                approvalQueueId=reader.GetInt32(0), dailyReportId=reportId, status=reader.GetString(2), submittedAt=reader.GetDateTime(3),
                reportDate=reader.GetDateTime(4).ToString("yyyy-MM-dd"), customerName=reader.GetString(5), categoryName=reader.GetString(6),
                lineName=reader.GetString(7), sapLocation=reader.IsDBNull(8)?null:reader.GetString(8), modelName=reader.IsDBNull(9)?null:reader.GetString(9), submittedBy=reader.GetString(10),
                metrics=new {
                    productionPlan=GetDecimal(reader,11), productionActual=GetDecimal(reader,12), uphTarget=GetDecimal(reader,13), uphActual=GetDecimal(reader,14),
                    upphTarget=GetDecimal(reader,15), upphActual=GetDecimal(reader,16), cphTarget=GetDecimal(reader,17), cphActual=GetDecimal(reader,18),
                    fpyTarget=GetDecimal(reader,19), fpyActual=GetDecimal(reader,20), ftyTarget=GetDecimal(reader,21), ftyActual=GetDecimal(reader,22),
                    rtyTarget=GetDecimal(reader,23), rtyActual=GetDecimal(reader,24), monthPlan=GetDecimal(reader,25), monthActual=GetDecimal(reader,26),
                    osdReportingDateValue=GetDecimal(reader,27), osdReportingDatePercent=GetDecimal(reader,28), osdMtdValue=GetDecimal(reader,29), osdMtdPercent=GetDecimal(reader,30),
                    plannedOTManhours=GetDecimal(reader,31), unplannedOTManhours=GetDecimal(reader,32), actualOTManhours=GetDecimal(reader,33),
                    openWOQty=GetInt(reader,34), over7DaysWOBalanceQty=GetInt(reader,35), dailyTRCInQty=GetInt(reader,36), dailyTRCOutQty=GetInt(reader,37),
                    trcOverallFailureInflowPercent=GetDecimal(reader,38), trcLyingOver3DaysCr=GetDecimal(reader,39), issueDescription=GetString(reader,40), rejectionReason=GetString(reader,41),
                    upphInstalled=GetDecimal(reader,42), osdTarget=GetDecimal(reader,43), otTarget=GetDecimal(reader,44),
                    wipLyingGreater3Days=GetInt(reader,45), wipLyingGreater7Days=GetInt(reader,46),
                    textValues=ParseTextValues(reader.IsDBNull(47)?null:reader.GetString(47))
                }
            });
        }
        if (rows.Count == 0) return NotFound(new { message="Approval batch not found or outside your plant/customer assignment." });
        return Ok(rows);
    }

    [HttpGet("batch/{batchId:guid}/annotations")]
    public async Task<IActionResult> BatchAnnotations(Guid batchId,CancellationToken cancellationToken)
    {
        var userId=GetUserId();var plantId=GetPlantId();if(userId<=0||plantId<=0)return Unauthorized();
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        const string sql="""
            SELECT a.AnnotationId,a.DailyReportId,a.FieldKey,a.Severity,a.Comment,a.OriginalValue,a.CorrectedValue,a.CreatedAt,a.ResolvedAt,creator.FullName,resolver.FullName
            FROM ReportFieldAnnotation a INNER JOIN DailyReport dr ON dr.DailyReportId=a.DailyReportId
            INNER JOIN [User] creator ON creator.UserId=a.CreatedBy LEFT JOIN [User] resolver ON resolver.UserId=a.ResolvedBy
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            WHERE dr.SubmissionBatchId=@BatchId AND dr.PlantId=@PlantId ORDER BY a.CreatedAt,a.AnnotationId;
            """;
        await using var command=new SqlCommand(sql,connection);Add(command,"@BatchId",SqlDbType.UniqueIdentifier,batchId);Add(command,"@PlantId",SqlDbType.Int,plantId);Add(command,"@UserId",SqlDbType.Int,userId);
        var rows=new List<object>();await using var reader=await command.ExecuteReaderAsync(cancellationToken);while(await reader.ReadAsync(cancellationToken))rows.Add(new{annotationId=reader.GetInt64(0),dailyReportId=reader.GetInt32(1),fieldKey=reader.GetString(2),severity=reader.GetString(3),comment=reader.GetString(4),originalValue=reader.IsDBNull(5)?null:reader.GetString(5),correctedValue=reader.IsDBNull(6)?null:reader.GetString(6),createdAt=reader.GetDateTime(7),resolvedAt=reader.IsDBNull(8)?(DateTime?)null:reader.GetDateTime(8),createdBy=reader.GetString(9),resolvedBy=reader.IsDBNull(10)?null:reader.GetString(10)});return Ok(rows);
    }

    [HttpPost("batch/{batchId:guid}/reject")]
    public Task<IActionResult> RejectBatch(Guid batchId, [FromBody] BatchReviewRequest request, CancellationToken cancellationToken)
        => ReviewBatch(batchId, false, request, cancellationToken);

    [HttpPost("batch/{batchId:guid}/approve")]
    public Task<IActionResult> ApproveBatch(Guid batchId, CancellationToken cancellationToken)
        => ReviewBatch(batchId, true, new BatchReviewRequest(), cancellationToken);

    private async Task<IActionResult> ReviewBatch(Guid batchId, bool approve, BatchReviewRequest request, CancellationToken cancellationToken)
    {
        if (!approve && string.IsNullOrWhiteSpace(request.Comment)) return BadRequest(new { message="Add a general review comment before returning this report." });
        if (!approve && (request.Fields?.Count ?? 0) > 80) return BadRequest(new { message="A review can contain up to 80 field annotations." });
        var userId=GetUserId(); var plantId=GetPlantId(); if(userId<=0||plantId<=0)return Unauthorized();
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        await using var tx=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken);
        try
        {
            const string rowsSql="""
                SELECT dr.DailyReportId, aq.Status, dr.Status, dr.ReportDate, dr.CustomerId, dr.CategoryId, dr.CreatedBy, dr.SubmissionBatchEnforced
                FROM DailyReport dr WITH (UPDLOCK,HOLDLOCK)
                INNER JOIN ApprovalQueue aq WITH (UPDLOCK,HOLDLOCK) ON aq.DailyReportId=dr.DailyReportId
                INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
                INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
                WHERE dr.SubmissionBatchId=@BatchId AND dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.ModelId IS NOT NULL;
                """;
            var ids=new List<int>();var batchCategoryIds=new HashSet<int>();DateTime? batchDate=null;int batchCustomer=0,batchSubmitter=0;bool batchEnforced=false;
            await using(var rowsCommand=new SqlCommand(rowsSql,connection,tx))
            {
                Add(rowsCommand,"@BatchId",SqlDbType.UniqueIdentifier,batchId); Add(rowsCommand,"@PlantId",SqlDbType.Int,plantId); Add(rowsCommand,"@UserId",SqlDbType.Int,userId);
                await using var reader=await rowsCommand.ExecuteReaderAsync(cancellationToken);
                while(await reader.ReadAsync(cancellationToken))
                {
                    if(reader.GetString(1)!="PENDING"||reader.GetString(2)!="SUBMITTED")
                    { await tx.RollbackAsync(cancellationToken); return Conflict(new { message="Every active line/model entry in this daily batch must be submitted and pending before the batch can be reviewed." }); }
                    batchDate ??=reader.GetDateTime(3);batchCustomer=reader.GetInt32(4);batchCategoryIds.Add(reader.GetInt32(5));batchSubmitter=reader.GetInt32(6);
                    batchEnforced |= reader.GetBoolean(7);
                    ids.Add(reader.GetInt32(0));
                }
            }
            if(ids.Count==0){await tx.RollbackAsync(cancellationToken);return NotFound(new { message="Batch not found or outside your plant/customer assignment." });}
            if(batchEnforced)
            {
                var batchAcrossCategories=batchCategoryIds.Count>1;
                const string incompleteSql="""
                    SELECT
                      (SELECT COUNT(1) FROM DailyReport dr WITH (UPDLOCK,HOLDLOCK)
                       INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
                       WHERE dr.PlantId=@PlantId AND dr.ReportDate=@Date AND dr.CustomerId=@CustomerId AND dr.CreatedBy=@Submitter
                         AND dr.IsActive=1 AND dr.ModelId IS NOT NULL AND dr.Status NOT IN ('APPROVED','INACTIVE')
                         AND (dr.Status<>'SUBMITTED' OR dr.SubmissionBatchId<>@BatchId)
                         AND ((@AcrossCategories=0 AND dr.CategoryId=@CategoryId)
                           OR (@AcrossCategories=1 AND cat.PlantId=@PlantId AND cat.IsActive=1 AND UPPER(cat.CategoryName) IN ('FATP','SMT'))))
                      + (SELECT COUNT(1) FROM ProductionLine pl INNER JOIN Category cat ON cat.CategoryId=pl.CategoryId
                         WHERE pl.PlantId=@PlantId AND pl.IsActive=1 AND cat.PlantId=@PlantId AND cat.CustomerId=@CustomerId AND cat.IsActive=1
                           AND ((@AcrossCategories=0 AND cat.CategoryId=@CategoryId)
                             OR (@AcrossCategories=1 AND UPPER(cat.CategoryName) IN ('FATP','SMT')))
                           AND NOT EXISTS(SELECT 1 FROM DailyReport lineState WHERE lineState.PlantId=@PlantId AND lineState.ReportDate=@Date AND lineState.CustomerId=@CustomerId AND lineState.CategoryId=cat.CategoryId AND lineState.LineId=pl.LineId AND lineState.CreatedBy=@Submitter AND ((lineState.ModelId IS NULL AND lineState.IsActive=0) OR (lineState.ModelId IS NOT NULL AND lineState.IsActive=1))));
                    """;
                await using var incomplete=new SqlCommand(incompleteSql,connection,tx);Add(incomplete,"@PlantId",SqlDbType.Int,plantId);Add(incomplete,"@Date",SqlDbType.Date,batchDate);Add(incomplete,"@CustomerId",SqlDbType.Int,batchCustomer);Add(incomplete,"@CategoryId",SqlDbType.Int,batchCategoryIds.First());Add(incomplete,"@AcrossCategories",SqlDbType.Bit,batchAcrossCategories);Add(incomplete,"@Submitter",SqlDbType.Int,batchSubmitter);Add(incomplete,"@BatchId",SqlDbType.UniqueIdentifier,batchId);
                if(Convert.ToInt32(await incomplete.ExecuteScalarAsync(cancellationToken))>0){await tx.RollbackAsync(cancellationToken);return Conflict(new{message="This reporting date has draft or unsubmitted model entries, or active lines without a submitted report. Submit each active line or mark unused lines inactive before reviewing the complete daily batch."});}
            }
            var seen=new HashSet<(int,string)>(new FieldAnnotationKeyComparer());
            if(!approve)
            {
                foreach(var field in request.Fields??[])
                {
                    if(!ids.Contains(field.DailyReportId)||!AnnotationColumns.TryGetValue(field.FieldKey??"",out var column)||
                       (field.Severity!="RED"&&field.Severity!="YELLOW")||string.IsNullOrWhiteSpace(field.Comment)||field.Comment.Length>1000||
                       !seen.Add((field.DailyReportId,field.FieldKey!)))
                    { await tx.RollbackAsync(cancellationToken); return BadRequest(new { message="Each annotation must target a field in this batch, use RED or YELLOW, and include one specific comment." }); }
                    string? original;
                    await using(var valueCommand=new SqlCommand($"SELECT CONVERT(nvarchar(4000),[{column}]) FROM DailyReport WHERE DailyReportId=@ReportId AND PlantId=@PlantId;",connection,tx))
                    { Add(valueCommand,"@ReportId",SqlDbType.Int,field.DailyReportId); Add(valueCommand,"@PlantId",SqlDbType.Int,plantId); original=Convert.ToString(await valueCommand.ExecuteScalarAsync(cancellationToken)); }
                    const string insertAnnotation="INSERT INTO ReportFieldAnnotation(DailyReportId,SubmissionBatchId,FieldKey,Severity,Comment,OriginalValue,CreatedBy) VALUES(@ReportId,@BatchId,@Field,@Severity,@Comment,@Original,@UserId);";
                    await using var insert=new SqlCommand(insertAnnotation,connection,tx);
                    Add(insert,"@ReportId",SqlDbType.Int,field.DailyReportId); Add(insert,"@BatchId",SqlDbType.UniqueIdentifier,batchId); Add(insert,"@Field",SqlDbType.VarChar,field.FieldKey);
                    Add(insert,"@Severity",SqlDbType.VarChar,field.Severity); Add(insert,"@Comment",SqlDbType.VarChar,field.Comment.Trim()); Add(insert,"@Original",SqlDbType.NVarChar,original); Add(insert,"@UserId",SqlDbType.Int,userId);
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            var outcome=approve?"APPROVED":"REJECTED";
            const string updateSql="""
                UPDATE aq SET Status=@Status,ReviewedBy=@UserId,ReviewedAt=SYSUTCDATETIME(),Comments=@Comment,UpdatedAt=SYSUTCDATETIME()
                FROM ApprovalQueue aq INNER JOIN DailyReport dr ON dr.DailyReportId=aq.DailyReportId
                WHERE dr.SubmissionBatchId=@BatchId AND dr.PlantId=@PlantId AND aq.Status='PENDING';
                UPDATE dr SET Status=@Status,ApprovedBy=CASE WHEN @Status='APPROVED' THEN @UserId ELSE NULL END,
                    ApprovedAt=CASE WHEN @Status='APPROVED' THEN SYSUTCDATETIME() ELSE NULL END,
                    RejectionReason=CASE WHEN @Status='REJECTED' THEN @Comment ELSE NULL END,UpdatedAt=SYSUTCDATETIME()
                FROM DailyReport dr WHERE dr.SubmissionBatchId=@BatchId AND dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.ModelId IS NOT NULL;
                INSERT INTO ReportApproval(DailyReportId,ActionBy,ActionType,Comments,ActionAt)
                SELECT dr.DailyReportId,@UserId,@Status,@Comment,SYSUTCDATETIME()
                FROM DailyReport dr WHERE dr.SubmissionBatchId=@BatchId AND dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.ModelId IS NOT NULL;
                """;
            await using(var update=new SqlCommand(updateSql,connection,tx))
            { Add(update,"@Status",SqlDbType.VarChar,outcome); Add(update,"@UserId",SqlDbType.Int,userId); Add(update,"@Comment",SqlDbType.VarChar,approve?null:request.Comment!.Trim()); Add(update,"@BatchId",SqlDbType.UniqueIdentifier,batchId); Add(update,"@PlantId",SqlDbType.Int,plantId); await update.ExecuteNonQueryAsync(cancellationToken); }
            if(approve) foreach(var reportId in ids) await CreateFinalKpiSnapshotAsync(connection,tx,reportId,cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Ok(new { status=outcome, reviewedCount=ids.Count, message=approve?"Daily report batch approved and locked.":"Daily report batch returned with reviewer annotations." });
        }
        catch { await tx.RollbackAsync(cancellationToken); throw; }
    }

    private static readonly IReadOnlyDictionary<string,string> AnnotationColumns = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["monthPlan"]="MonthPlan",["monthActual"]="MonthActual",["productionPlan"]="ProductionPlan",["productionActual"]="ProductionActual",
        ["uphTarget"]="UPHTarget",["uphActual"]="UPHActual",["upphTarget"]="UPPHTarget",["upphInstalled"]="UPPHInstalled",["upphActual"]="UPPHActual",
        ["cphTarget"]="CPHTarget",["cphActual"]="CPHActual",["fpyTarget"]="FPYTarget",["fpyActual"]="FPYActual",["ftyTarget"]="FTYTarget",["ftyActual"]="FTYActual",
        ["rtyTarget"]="RTYTarget",["rtyActual"]="RTYActual",["osdTarget"]="OSDTarget",["osdReportingDateValue"]="OSDReportingDateValue",["osdReportingDatePercent"]="OSDReportingDatePercent",
        ["osdMtdValue"]="OSDMTDValue",["osdMtdPercent"]="OSDMTDPercent",["otTarget"]="OTTarget",["plannedOTManhours"]="PlannedOTManhours",["unplannedOTManhours"]="UnplannedOTManhours",
        ["actualOTManhours"]="ActualOTManhours",["openWOQty"]="OpenWOQty",["over7DaysWOBalanceQty"]="Over7DaysWOBalanceQty",["dailyTRCInQty"]="DailyTRCInQty",["dailyTRCOutQty"]="DailyTRCOutQty",
        ["wipLyingGreater3Days"]="wip_lying_greater_3_days",["wipLyingGreater7Days"]="wip_lying_greater_7_days",
        ["trcOverallFailureInflowPercent"]="TRCOverallFailureInflowPercent",["trcLyingOver3DaysCr"]="TRCLyingOver3DaysCr",["issueDescription"]="IssueDescription"
    };
    private sealed class FieldAnnotationKeyComparer : IEqualityComparer<(int,string)>
    { public bool Equals((int,string) x,(int,string) y)=>x.Item1==y.Item1&&StringComparer.OrdinalIgnoreCase.Equals(x.Item2,y.Item2); public int GetHashCode((int,string) value)=>HashCode.Combine(value.Item1,StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2)); }

    // =========================================================
    // APPROVE
    // =========================================================

    [HttpPost("{id:int}/approve")]
    public Task<IActionResult> Approve(
        int id,
        CancellationToken cancellationToken)
    {
        return Review(
            id,
            approve: true,
            comment: null,
            cancellationToken);
    }


    // =========================================================
    // REJECT
    // =========================================================

    [HttpPost("{id:int}/reject")]
    public Task<IActionResult> Reject(
        int id,
        [FromBody] ReviewRequest request,
        CancellationToken cancellationToken)
    {
        return Review(
            id,
            approve: false,
            comment: request.Comment,
            cancellationToken);
    }


    // =========================================================
    // REVIEW
    // =========================================================

    private async Task<IActionResult> Review(
        int id,
        bool approve,
        string? comment,
        CancellationToken cancellationToken)
    {
        if (!approve && string.IsNullOrWhiteSpace(comment))
        {
            return BadRequest(new
            {
                message = "A rejection comment is required."
            });
        }

        var userId = GetUserId();

        if (userId <= 0)
            return Unauthorized();
        var plantId = GetPlantId();
        if (plantId <= 0)
            return Unauthorized();

        await using var connection =
            await connectionFactory.OpenAsync(cancellationToken);

        await using var tx =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        int? dailyReportIdForLog = null;

        try
        {
            // -------------------------------------------------
            // Get approval + DailyReport
            // -------------------------------------------------

            const string checkSql = """
                SELECT
                    aq.DailyReportId,
                    aq.Status,
                    dr.CustomerId,
                    dr.SubmissionBatchEnforced
                FROM ApprovalQueue aq

                INNER JOIN DailyReport dr
                    ON dr.DailyReportId = aq.DailyReportId

                INNER JOIN UserCustomer uc
                    ON uc.CustomerId = dr.CustomerId
                   AND uc.UserId = @UserId
                INNER JOIN PlantCustomer pc
                    ON pc.PlantId = dr.PlantId
                   AND pc.CustomerId = dr.CustomerId

                WHERE aq.ApprovalQueueId = @Id
                  AND dr.PlantId = @PlantId;
                """;

            await using var checkCommand =
                new SqlCommand(
                    checkSql,
                    connection,
                    tx);

            Add(
                checkCommand,
                "@Id",
                SqlDbType.Int,
                id);

            Add(
                checkCommand,
                "@UserId",
                SqlDbType.Int,
                userId);
            Add(
                checkCommand,
                "@PlantId",
                SqlDbType.Int,
                plantId);

            await using var reader =
                await checkCommand.ExecuteReaderAsync(
                    cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                await tx.RollbackAsync(cancellationToken);

                return NotFound(new
                {
                    message =
                        "Approval item not found or not assigned to you."
                });
            }

            var dailyReportId = reader.GetInt32(0);
            dailyReportIdForLog = dailyReportId;
            var status = reader.GetString(1);
            var batchEnforced = reader.GetBoolean(3);

            await reader.CloseAsync();

            if (batchEnforced)
            {
                await tx.RollbackAsync(cancellationToken);
                return Conflict(new { message="This report belongs to a consolidated daily batch. Review the complete batch from the review queue." });
            }

            if (!string.Equals(
                    status,
                    "PENDING",
                    StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync(cancellationToken);

                return Conflict(new
                {
                    message =
                        "This approval item has already been reviewed."
                });
            }


            // -------------------------------------------------
            // REJECT
            // -------------------------------------------------

            if (!approve)
            {
                const string rejectSql = """
                    UPDATE ApprovalQueue
                    SET
                        Status = 'REJECTED',
                        ReviewedBy = @UserId,
                        ReviewedAt = SYSUTCDATETIME(),
                        Comments = @Comments,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE ApprovalQueueId = @Id;

                    UPDATE DailyReport
                    SET
                        Status = 'REJECTED',
                        ApprovedBy = NULL,
                        ApprovedAt = NULL,
                        RejectionReason = @Comments,
                        UpdatedAt = SYSUTCDATETIME()
                    WHERE DailyReportId = @DailyReportId;

                    INSERT INTO ReportApproval
                    (
                        DailyReportId,
                        ActionBy,
                        ActionType,
                        Comments,
                        ActionAt
                    )
                    VALUES
                    (
                        @DailyReportId,
                        @UserId,
                        'REJECTED',
                        @Comments,
                        SYSUTCDATETIME()
                    );
                    """;

                await using var rejectCommand =
                    new SqlCommand(
                        rejectSql,
                        connection,
                        tx);

                Add(
                    rejectCommand,
                    "@Status",
                    SqlDbType.VarChar,
                    "REJECTED");

                Add(
                    rejectCommand,
                    "@UserId",
                    SqlDbType.Int,
                    userId);

                Add(
                    rejectCommand,
                    "@Comments",
                    SqlDbType.VarChar,
                    comment?.Trim());

                Add(
                    rejectCommand,
                    "@Id",
                    SqlDbType.Int,
                    id);

                Add(
                    rejectCommand,
                    "@DailyReportId",
                    SqlDbType.Int,
                    dailyReportId);

                await rejectCommand.ExecuteNonQueryAsync(
                    cancellationToken);

                await tx.CommitAsync(cancellationToken);

                return Ok(new
                {
                    message =
                        "Report rejected and returned to contributor.",

                    status = "REJECTED"
                });
            }


            // -------------------------------------------------
            // APPROVE
            // -------------------------------------------------
            //
            // First create/update final KPI_Daily snapshot.
            // Then mark DailyReport + ApprovalQueue approved.
            //

            await CreateFinalKpiSnapshotAsync(
                connection,
                tx,
                dailyReportId,
                cancellationToken);


            // -------------------------------------------------
            // Mark approval as APPROVED
            // -------------------------------------------------

            const string approveSql = """
                UPDATE ApprovalQueue
                SET
                    Status = 'APPROVED',
                    ReviewedBy = @UserId,
                    ReviewedAt = SYSUTCDATETIME(),
                    Comments = NULL,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE ApprovalQueueId = @Id;

                UPDATE DailyReport
                SET
                    Status = 'APPROVED',
                    ApprovedBy = @UserId,
                    ApprovedAt = SYSUTCDATETIME(),
                    RejectionReason = NULL,
                    UpdatedAt = SYSUTCDATETIME()
                WHERE DailyReportId = @DailyReportId;

                INSERT INTO ReportApproval
                (
                    DailyReportId,
                    ActionBy,
                    ActionType,
                    Comments,
                    ActionAt
                )
                VALUES
                (
                    @DailyReportId,
                    @UserId,
                    'APPROVED',
                    NULL,
                    SYSUTCDATETIME()
                );
                """;

            await using var approveCommand =
                new SqlCommand(
                    approveSql,
                    connection,
                    tx);

            Add(
                approveCommand,
                "@UserId",
                SqlDbType.Int,
                userId);

            Add(
                approveCommand,
                "@Id",
                SqlDbType.Int,
                id);

            Add(
                approveCommand,
                "@DailyReportId",
                SqlDbType.Int,
                dailyReportId);

            await approveCommand.ExecuteNonQueryAsync(
                cancellationToken);


            await tx.CommitAsync(cancellationToken);

            return Ok(new
            {
                message =
                    "Report approved and final KPI snapshot created.",

                status = "APPROVED"
            });
        }
        catch (SqlException ex)
        {
            await tx.RollbackAsync(cancellationToken);

            if (environment.IsDevelopment())
            {
                logger.LogError(
                    ex,
                    "Approval review failed. ApprovalQueueId={ApprovalQueueId}, DailyReportId={DailyReportId}, Approve={Approve}, UserId={UserId}, SqlNumber={SqlNumber}, SqlState={SqlState}, SqlProcedure={SqlProcedure}, SqlLineNumber={SqlLineNumber}, SqlMessage={SqlMessage}",
                    id,
                    dailyReportIdForLog,
                    approve,
                    userId,
                    ex.Number,
                    ex.State,
                    ex.Procedure,
                    ex.LineNumber,
                    ex.Message);

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        message = "Approval review failed.",
                        sqlError = new
                        {
                            ex.Message,
                            ex.Number,
                            ex.State,
                            ex.Procedure,
                            ex.LineNumber
                        }
                    });
            }

            throw;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }


    // =========================================================
    // CREATE FINAL KPI SNAPSHOT
    // =========================================================

    private static async Task CreateFinalKpiSnapshotAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int dailyReportId,
        CancellationToken cancellationToken)
    {
        /*
         * KPI_Daily is the FINAL approved snapshot.
         *
         * DailyReport = editable/submitted source
         * KPI_Daily    = approved/final data
         *
         * KPI_Master is intentionally NOT used.
         */

        const string sql = """
            IF EXISTS
            (
                SELECT 1
                FROM KPI_Daily
                WHERE DailyReportId = @DailyReportId
            )
            BEGIN
                UPDATE kd
                SET
                    ModelId = dr.ModelId,
                    ReportDate = dr.ReportDate,

                    MonthPlan = dr.MonthPlan,
                    MonthActual = dr.MonthActual,

                    ProductionPlan = dr.ProductionPlan,
                    ProductionActual = dr.ProductionActual,

                    UPHTarget = dr.UPHTarget,
                    UPHActual = dr.UPHActual,

                    UPPHTarget = dr.UPPHTarget,
                    UPPHInstalled = dr.UPPHInstalled,
                    UPPHActual = dr.UPPHActual,
                    CPHTarget = dr.CPHTarget,
                    CPHActual = dr.CPHActual,

                    FPYTarget = dr.FPYTarget,
                    FPYActual = dr.FPYActual,

                    FTYTarget = dr.FTYTarget,
                    FTYActual = dr.FTYActual,

                    RTYTarget = dr.RTYTarget,
                    RTYActual = dr.RTYActual,

                    OSDReportingDateValue = dr.OSDReportingDateValue,
                    OSDReportingDatePercent = dr.OSDReportingDatePercent,

                    OSDMTDValue = dr.OSDMTDValue,
                    OSDMTDPercent = dr.OSDMTDPercent,
                    OSDTarget = dr.OSDTarget,

                    PlannedOTManhours = dr.PlannedOTManhours,
                    UnplannedOTManhours = dr.UnplannedOTManhours,
                    OTTarget = dr.OTTarget,
                    ActualOTManhours = dr.ActualOTManhours,

                    OpenWOQty = dr.OpenWOQty,
                    Over7DaysWOBalanceQty = dr.Over7DaysWOBalanceQty,
                    wip_lying_greater_3_days = dr.wip_lying_greater_3_days,
                    wip_lying_greater_7_days = dr.wip_lying_greater_7_days,

                    DailyTRCInQty = dr.DailyTRCInQty,
                    DailyTRCOutQty = dr.DailyTRCOutQty,

                    TRCOverallFailureInflowPercent = dr.TRCOverallFailureInflowPercent,
                    TRCLyingOver3DaysCr = dr.TRCLyingOver3DaysCr,

                    IssueDescription = dr.IssueDescription,
                    TextValuesJson = dr.TextValuesJson,
                    UpdatedAt = SYSUTCDATETIME()

                FROM KPI_Daily kd
                INNER JOIN DailyReport dr
                    ON dr.DailyReportId = kd.DailyReportId
                WHERE kd.DailyReportId = @DailyReportId;
            END
            ELSE
            BEGIN
                INSERT INTO KPI_Daily
                (
                    DailyReportId,
                    ModelId,
                    ReportDate,

                    MonthPlan,
                    MonthActual,

                    ProductionPlan,
                    ProductionActual,

                    UPHTarget,
                    UPHActual,

                    UPPHTarget,
                    UPPHInstalled,
                    UPPHActual,

                    CPHTarget,
                    CPHActual,

                    FPYTarget,
                    FPYActual,

                    FTYTarget,
                    FTYActual,

                    RTYTarget,
                    RTYActual,

                    OSDReportingDateValue,
                    OSDReportingDatePercent,

                    OSDMTDValue,
                    OSDMTDPercent,
                    OSDTarget,

                    PlannedOTManhours,
                    UnplannedOTManhours,
                    OTTarget,
                    ActualOTManhours,

                    OpenWOQty,
                    Over7DaysWOBalanceQty,
                    wip_lying_greater_3_days,
                    wip_lying_greater_7_days,

                    DailyTRCInQty,
                    DailyTRCOutQty,

                    TRCOverallFailureInflowPercent,
                    TRCLyingOver3DaysCr,

                    IssueDescription,
                    TextValuesJson,
                    CreatedAt,
                    UpdatedAt
                )
                SELECT
                    dr.DailyReportId,
                    dr.ModelId,
                    dr.ReportDate,

                    dr.MonthPlan,
                    dr.MonthActual,

                    dr.ProductionPlan,
                    dr.ProductionActual,

                    dr.UPHTarget,
                    dr.UPHActual,

                    dr.UPPHTarget,
                    dr.UPPHInstalled,
                    dr.UPPHActual,

                    dr.CPHTarget,
                    dr.CPHActual,

                    dr.FPYTarget,
                    dr.FPYActual,

                    dr.FTYTarget,
                    dr.FTYActual,

                    dr.RTYTarget,
                    dr.RTYActual,

                    dr.OSDReportingDateValue,
                    dr.OSDReportingDatePercent,

                    dr.OSDMTDValue,
                    dr.OSDMTDPercent,
                    dr.OSDTarget,

                    dr.PlannedOTManhours,
                    dr.UnplannedOTManhours,
                    dr.OTTarget,
                    dr.ActualOTManhours,

                    dr.OpenWOQty,
                    dr.Over7DaysWOBalanceQty,
                    dr.wip_lying_greater_3_days,
                    dr.wip_lying_greater_7_days,

                    dr.DailyTRCInQty,
                    dr.DailyTRCOutQty,

                    dr.TRCOverallFailureInflowPercent,
                    dr.TRCLyingOver3DaysCr,

                    dr.IssueDescription,
                    dr.TextValuesJson,
                    SYSUTCDATETIME(),
                    SYSUTCDATETIME()

                FROM DailyReport dr
                WHERE dr.DailyReportId = @DailyReportId;
            END
            """;

        await using var command =
            new SqlCommand(
                sql,
                connection,
                transaction);

        Add(
            command,
            "@DailyReportId",
            SqlDbType.Int,
            dailyReportId);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }


    // =========================================================
    // HELPERS
    // =========================================================

    private int GetUserId()
    {
        return int.TryParse(
            User.FindFirstValue(
                ClaimTypes.NameIdentifier),
            out var id)
            ? id
            : 0;
    }

    private int GetPlantId()
    {
        return int.TryParse(User.FindFirstValue("plantId"), out var plantId)
            ? plantId
            : 0;
    }


    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        var parameter =
            command.Parameters.Add(name, type);

        parameter.Value =
            value ?? DBNull.Value;
    }


    private static decimal? GetDecimal(
        SqlDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : Convert.ToDecimal(
                reader.GetValue(index));
    }


    private static int? GetInt(
        SqlDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : Convert.ToInt32(
                reader.GetValue(index));
    }

    private static IReadOnlyDictionary<string, string> ParseTextValues(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return parsed is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }


    private static string? GetString(
        SqlDataReader reader,
        int index)
    {
        return reader.IsDBNull(index)
            ? null
            : reader.GetString(index);
    }
}


// =============================================================
// REQUEST MODEL
// =============================================================

public sealed record ReviewRequest(string? Comment);
public sealed class BatchReviewRequest
{
    public string? Comment { get; set; }
    public List<BatchFieldAnnotationRequest>? Fields { get; set; }
}
public sealed class BatchFieldAnnotationRequest
{
    public int DailyReportId { get; set; }
    public string? FieldKey { get; set; }
    public string? Severity { get; set; }
    public string? Comment { get; set; }
}
