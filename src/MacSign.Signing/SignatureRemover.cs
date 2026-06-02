using MacSign.Signing.Engine;
using MacSign.Signing.Formats;

namespace MacSign.Signing;

/// <summary>Removes an Authenticode signature from a file in place.</summary>
public static class SignatureRemover
{
    /// <summary>
    /// Strip the signature from <paramref name="filePath"/>. Returns true if a signature was
    /// removed, or false (file untouched) if it wasn't signed.
    /// </summary>
    public static bool Remove(string filePath)
    {
        var format = FormatRegistry.For(filePath)
            ?? throw new NotSupportedException(
                $"{Path.GetExtension(filePath)} signature removal isn't supported (PE, PowerShell, and MSI only).");

        var bytes = File.ReadAllBytes(filePath);
        if (!format.TryRemoveSignature(bytes, out var unsigned))
            return false;

        AtomicFile.Write(filePath, unsigned);
        return true;
    }
}
