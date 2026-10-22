using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(SqlConnectionFactory connectionFactory, SmsOtpService smsOtpService, IConfiguration configuration) : ControllerBase
{
    private const int MaxPictureBytes = 1024 * 1024;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = """
            SELECT u.UserId, u.FullName, u.Email, u.PhoneNumber,
                   r.RoleName, p.PlantId, p.PlantName,
                   u.ProfilePictureData, u.ProfilePictureMimeType
            FROM dbo.[User] u
            INNER JOIN dbo.Role r ON r.RoleId = u.RoleId
            INNER JOIN dbo.Plant p ON p.PlantId = u.PlantId
            WHERE u.UserId = @UserId AND u.IsActive = 1;
            """;
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return NotFound();

        var picture = reader.IsDBNull(7) || reader.IsDBNull(8)
            ? null
            : $"data:{reader.GetString(8)};base64,{Convert.ToBase64String((byte[])reader.GetValue(7))}";
        return Ok(new
        {
            userId = reader.GetInt32(0),
            fullName = reader.GetString(1),
            email = reader.IsDBNull(2) ? null : reader.GetString(2),
            phoneNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
            roleName = reader.GetString(4),
            plantId = reader.GetInt32(5),
            plantName = reader.GetString(6),
            profilePicture = picture
        });
    }

    [HttpPost("phone/otp")]
    public async Task<IActionResult> SendPhoneOtp([FromBody] PhoneNumberRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        var phone = NormalizePhone(request.PhoneNumber);
        if (phone is null) return BadRequest(new { message = "Enter a valid phone number with country code, for example +91 98765 43210." });

        var key = GetOtpKey();
        if (key is null) return StatusCode(503, new { message = "Phone verification is not configured yet. Please contact your administrator." });

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string checkSql = "SELECT PhoneVerificationSentAt, PhoneVerificationWindowStartedAt, PhoneVerificationSendCount FROM dbo.[User] WHERE UserId=@UserId AND IsActive=1;";
        await using (var check = new SqlCommand(checkSql, connection))
        {
            Add(check, "@UserId", SqlDbType.Int, userId);
            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return NotFound();
            var sentAt = reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0);
            var windowAt = reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1);
            var count = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            var now = DateTime.UtcNow;
            if (sentAt.HasValue && now - sentAt.Value < TimeSpan.FromSeconds(60))
                return StatusCode(429, new { message = "Please wait a minute before requesting another code." });
            if (windowAt.HasValue && now - windowAt.Value < TimeSpan.FromHours(1) && count >= 5)
                return StatusCode(429, new { message = "You have reached the hourly code limit. Try again later." });
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        try { await smsOtpService.SendAsync(phone, $"Your Dixon Command Center verification code is {code}. It expires in 5 minutes.", cancellationToken); }
        catch (InvalidOperationException) { return StatusCode(503, new { message = "Phone verification is not configured yet. Please contact your administrator." }); }
        catch (SmsDeliveryException ex) { return StatusCode(502, new { message = SmsOtpService.GetUserMessage(ex.ProviderCode) }); }
        catch (HttpRequestException) { return StatusCode(502, new { message = "Could not connect to the SMS provider. Check internet access and try again." }); }

        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId}:{phone}:{code}"));
        const string saveSql = """
            UPDATE dbo.[User] SET PendingPhoneNumber=@Phone, PhoneVerificationCodeHash=@Hash,
                PhoneVerificationExpiresAt=@Expires, PhoneVerificationSentAt=@Sent,
                PhoneVerificationAttempts=0,
                PhoneVerificationWindowStartedAt=CASE WHEN PhoneVerificationWindowStartedAt IS NULL OR DATEADD(hour,1,PhoneVerificationWindowStartedAt)<=@Sent THEN @Sent ELSE PhoneVerificationWindowStartedAt END,
                PhoneVerificationSendCount=CASE WHEN PhoneVerificationWindowStartedAt IS NULL OR DATEADD(hour,1,PhoneVerificationWindowStartedAt)<=@Sent THEN 1 ELSE PhoneVerificationSendCount+1 END
            WHERE UserId=@UserId AND IsActive=1;
            """;
        await using var save = new SqlCommand(saveSql, connection);
        Add(save, "@UserId", SqlDbType.Int, userId); Add(save, "@Phone", SqlDbType.VarChar, phone);
        Add(save, "@Hash", SqlDbType.VarBinary, hash); Add(save, "@Expires", SqlDbType.DateTime2, DateTime.UtcNow.AddMinutes(5));
        Add(save, "@Sent", SqlDbType.DateTime2, DateTime.UtcNow);
        return await save.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : Ok(new { message = "Verification code sent." });
    }

    [HttpPost("phone/verify")]
    public async Task<IActionResult> VerifyPhone([FromBody] PhoneOtpRequest request, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        if (request.Code is null || request.Code.Length != 6 || !request.Code.All(char.IsDigit))
            return BadRequest(new { message = "Enter the 6-digit verification code." });
        var key = GetOtpKey();
        if (key is null) return StatusCode(503, new { message = "Phone verification is not configured yet. Please contact your administrator." });

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        const string readSql = "SELECT PendingPhoneNumber, PhoneVerificationCodeHash, PhoneVerificationExpiresAt, PhoneVerificationAttempts FROM dbo.[User] WITH (UPDLOCK, ROWLOCK) WHERE UserId=@UserId AND IsActive=1;";
        string? phone; byte[]? storedHash; DateTime? expires; int attempts;
        await using (var read = new SqlCommand(readSql, connection, transaction))
        {
            Add(read, "@UserId", SqlDbType.Int, userId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return NotFound();
            phone = reader.IsDBNull(0) ? null : reader.GetString(0);
            storedHash = reader.IsDBNull(1) ? null : (byte[])reader.GetValue(1);
            expires = reader.IsDBNull(2) ? null : reader.GetDateTime(2);
            attempts = reader.GetInt32(3);
        }
        if (phone is null || storedHash is null || expires is null || expires <= DateTime.UtcNow)
            return BadRequest(new { message = "No active code found. Request a new verification code." });
        if (attempts >= 5)
        {
            await ClearPendingPhone(connection, transaction, userId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return StatusCode(429, new { message = "Too many incorrect attempts. Request a new verification code." });
        }
        var suppliedHash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{userId}:{phone}:{request.Code}"));
        if (!CryptographicOperations.FixedTimeEquals(storedHash, suppliedHash))
        {
            await using var increment = new SqlCommand("UPDATE dbo.[User] SET PhoneVerificationAttempts=PhoneVerificationAttempts+1 WHERE UserId=@UserId;", connection, transaction);
            Add(increment, "@UserId", SqlDbType.Int, userId); await increment.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return BadRequest(new { message = "That code is incorrect. Please check it and try again." });
        }
        const string verifySql = "UPDATE dbo.[User] SET PhoneNumber=PendingPhoneNumber, PendingPhoneNumber=NULL, PhoneVerificationCodeHash=NULL, PhoneVerificationExpiresAt=NULL, PhoneVerificationSentAt=NULL, PhoneVerificationAttempts=0, PhoneVerificationSendCount=0, PhoneVerificationWindowStartedAt=NULL WHERE UserId=@UserId;";
        await using var verify = new SqlCommand(verifySql, connection, transaction);
        Add(verify, "@UserId", SqlDbType.Int, userId); await verify.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(new { message = "Phone number verified and saved.", phoneNumber = phone });
    }

    private byte[]? GetOtpKey() { var secret = configuration["Sms:OtpSigningKey"] ?? configuration["Jwt:Secret"]; return string.IsNullOrWhiteSpace(secret) ? null : SHA256.HashData(Encoding.UTF8.GetBytes(secret)); }
    private static string? NormalizePhone(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; var phone = new string(value.Where(c => char.IsDigit(c) || c == '+').ToArray()); return System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+[1-9]\d{7,14}$") ? phone : null; }
    private static async Task ClearPendingPhone(SqlConnection connection, SqlTransaction transaction, int userId, CancellationToken cancellationToken) { await using var command = new SqlCommand("UPDATE dbo.[User] SET PendingPhoneNumber=NULL, PhoneVerificationCodeHash=NULL, PhoneVerificationExpiresAt=NULL, PhoneVerificationSentAt=NULL, PhoneVerificationAttempts=0 WHERE UserId=@UserId;", connection, transaction); Add(command, "@UserId", SqlDbType.Int, userId); await command.ExecuteNonQueryAsync(cancellationToken); }

    [HttpPost("picture")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxPictureBytes + 64 * 1024)]
    public async Task<IActionResult> UploadPicture(
        [FromForm] IFormFile? picture,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        if (picture is null || picture.Length == 0)
            return BadRequest(new { message = "Choose a profile picture." });
        if (picture.Length > MaxPictureBytes)
            return BadRequest(new { message = "Profile pictures must be 1 MB or smaller." });

        await using var stream = new MemoryStream();
        await picture.CopyToAsync(stream, cancellationToken);
        var image = stream.ToArray();
        var mimeType = DetectImageMimeType(image);
        if (mimeType is null)
            return BadRequest(new { message = "Use a valid JPEG, PNG, or WebP image." });

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = "UPDATE dbo.[User] SET ProfilePictureData = @Picture, ProfilePictureMimeType = @MimeType WHERE UserId = @UserId AND IsActive = 1;";
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        Add(command, "@Picture", SqlDbType.VarBinary, image);
        Add(command, "@MimeType", SqlDbType.VarChar, mimeType);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : Ok(new { message = "Profile picture updated." });
    }

    [HttpDelete("picture")]
    public async Task<IActionResult> DeletePicture(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId <= 0) return Unauthorized();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        const string sql = "UPDATE dbo.[User] SET ProfilePictureData = NULL, ProfilePictureMimeType = NULL WHERE UserId = @UserId AND IsActive = 1;";
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : Ok(new { message = "Profile picture removed." });
    }

    private int GetUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : 0;

    private static string? DetectImageMimeType(byte[] image)
    {
        if (image.Length >= 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF) return "image/jpeg";
        if (image.Length >= 8 && image.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (image.Length >= 12 && image.AsSpan(0, 4).SequenceEqual("RIFF"u8) && image.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    private static void Add(SqlCommand command, string name, SqlDbType type, object? value)
    {
        var parameter = command.Parameters.Add(name, type);
        parameter.Value = value ?? DBNull.Value;
    }
}

public sealed record PhoneNumberRequest(string? PhoneNumber);
public sealed record PhoneOtpRequest(string? Code);
