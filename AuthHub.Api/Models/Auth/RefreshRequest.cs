namespace AuthHub.Api.Models.Auth;

public class RefreshRequest
{
    public string RefreshToken { get; set; } = default!;
}
