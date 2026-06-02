namespace MacSign.Signing.Msi;

/// <summary>
/// Entry point for the MSI backend. A consumer (CLI / app) calls <see cref="Register"/>
/// once at startup to enable signing/verifying <c>.msi</c> files in the core engine.
/// </summary>
public static class MsiBackend
{
    public static void Register() => FormatBackends.Msi = new MsiFormat();
}
