using AuthHub.Api.Models.Auth;
using AuthHub.Domain.Entities;
using AuthHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;

using Microsoft.IdentityModel.Tokens;


namespace AuthHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public AuthController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    AppDbContext db,
    IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _config = config;
    }

    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest req)
    {
        var existing = await _userManager.FindByEmailAsync(req.Email);
        if (existing != null)
            return BadRequest("Email is already in use.");

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            FullName = req.FullName
        };

        var result = await _userManager.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        var emailToken = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        Console.WriteLine($"EMAIL CONFIRM TOKEN for {user.Email}: {emailToken}");




        await _userManager.AddToRoleAsync(user, "User");
        return Ok("Registered.");
        

    }

    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null)
            return Unauthorized("Invalid credentials.");

        if (user.IsDisabled)
            return Unauthorized("User is disabled.");

        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            req.Password,
            lockoutOnFailure: true
        );

        if (result.IsLockedOut)
            return Unauthorized("Account locked. Try later.");

        if (!result.Succeeded)
            return Unauthorized("Invalid credentials.");

        if (!user.EmailConfirmed)
            return Unauthorized("Email not confirmed.");

        var tokens = await GenerateTokensAsync(user);
        return Ok(tokens);
    }




    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var stored = await _db.RefreshTokens
    .FirstOrDefaultAsync(x =>
        x.Token == req.RefreshToken &&
        x.RevokedAtUtc == null &&
        x.ExpiresAtUtc > DateTime.UtcNow);



        if (stored == null)
            return Unauthorized("Invalid refresh token.");

        var user = await _userManager.FindByIdAsync(stored.UserId);
        if (user == null || user.IsDisabled)
            return Unauthorized("User not valid.");


        stored.RevokedAtUtc = DateTime.UtcNow;
        stored.RevokedReason = "Rotated";


        var tokens = await GenerateTokensAsync(user);
        return Ok(tokens);
    }



    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(x => x.Token == req.RefreshToken);

        if (token == null)
            return Ok(); // zaten logout

        token.RevokedAtUtc = DateTime.UtcNow;
        token.RevokedReason = "User logout";

        await _db.SaveChangesAsync();

        return Ok("Logged out.");
    }





    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        await RevokeAllUserTokens(userId, "User logout-all");
        return Ok("All sessions revoked.");
    }






    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null) return Unauthorized();

        var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        
        await RevokeAllUserTokens(user.Id, "Password changed");

        return Ok("Password changed.");
    }






    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);

        
        if (user == null) return Ok("If the email exists, reset instructions were sent.");

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);

        
        Console.WriteLine($"RESET TOKEN for {req.Email}: {token}");

        return Ok("If the email exists, reset instructions were sent.");
    }











    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null) return Ok("If the email exists, password was reset.");

        var result = await _userManager.ResetPasswordAsync(user, req.Token, req.NewPassword);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        
        await RevokeAllUserTokens(user.Id, "Password reset");

        return Ok("Password reset.");
    }




    [HttpPost("confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest req)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null) return BadRequest("Invalid.");

        var result = await _userManager.ConfirmEmailAsync(user, req.Token);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        return Ok("Email confirmed.");
    }






    private string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private async Task<AuthResponse> GenerateTokensAsync(ApplicationUser user)
    {
        var jwt = _config.GetSection("Jwt");

        var key = jwt["Key"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException("JWT Key is missing or too short (min 32 chars). Check appsettings.json");

        var issuer = jwt["Issuer"];
        var audience = jwt["Audience"];

        var accessMinutes = int.TryParse(jwt["AccessTokenMinutes"], out var am) ? am : 15;
        var refreshDays = int.TryParse(jwt["RefreshTokenDays"], out var rd) ? rd : 7;

        
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? "")
        };

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var r in roles)
            claims.Add(new Claim(ClaimTypes.Role, r));





       
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(accessMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: creds
        );

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        
        var refreshToken = GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(refreshDays),
            RevokedAtUtc = null

        });

        await _db.SaveChangesAsync();



        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    private async Task RevokeAllUserTokens(string userId, string reason)
    {
        var tokens = await _db.RefreshTokens
            .Where(x => x.UserId == userId && x.IsActive)
            .ToListAsync();

        var now = DateTime.UtcNow;

        foreach (var t in tokens)
        {
            t.RevokedAtUtc = now;
            t.RevokedReason = reason;
        }

        await _db.SaveChangesAsync();
    }

}
