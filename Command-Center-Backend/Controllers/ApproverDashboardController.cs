using System.Data;
using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Approver")]
[Route("api/approver")]
public sealed class ApproverDashboardController(SqlConnectionFactory connectionFactory) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate, CancellationToken cancellationToken)
    {
        if (!TryScope(out var userId, out var plantId)) return Unauthorized();
        var end=(toDate??DateTime.Today).Date; var start=(fromDate??end.AddDays(-13)).Date;
        if(start>end)return BadRequest(new{message="Start date must be on or before end date."});
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        const string totalsSql="""
            SELECT COUNT(DISTINCT dr.DailyReportId) totalReports,
                COUNT(DISTINCT CASE WHEN aq.Status='PENDING' AND dr.Status='SUBMITTED' THEN dr.DailyReportId END) pending,
                COUNT(DISTINCT CASE WHEN dr.Status='APPROVED' THEN dr.DailyReportId END) approved,
                COUNT(DISTINCT CASE WHEN dr.Status='REJECTED' THEN dr.DailyReportId END) rejected,
                COALESCE(SUM(CASE WHEN dr.ReportDate=@Today AND dr.Status='APPROVED' THEN dr.ProductionPlan ELSE 0 END),0) todayTarget,
                COALESCE(SUM(CASE WHEN dr.ReportDate=@Today AND dr.Status='APPROVED' THEN dr.ProductionActual ELSE 0 END),0) todayActual
            FROM DailyReport dr
            LEFT JOIN ApprovalQueue aq ON aq.DailyReportId=dr.DailyReportId
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            WHERE dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.ModelId IS NOT NULL AND dr.ReportDate BETWEEN @Start AND @End;
            """;
        await using var totals=new SqlCommand(totalsSql,connection);Add(totals,"@UserId",SqlDbType.Int,userId);Add(totals,"@PlantId",SqlDbType.Int,plantId);Add(totals,"@Today",SqlDbType.Date,DateTime.Today);Add(totals,"@Start",SqlDbType.Date,start);Add(totals,"@End",SqlDbType.Date,end);
        object? summary=null;await using(var r=await totals.ExecuteReaderAsync(cancellationToken)){if(await r.ReadAsync(cancellationToken))summary=new{totalReports=r.GetInt32(0),pending=r.GetInt32(1),approved=r.GetInt32(2),rejected=r.GetInt32(3),todayTarget=Convert.ToDecimal(r.GetValue(4)),todayActual=Convert.ToDecimal(r.GetValue(5))};}
        const string categorySql="""
            SELECT UPPER(cat.CategoryName),COUNT(1),COALESCE(SUM(dr.ProductionPlan),0),COALESCE(SUM(dr.ProductionActual),0)
            FROM DailyReport dr INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId
            INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            WHERE dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.Status='APPROVED' AND dr.ModelId IS NOT NULL AND dr.ReportDate BETWEEN @Start AND @End AND UPPER(cat.CategoryName) IN ('FATP','SMT')
            GROUP BY UPPER(cat.CategoryName);
            """;
        var performance=new Dictionary<string,object>();await using(var c=new SqlCommand(categorySql,connection)){Add(c,"@UserId",SqlDbType.Int,userId);Add(c,"@PlantId",SqlDbType.Int,plantId);Add(c,"@Start",SqlDbType.Date,start);Add(c,"@End",SqlDbType.Date,end);await using var r=await c.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))performance[r.GetString(0)]=new{reports=r.GetInt32(1),target=Convert.ToDecimal(r.GetValue(2)),actual=Convert.ToDecimal(r.GetValue(3))};}
        const string trendSql="""
            SELECT dr.ReportDate,COALESCE(SUM(dr.ProductionPlan),0),COALESCE(SUM(dr.ProductionActual),0),COUNT(1)
            FROM DailyReport dr INNER JOIN UserCustomer uc ON uc.CustomerId=dr.CustomerId AND uc.UserId=@UserId
            INNER JOIN PlantCustomer pc ON pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId
            WHERE dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.Status='APPROVED' AND dr.ModelId IS NOT NULL AND dr.ReportDate BETWEEN @Start AND @End
            GROUP BY dr.ReportDate ORDER BY dr.ReportDate;
            """;
        var trend=new List<object>();await using(var c=new SqlCommand(trendSql,connection)){Add(c,"@UserId",SqlDbType.Int,userId);Add(c,"@PlantId",SqlDbType.Int,plantId);Add(c,"@Start",SqlDbType.Date,start);Add(c,"@End",SqlDbType.Date,end);await using var r=await c.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))trend.Add(new{date=r.GetDateTime(0).ToString("yyyy-MM-dd"),target=Convert.ToDecimal(r.GetValue(1)),actual=Convert.ToDecimal(r.GetValue(2)),reports=r.GetInt32(3)});}
        return Ok(new{fromDate=start.ToString("yyyy-MM-dd"),toDate=end.ToString("yyyy-MM-dd"),summary,fatp=performance.GetValueOrDefault("FATP",new{reports=0,target=0m,actual=0m}),smt=performance.GetValueOrDefault("SMT",new{reports=0,target=0m,actual=0m}),trend});
    }

    [HttpGet("daily-reports")]
    public async Task<IActionResult> DailyReports([FromQuery] DateTime? fromDate,[FromQuery] DateTime? toDate,[FromQuery] int? categoryId,[FromQuery] int? lineId,[FromQuery] string? status,[FromQuery] string? search,[FromQuery] string? sort="date",[FromQuery] string? direction="desc",[FromQuery] int page=1,[FromQuery] int pageSize=25,CancellationToken cancellationToken=default)
    {
        if(!TryScope(out var userId,out var plantId))return Unauthorized();page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,10,100);var end=(toDate??DateTime.Today).Date;var start=(fromDate??end.AddDays(-29)).Date;if(start>end)return BadRequest(new{message="Start date must be on or before end date."});
        var sortColumn=sort?.ToLowerInvariant() switch{"customer"=>"c.CustomerName","line"=>"pl.LineName","status"=>"dr.Status","production"=>"dr.ProductionActual",_=>"dr.ReportDate"};var sortDirection=direction?.Equals("asc",StringComparison.OrdinalIgnoreCase)==true?"ASC":"DESC";
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        const string filter="dr.PlantId=@PlantId AND dr.IsActive=1 AND dr.ModelId IS NOT NULL AND dr.ReportDate BETWEEN @Start AND @End AND (@CategoryId IS NULL OR dr.CategoryId=@CategoryId) AND (@LineId IS NULL OR dr.LineId=@LineId) AND (@Status IS NULL OR dr.Status=@Status) AND (@Search IS NULL OR c.CustomerName LIKE @Search OR cat.CategoryName LIKE @Search OR pl.LineName LIKE @Search OR m.ModelName LIKE @Search OR submitter.FullName LIKE @Search) AND EXISTS(SELECT 1 FROM UserCustomer uc WHERE uc.UserId=@UserId AND uc.CustomerId=dr.CustomerId) AND EXISTS(SELECT 1 FROM PlantCustomer pc WHERE pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId)";
        var baseSql=$"FROM DailyReport dr INNER JOIN Customer c ON c.CustomerId=dr.CustomerId INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId LEFT JOIN Model m ON m.ModelId=dr.ModelId INNER JOIN [User] submitter ON submitter.UserId=dr.CreatedBy LEFT JOIN ApprovalQueue aq ON aq.DailyReportId=dr.DailyReportId WHERE {filter}";
        int total;await using(var count=new SqlCommand("SELECT COUNT(1) "+baseSql,connection)){Bind(count);total=Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken));}
        var sql=$"SELECT dr.DailyReportId,dr.SubmissionBatchId,dr.ReportDate,c.CustomerName,cat.CategoryName,pl.LineName,pl.SAPLocation,m.ModelName,submitter.FullName,dr.Status,aq.Status,dr.SubmittedAt,dr.UpdatedAt,dr.ProductionPlan,dr.ProductionActual,dr.UPHTarget,dr.UPHActual,dr.CPHTarget,dr.CPHActual,dr.UPPHTarget,dr.UPPHActual "+baseSql+$" ORDER BY {sortColumn} {sortDirection},dr.DailyReportId DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        var rows=new List<object>();await using(var cmd=new SqlCommand(sql,connection)){Bind(cmd);Add(cmd,"@Offset",SqlDbType.Int,(page-1)*pageSize);Add(cmd,"@PageSize",SqlDbType.Int,pageSize);await using var r=await cmd.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))rows.Add(new{dailyReportId=r.GetInt32(0),submissionBatchId=r.IsDBNull(1)?(Guid?)null:r.GetGuid(1),reportDate=r.GetDateTime(2).ToString("yyyy-MM-dd"),customerName=r.GetString(3),categoryName=r.GetString(4),lineName=r.GetString(5),sapLocation=r.IsDBNull(6)?null:r.GetString(6),modelName=r.IsDBNull(7)?null:r.GetString(7),submittedBy=r.GetString(8),status=r.GetString(9),approvalStatus=r.IsDBNull(10)?null:r.GetString(10),submittedAt=r.IsDBNull(11)?(DateTime?)null:r.GetDateTime(11),updatedAt=r.IsDBNull(12)?(DateTime?)null:r.GetDateTime(12),productionPlan=GetDecimal(r,13),productionActual=GetDecimal(r,14),uphTarget=GetDecimal(r,15),uphActual=GetDecimal(r,16),cphTarget=GetDecimal(r,17),cphActual=GetDecimal(r,18),upphTarget=GetDecimal(r,19),upphActual=GetDecimal(r,20)});}
        return Ok(new{rows,total,page,pageSize,totalPages=(int)Math.Ceiling(total/(double)pageSize),fromDate=start.ToString("yyyy-MM-dd"),toDate=end.ToString("yyyy-MM-dd")});
        void Bind(SqlCommand command){Add(command,"@PlantId",SqlDbType.Int,plantId);Add(command,"@UserId",SqlDbType.Int,userId);Add(command,"@Start",SqlDbType.Date,start);Add(command,"@End",SqlDbType.Date,end);Add(command,"@CategoryId",SqlDbType.Int,categoryId);Add(command,"@LineId",SqlDbType.Int,lineId);Add(command,"@Status",SqlDbType.VarChar,string.IsNullOrWhiteSpace(status)?null:status.Trim().ToUpperInvariant());Add(command,"@Search",SqlDbType.NVarChar,string.IsNullOrWhiteSpace(search)?null:"%"+search.Trim()+"%");}
    }

    [HttpGet("activity")]
    public async Task<IActionResult> Activity([FromQuery] DateTime? fromDate,[FromQuery] DateTime? toDate,[FromQuery] string? action,[FromQuery] string? search,[FromQuery] int page=1,[FromQuery] int pageSize=25,CancellationToken cancellationToken=default)
    {
        if(!TryScope(out var userId,out var plantId))return Unauthorized();page=Math.Max(1,page);pageSize=Math.Clamp(pageSize,10,100);var end=(toDate??DateTime.Today).Date;var start=(fromDate??end.AddDays(-89)).Date;
        await using var connection=await connectionFactory.OpenAsync(cancellationToken);
        const string countSql="""
            SELECT COUNT(1) FROM ReportApproval ra INNER JOIN DailyReport dr ON dr.DailyReportId=ra.DailyReportId
            INNER JOIN [User] actor ON actor.UserId=ra.ActionBy INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId LEFT JOIN Model m ON m.ModelId=dr.ModelId
            WHERE dr.PlantId=@PlantId AND ra.ActionAt>=@Start AND ra.ActionAt<DATEADD(day,1,@End) AND (@Action IS NULL OR ra.ActionType=@Action)
              AND (@Search IS NULL OR actor.FullName LIKE @Search OR c.CustomerName LIKE @Search OR cat.CategoryName LIKE @Search OR pl.LineName LIKE @Search OR m.ModelName LIKE @Search)
              AND EXISTS(SELECT 1 FROM UserCustomer uc WHERE uc.UserId=@UserId AND uc.CustomerId=dr.CustomerId)
              AND EXISTS(SELECT 1 FROM PlantCustomer pc WHERE pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId);
            """;
        await using var countCommand=new SqlCommand(countSql,connection);Bind(countCommand);var total=Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        const string sql="""
            SELECT ra.DailyReportId,ra.ActionType,ra.Comments,ra.ActionAt,actor.FullName,dr.ReportDate,c.CustomerName,cat.CategoryName,pl.LineName,m.ModelName
            FROM ReportApproval ra INNER JOIN DailyReport dr ON dr.DailyReportId=ra.DailyReportId
            INNER JOIN [User] actor ON actor.UserId=ra.ActionBy INNER JOIN Customer c ON c.CustomerId=dr.CustomerId
            INNER JOIN Category cat ON cat.CategoryId=dr.CategoryId INNER JOIN ProductionLine pl ON pl.LineId=dr.LineId LEFT JOIN Model m ON m.ModelId=dr.ModelId
            WHERE dr.PlantId=@PlantId AND ra.ActionAt>=@Start AND ra.ActionAt<DATEADD(day,1,@End) AND (@Action IS NULL OR ra.ActionType=@Action)
              AND (@Search IS NULL OR actor.FullName LIKE @Search OR c.CustomerName LIKE @Search OR cat.CategoryName LIKE @Search OR pl.LineName LIKE @Search OR m.ModelName LIKE @Search)
              AND EXISTS(SELECT 1 FROM UserCustomer uc WHERE uc.UserId=@UserId AND uc.CustomerId=dr.CustomerId)
              AND EXISTS(SELECT 1 FROM PlantCustomer pc WHERE pc.PlantId=dr.PlantId AND pc.CustomerId=dr.CustomerId)
            ORDER BY ra.ActionAt DESC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """;
        await using var cmd=new SqlCommand(sql,connection);Bind(cmd);Add(cmd,"@Offset",SqlDbType.Int,(page-1)*pageSize);Add(cmd,"@PageSize",SqlDbType.Int,pageSize);
        var rows=new List<object>();await using var r=await cmd.ExecuteReaderAsync(cancellationToken);while(await r.ReadAsync(cancellationToken))rows.Add(new{dailyReportId=r.GetInt32(0),action=r.GetString(1),comment=r.IsDBNull(2)?null:r.GetString(2),at=r.GetDateTime(3),actor=r.GetString(4),reportDate=r.GetDateTime(5).ToString("yyyy-MM-dd"),customerName=r.GetString(6),categoryName=r.GetString(7),lineName=r.GetString(8),modelName=r.IsDBNull(9)?null:r.GetString(9)});return Ok(new{rows,total,page,pageSize,totalPages=(int)Math.Ceiling(total/(double)pageSize)});
        void Bind(SqlCommand command){Add(command,"@PlantId",SqlDbType.Int,plantId);Add(command,"@UserId",SqlDbType.Int,userId);Add(command,"@Start",SqlDbType.Date,start);Add(command,"@End",SqlDbType.Date,end);Add(command,"@Action",SqlDbType.VarChar,string.IsNullOrWhiteSpace(action)?null:action.Trim().ToUpperInvariant());Add(command,"@Search",SqlDbType.NVarChar,string.IsNullOrWhiteSpace(search)?null:"%"+search.Trim()+"%");}
    }

    private bool TryScope(out int userId,out int plantId){userId=int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var u)?u:0;plantId=int.TryParse(User.FindFirstValue("plantId"),out var p)?p:0;return userId>0&&plantId>0;}
    private static void Add(SqlCommand c,string n,SqlDbType t,object? v){var p=c.Parameters.Add(n,t);p.Value=v??DBNull.Value;}
    private static decimal? GetDecimal(SqlDataReader r,int i)=>r.IsDBNull(i)?null:Convert.ToDecimal(r.GetValue(i));
}
