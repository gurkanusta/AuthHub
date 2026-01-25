namespace AuthHub.Api.Models.Admin;

public class AssignRoleRequest
{
    public string UserId { get; set; } = default!;
    public string Role { get; set; } = default!;
}
