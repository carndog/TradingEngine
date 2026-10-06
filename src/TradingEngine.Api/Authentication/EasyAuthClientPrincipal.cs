using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace TradingEngine.Api.Authentication;

internal static class EasyAuthClientPrincipal
{
    internal const string HeaderName = "X-MS-CLIENT-PRINCIPAL";
    internal const string AuthenticationType = "EasyAuth";
    internal const string ObjectIdentifierClaimType =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";
    internal const string ShortObjectIdentifierClaimType = "oid";
    internal const string ShortSubjectClaimType = "sub";

    internal static ClaimsPrincipal? Decode(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(headerValue.Trim());
        }
        catch (FormatException)
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(payload));
            return ToPrincipal(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? StableIdentifier(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        string? identifier = principal.FindFirst(ObjectIdentifierClaimType)?.Value
            ?? principal.FindFirst(ShortObjectIdentifierClaimType)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst(ShortSubjectClaimType)?.Value;

        return string.IsNullOrWhiteSpace(identifier) ? null : identifier;
    }

    private static ClaimsPrincipal? ToPrincipal(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || root.TryGetProperty("claims", out JsonElement claimsElement) is false
            || claimsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? nameType = ReadString(root, "name_typ");
        string? roleType = ReadString(root, "role_typ");
        ClaimsIdentity identity = new(
            AuthenticationType,
            string.IsNullOrWhiteSpace(nameType) ? ClaimsIdentity.DefaultNameClaimType : nameType,
            string.IsNullOrWhiteSpace(roleType) ? ClaimsIdentity.DefaultRoleClaimType : roleType);

        foreach (JsonElement claim in claimsElement.EnumerateArray())
        {
            string? type = ReadString(claim, "typ");
            string? value = ReadString(claim, "val");
            if (string.IsNullOrWhiteSpace(type) || value is null)
            {
                continue;
            }

            identity.AddClaim(new Claim(type, value));
        }

        return new ClaimsPrincipal(identity);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || element.TryGetProperty(propertyName, out JsonElement property) is false
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
