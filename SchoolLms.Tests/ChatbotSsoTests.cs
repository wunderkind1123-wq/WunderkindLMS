using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;
using SchoolLms.Domain;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  "Chatbot" menu — the one-time token LMS hands the embedded chatbot panel
//  (docs/modules/chatbot.md §3): who gets one, with which chatbot role, and that it is
//  signed with Chatbot:SsoKey only (never the LMS JWT key).
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class ChatbotSsoTests(ApiFixture fixture)
{
    private const string Url = "/api/admin/chatbot/sso";
    private const string Key = "chatbot-sso-test-key-0123456789-abcdefghij";

    private static readonly Lock WireLock = new();
    private static WebApplicationFactory<AuthController>? _withKey;

    private WebApplicationFactory<AuthController> WithKey()
    {
        lock (WireLock)
            return _withKey ??= fixture.Api.WithWebHostBuilder(b => b.UseSetting("Chatbot:SsoKey", Key));
    }

    private async Task<HttpClient> ClientAsync(string role, params string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(role, permissions: permissions);
        var client = WithKey().CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.Api.TokenFor(role, user.Id, user.FullName, user.Email));
        return client;
    }

    [Theory]
    [InlineData(Roles.Admin, null, "admin")]
    [InlineData(Roles.Staff, ChatbotController.PermissionKey, "admin")]
    [InlineData(Roles.Staff, ChatbotController.PermissionKey + Roles.ViewSuffix, "operator")]
    public async Task Token_carries_the_chatbot_role_and_is_signed_with_the_chatbot_key(
        string role, string? permission, string expected)
    {
        using var client = await ClientAsync(role, permission is null ? [] : [permission]);
        var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");

        var token = (await response.Content.ReadFromJsonAsync<ChatbotSsoDto>())!.Token;
        new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = "SchoolLms",
            ValidateAudience = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        }, out var validated);

        var jwt = (JwtSecurityToken)validated;
        Assert.Equal(expected, jwt.Claims.Single(c => c.Type == "role").Value);
        Assert.Equal("lms-sso", jwt.Claims.Single(c => c.Type == "purpose").Value);
        Assert.False(string.IsNullOrEmpty(jwt.Id));
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.Add(ChatbotController.SsoLifetime).AddSeconds(5));
    }

    [Fact]
    public async Task Staff_without_the_key_teachers_and_anonymous_get_nothing()
    {
        using var staff = await ClientAsync(Roles.Staff, "leads");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Url)).StatusCode);

        using var teacher = await ClientAsync(Roles.Teacher, ChatbotController.PermissionKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Url)).StatusCode);

        using var anonymous = WithKey().CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task Without_a_key_the_menu_answers_503()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await admin.GetAsync(Url)).StatusCode);
    }
}
