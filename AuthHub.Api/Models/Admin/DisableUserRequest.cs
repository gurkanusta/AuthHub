namespace AuthHub.Api.Models.Admin;

public class DisableUserRequest
{
    public string UserId { get; set; } = default!;
    public bool IsDisabled { get; set; }
}
