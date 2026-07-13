using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;
using Google.Cloud.Firestore;
using HundredPeaksTrackers.Api.Models;
using HundredPeaksTrackers.Api.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace HundredPeaksTrackers.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly FirestoreDb _db;
    private readonly GoogleAuthOptions _googleOptions;
    private readonly JwtOptions _jwtOptions;

    public AuthController(FirestoreDb db, IOptions<GoogleAuthOptions> googleOptions, IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _googleOptions = googleOptions.Value;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("google")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> GoogleLogin(GoogleLoginRequest request)
    {
        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_googleOptions.ClientId],
            });
        }
        catch (InvalidJwtException)
        {
            return Unauthorized();
        }

        var googleSub = payload.Subject;
        var userSnapshot = await _db.Collection("users").Document(googleSub).GetSnapshotAsync();

        // 單一使用者 app：firestoreUid 對應既有 Firebase Auth uid，需手動在 Firestore 建立（見設計文件 7.1），不自動建立新使用者
        if (!userSnapshot.Exists)
        {
            return Forbid();
        }

        var token = GenerateJwt(googleSub, payload.Email, payload.Name, payload.Picture);
        return Ok(new AuthResponse { Token = token });
    }

    private string GenerateJwt(string googleSub, string email, string name = "", string picture = "")
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, googleSub),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(JwtRegisteredClaimNames.Name, name ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Picture, picture ?? string.Empty),
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwtOptions.Issuer,
            Audience = _jwtOptions.Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes),
            SigningCredentials = credentials,
        });

        return token;
    }
}
