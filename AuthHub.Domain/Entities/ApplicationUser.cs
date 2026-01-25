using Microsoft.AspNetCore.Identity;

namespace AuthHub.Domain.Entities;

public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public bool IsDisabled { get; set; }=false;
}