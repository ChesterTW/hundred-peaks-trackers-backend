using System.ComponentModel.DataAnnotations;

namespace HundredPeaksTrackers.Api.Models;

public class GoogleLoginRequest
{
    [Required]
    public string IdToken { get; set; } = default!;
}

public class AuthResponse
{
    public string Token { get; set; } = default!;
}
