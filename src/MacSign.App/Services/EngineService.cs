using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MacSign.Signing;
using MacSign.Signing.Verification;

namespace MacSign.App.Services;

/// <summary>Outcome of a signature-removal attempt. Removed=false with a null Error
/// means the file simply wasn't signed; a non-null Error is an IO/format failure.</summary>
public sealed record RemoveOutcome(bool Removed, string? Error);

/// <summary>
/// Thin façade over the MacSign.Signing engine — the one place the GUI touches
/// the engine. Keeps view-models free of engine wiring.
/// </summary>
public class EngineService
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

    public virtual VerifyReport Verify(string filePath) => SignatureVerifier.Verify(filePath);

    /// <summary>Strip a file's Authenticode signature in place. No-throw (like Verify):
    /// IO/format failures come back as Error rather than an exception.</summary>
    public virtual RemoveOutcome Remove(string filePath)
    {
        try { return new RemoveOutcome(SignatureRemover.Remove(filePath), null); }
        catch (Exception ex) { return new RemoveOutcome(false, ex.Message); }
    }
}
