using MacSign.Signing.Credentials;
using MacSign.Signing.Engine;
using MacSign.Signing.Formats;

namespace MacSign.Signing;

/// <summary>
/// Public entry point. Signs one or more Windows artifacts in place. Phase 1
/// implements PE (<c>.exe</c>/<c>.dll</c>/<c>.sys</c>) with a local PFX certificate.
/// </summary>
public sealed class AuthenticodeSigner
{
    private AuthenticodeSigner() { }

    /// <summary>
    /// Validate the options for the chosen mode. Returns null + an actionable
    /// <paramref name="error"/> if they're not usable.
    /// </summary>
    public static AuthenticodeSigner? TryCreate(SigningOptions options, out string? error)
    {
        if (options.CertMode != CertMode.Pfx)
        {
            error = $"MacSign Phase 1 supports only PFX signing (got {options.CertMode}).";
            return null;
        }
        if (string.IsNullOrWhiteSpace(options.PfxPath) || !File.Exists(options.PfxPath))
        {
            error = $"PFX file not found: {options.PfxPath ?? "(none)"}";
            return null;
        }

        error = null;
        return new AuthenticodeSigner();
    }

    /// <summary>
    /// Sign <paramref name="setupFile"/> (and, if <see cref="SigningOptions.SignAllSignableFiles"/>,
    /// every signable file under <paramref name="sourceFolder"/>). Already-signed files are skipped.
    /// </summary>
    public async Task<SignResult> SignAsync(
        string sourceFolder, string setupFile, SigningOptions options,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        bool setupSignable = SignableExtensions.IsSignable(setupFile);
        if (!options.SignAllSignableFiles && !setupSignable)
            return SignResult.Fail(
                $"'{Path.GetFileName(setupFile)}' is not an Authenticode-signable type.");

        var targets = CollectTargets(sourceFolder, setupFile, options);
        if (targets.Count == 0)
            return SignResult.Fail("No Authenticode-signable files were found to sign.");

        ICredentialSigner credential;
        try
        {
            credential = new PfxCredentialSigner(options.PfxPath!, options.Secret);
        }
        catch (Exception ex)
        {
            return SignResult.Fail($"Could not load the PFX: {ex.Message}");
        }

        try
        {
            foreach (var file in targets)
            {
                ct.ThrowIfCancellationRequested();

                var format = FormatRegistry.For(file);
                if (format is null)
                {
                    // A signable-in-principle file we don't implement yet (e.g. .msi).
                    var ext = Path.GetExtension(file);
                    if (options.SignAllSignableFiles)
                    {
                        log?.Report($"Skipping {Path.GetFileName(file)} — {ext} signing isn't implemented yet (PE and PowerShell only).");
                        continue;
                    }
                    return SignResult.Fail($"{ext} signing isn't implemented yet (PE and PowerShell only).");
                }

                byte[] bytes = await File.ReadAllBytesAsync(file, ct);

                if (ExistingSignatureGate.IsSigned(format, bytes, out var subject))
                {
                    log?.Report($"Skipping {Path.GetFileName(file)} — already signed ({subject ?? "unknown signer"}).");
                    continue;
                }

                log?.Report($"Signing {Path.GetFileName(file)}…");
                byte[] signed;
                try
                {
                    signed = await SignEngine.SignFileBytesAsync(format, credential, options, bytes, ct);
                }
                catch (Exception ex)
                {
                    return SignResult.Fail($"Failed to sign {Path.GetFileName(file)}: {ex.Message}");
                }

                await WriteAtomicAsync(file, signed, ct);
                log?.Report($"Signed {Path.GetFileName(file)}.");
            }

            return SignResult.Ok();
        }
        finally
        {
            credential.Dispose();
        }
    }

    private static List<string> CollectTargets(string sourceFolder, string setupFile, SigningOptions options)
    {
        if (!options.SignAllSignableFiles)
            return [setupFile];

        return Directory
            .EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories)
            .Where(SignableExtensions.IsSignable)
            .ToList();
    }

    /// <summary>Write to a sibling temp file, then atomically rename over the original.</summary>
    private static async Task WriteAtomicAsync(string file, byte[] content, CancellationToken ct)
    {
        var temp = file + ".signtmp";
        try
        {
            await File.WriteAllBytesAsync(temp, content, ct);
            File.Move(temp, file, overwrite: true); // same volume → atomic rename
        }
        finally
        {
            if (File.Exists(temp))
                try { File.Delete(temp); } catch { /* best effort */ }
        }
    }
}
