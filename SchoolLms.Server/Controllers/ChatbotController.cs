using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// "Chatbot" menu — the Instagram/Telegram AI sales agent that runs next to LMS (`chatbot/`,
/// docs/modules/chatbot.md). LMS does not share its own JWT key with it: this endpoint hands the
/// panel a one-time, 2-minute token signed with <c>Chatbot:SsoKey</c>, which the chatbot exchanges
/// for its own session (<c>POST /chatbot/api/auth/sso</c>).
///
/// <para>
/// A GET, so <c>GatedRead</c> lets <c>chatbot:view</c> through as well: full key (and admin /
/// superadmin) → chatbot <c>admin</c>; view only → <c>operator</c> (inbox and leads, no settings).
/// Teachers and parents never pass <see cref="AdminPermAttribute"/>.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm(PermissionKey, GatedRead = true)]
[Route("api/admin/chatbot")]
public class ChatbotController(IConfiguration config) : ControllerBase
{
    /// <summary>Staff permission key of the menu (constants.ts `adminPermissions`).</summary>
    public const string PermissionKey = "chatbot";

    /// <summary>Lifetime of the hand-over token — long enough to load the iframe, no longer.</summary>
    public static readonly TimeSpan SsoLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Signed one-time token for the embedded panel; 503 when the chatbot is not configured.</summary>
    [HttpGet("sso")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<ChatbotSsoDto> Sso()
    {
        var key = config["Chatbot:SsoKey"] ?? "";
        if (key.Length < 32)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Chatbot ulanmagan" });

        var uid = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var full = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin)
            || User.Claims.Any(c => c.Type == AdminPermAttribute.ClaimType && c.Value == PermissionKey);

        var token = new JwtSecurityToken(
            issuer: "SchoolLms",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, uid),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("name", User.FindFirst(ClaimTypes.Name)?.Value ?? ""),
                new Claim("role", full ? "admin" : "operator"),
                new Claim("purpose", "lms-sso"),
            ],
            expires: DateTime.UtcNow.Add(SsoLifetime),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256));

        return new ChatbotSsoDto(new JwtSecurityTokenHandler().WriteToken(token));
    }
}

/// <summary>The one-time token the panel posts to the chatbot.</summary>
public record ChatbotSsoDto(string Token);
