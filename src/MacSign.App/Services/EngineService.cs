using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MacSign.Signing;
using MacSign.Signing.Verification;

namespace MacSign.App.Services;

/// <summary>
/// Thin façade over the MacSign.Signing engine — the one place the GUI touches
/// the engine. Keeps view-models free of engine wiring.
/// </summary>
public sealed class EngineService
{
    public bool IsSignable(string path) => SignableExtensions.IsSignable(path);

    /// <summary>Best-effort: is this file already Authenticode-signed?</summary>
    public bool IsAlreadySigned(string path)
    {
        try { return SignatureVerifier.Verify(path).IsSigned; }
        catch { return false; }
    }

    public AuthenticodeSigner? TryCreateSigner(SigningOptions options, out string? error)
        => AuthenticodeSigner.TryCreate(options, out error);

    /// <summary>
    /// Sign exactly one file. With <c>SignAllSignableFiles=false</c> the engine
    /// signs only <paramref name="filePath"/> (CollectTargets → [setupFile]).
    /// </summary>
    public Task<SignResult> SignOneAsync(AuthenticodeSigner signer, string filePath,
        SigningOptions options, IProgress<string>? log, CancellationToken ct)
    {
        var full = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(full) ?? ".";
        return signer.SignAsync(dir, full, options, log, ct);
    }

    public VerifyReport Verify(string filePath) => SignatureVerifier.Verify(filePath);
}
