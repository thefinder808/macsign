namespace MacSign.Signing;

/// <summary>Outcome of a signing run.</summary>
/// <param name="Success">True if every targeted file is now signed (or was already signed).</param>
/// <param name="Error">A human-readable message when not successful.</param>
public sealed record SignResult(bool Success, string? Error)
{
    /// <summary>
    /// For a cloud-backed credential, the account that authenticated to obtain it — e.g.
    /// <c>user@contoso.com (tenant 1234…)</c>. Null for a local key, whose identity is the
    /// certificate itself, and null when it couldn't be determined.
    /// <para>
    /// Display only. It is read from an unvalidated token (see the Azure backend's
    /// <c>JwtIdentity</c>) and must never gate a decision — it exists so a successful run can
    /// say <i>who</i> signed, which previously only a failure ever reported.
    /// </para>
    /// </summary>
    public string? AuthenticatedAs { get; init; }

    public static SignResult Ok() => new(true, null);
    public static SignResult Fail(string error) => new(false, error);
}
