using AuthHub.Api.Models.Admin;
using AuthHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AuthHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet("users")]
    public IActionResult Users()
    {
        var users = _userManager.Users
            .Select(u => new { u.Id, u.Email, u.UserName, u.EmailConfirmed, u.IsDisabled })
            .ToList();

        return Ok(users);
    }

    [HttpPost("assign-role")]
    public async Task<IActionResult> AssignRole(AssignRoleRequest req)
    {
        var user = await _userManager.FindByIdAsync(req.UserId);
        if (user == null) return NotFound("User not found.");

        var result = await _userManager.AddToRoleAsync(user, req.Role);
        if (!result.Succeeded) return BadRequest(result.Errors.Select(e => e.Description));

        return Ok("Role assigned.");
    }

    [HttpPost("disable-user")]
    public async Task<IActionResult> DisableUser(DisableUserRequest req)
    {
        var user = await _userManager.FindByIdAsync(req.UserId);
        if (user == null) return NotFound("User not found.");

        user.IsDisabled = req.IsDisabled;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest(result.Errors.Select(e => e.Description));

        return Ok("User updated.");
    }
}
