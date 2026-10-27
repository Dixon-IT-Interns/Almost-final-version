using System.Security.Claims;
using Dixon.CommandCenter.API.Data;
using Dixon.CommandCenter.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dixon.CommandCenter.API.Controllers;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin")]
public sealed class ViewerAccessAdminController(
    ViewerAccessRepository viewerAccess) : ControllerBase
{
    [HttpGet("users/{id:int}/viewer-access")]
    public async Task<IActionResult> Get(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await viewerAccess.GetConfigurationAsync(
                id,
                IsSuperAdmin ? null : CurrentPlantId,
                IsSuperAdmin,
                cancellationToken);

            return configuration is null ? NotFound() : Ok(configuration);
        }
        catch (ViewerAccessForbiddenException)
        {
            return Forbid();
        }
    }

    [HttpPut("users/{id:int}/viewer-access")]
    public async Task<IActionResult> Update(
        int id,
        ViewerAccessUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var administratorId = GetUserId();
        if (administratorId <= 0)
        {
            return Unauthorized();
        }

        try
        {
            var updated = await viewerAccess.SaveAsync(
                id,
                administratorId,
                CurrentPlantId,
                IsSuperAdmin,
                request,
                cancellationToken);

            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ViewerAccessForbiddenException)
        {
            return Forbid();
        }
        catch (ViewerAccessValidationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private bool IsSuperAdmin => User.IsInRole("SuperAdmin");

    private int CurrentPlantId =>
        int.TryParse(User.FindFirstValue("plantId"), out var plantId)
            ? plantId
            : 0;

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : 0;
}
