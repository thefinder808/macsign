using System.Text.Json;

namespace MacSign.Signing.Azure;

/// <summary>
/// Reads the account out of an access token so MacSign can say <i>who</i> it was issued to —
/// on a rejection, on a successful run, and when asked outright ("who would sign?").
/// <para>
/// <b>Display only.</b> The token's signature is never checked here and nothing in this file may
/// ever gate a security decision — treat every value as attacker-controlled text that is only
/// ever concatenated into a human-readable message. The reason it exists is diagnostic: without
/// it, a 401/403 tells the user their identity lacks a role but not <i>which</i> identity, which
/// is exactly how someone can spend days granting roles to the wrong account.
/// </para>
/// <para>
/// Every path returns null instead of throwing. It runs while formatting an error — where an
/// exception would replace a useful diagnostic with a confusing crash — and also on the success
/// and pre-flight paths, where null simply means "couldn't read the account" and callers say so.
/// </para>
/// </summary>
internal static class JwtIdentity
{
    /// <summary>Claims that may name the principal, best first. A user token usually carries
    /// <c>preferred_username</c>; older ones carry <c>upn</c>/<c>unique_name</c>; a service
    /// principal carries <c>azp</c> or <c>appid</c> instead.</summary>
    private static readonly string[] IdentityClaims =
        ["preferred_username", "upn", "unique_name", "azp", "appid"];

    /// <summary>
    /// Renders something like <c>user@contoso.com (tenant 1234…)</c>, or the tenant alone when
    /// the token names no principal — a wrong-tenant token is worth reporting either way.
    /// Returns null if nothing useful can be read.
    /// </summary>
    internal static string? Describe(string? token)
    {
        var payload = ReadPayload(token);
        if (payload is null) return null;

        using var doc = payload;
        var who = IdentityClaims
            .Select(claim => Text(doc.RootElement, claim))
            .FirstOrDefault(v => v is not null);
        var tenant = Text(doc.RootElement, "tid");

        return (who, tenant) switch
        {
            (not null, not null) => $"{who} (tenant {tenant})",
            (not null, null) => who,
            (null, not null) => $"tenant {tenant}",
            _ => null,
        };
    }

    private static JsonDocument? ReadPayload(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        try
        {
            // header.payload.signature — we only ever look at the middle segment.
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            return JsonDocument.Parse(Base64Url(parts[1]));
        }
        catch
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static byte[] Base64Url(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String((s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s });
    }
}
