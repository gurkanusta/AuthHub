namespace AuthHub.Api.Models.Auth;

public class ConfirmEmailRequest
{
    public string Email { get; set; } = default!;
    public string Token { get; set; } = default!;
}
