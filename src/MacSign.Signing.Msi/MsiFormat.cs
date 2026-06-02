using System.Security.Cryptography;
using System.Text;
using MacSign.Signing.Cms;
using MacSign.Signing.Formats;
using OpenMcdf;

namespace MacSign.Signing.Msi;

/// <summary>
/// Authenticode signing for Windows Installer (<c>.msi</c>) packages — OLE/CFBF
/// compound files. The digest walks the storage tree (children sorted by the
/// UTF-16LE bytes of their on-disk names), hashing each stream's content and each
/// storage's CLSID, skipping the two signature streams. The signature is stored in
/// the \u0005DigitalSignature stream. CFBF read/write is delegated to OpenMcdf.
/// </summary>
internal sealed class MsiFormat : ISignatureFormat
{
    // On-disk stream names carry a U+0005 control-character prefix.
    private const string DigitalSignature = "\u0005DigitalSignature";
    private const string MsiDigitalSignatureEx = "\u0005MsiDigitalSignatureEx";

    public bool CanHandle(string path) =>
        Path.GetExtension(path).Equals(".msi", StringComparison.OrdinalIgnoreCase);

    public byte[] ComputeDigest(byte[] fileBytes)
    {
        using var ms = new MemoryStream(fileBytes, writable: false);
        using var root = RootStorage.Open(ms);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // The extended "Ex" prehash, when present, is hashed before the tree.
        if (root.EnumerateEntries().Any(e => e.Type == EntryType.Stream && e.Name == MsiDigitalSignatureEx))
        {
            using var ex = root.OpenStream(MsiDigitalSignatureEx);
            hash.AppendData(ReadAll(ex));
        }

        HashStorage(root, hash);
        return hash.GetHashAndReset();
    }

    public byte[] BuildSpcIndirectData(byte[] fileDigest) =>
        SpcEncoder.BuildMsiIndirectData(fileDigest);

    public byte[] Embed(byte[] fileBytes, byte[] pkcs7Der)
    {
        using var ms = new MemoryStream();
        ms.Write(fileBytes, 0, fileBytes.Length);
        ms.Position = 0;

        using (var root = RootStorage.Open(ms, StorageModeFlags.Transacted | StorageModeFlags.LeaveOpen))
        {
            if (root.EnumerateEntries().Any(e => e.Type == EntryType.Stream && e.Name == DigitalSignature))
                root.Delete(DigitalSignature);

            using (var stream = root.CreateStream(DigitalSignature))
                stream.Write(pkcs7Der, 0, pkcs7Der.Length);

            root.Commit();
        }

        return ms.ToArray();
    }

    public bool TryExtractSignature(byte[] fileBytes, out byte[] pkcs7Der)
    {
        pkcs7Der = [];
        try
        {
            using var ms = new MemoryStream(fileBytes, writable: false);
            using var root = RootStorage.Open(ms);
            if (!root.EnumerateEntries().Any(e => e.Type == EntryType.Stream && e.Name == DigitalSignature))
                return false;

            using var stream = root.OpenStream(DigitalSignature);
            pkcs7Der = ReadAll(stream);
            return pkcs7Der.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void HashStorage(Storage storage, IncrementalHash hash)
    {
        var entries = storage.EnumerateEntries().ToList();
        entries.Sort((a, b) => CompareNameBytes(a.Name, b.Name));

        foreach (var entry in entries)
        {
            if (entry.Type == EntryType.Stream)
            {
                if (entry.Name == DigitalSignature || entry.Name == MsiDigitalSignatureEx)
                    continue;
                using var stream = storage.OpenStream(entry.Name);
                hash.AppendData(ReadAll(stream));
            }
            else // Storage
            {
                var child = storage.OpenStorage(entry.Name);
                HashStorage(child, hash);
            }
        }

        // After its children, each storage contributes its 16-byte CLSID.
        hash.AppendData(storage.CLSID.ToByteArray());
    }

    /// <summary>Order names by their raw UTF-16LE bytes (MSI's mangled on-disk names).</summary>
    private static int CompareNameBytes(string a, string b)
    {
        byte[] ba = Encoding.Unicode.GetBytes(a);
        byte[] bb = Encoding.Unicode.GetBytes(b);
        int n = Math.Min(ba.Length, bb.Length);
        for (int i = 0; i < n; i++)
        {
            int diff = ba[i] - bb[i];
            if (diff != 0)
                return diff;
        }
        return ba.Length - bb.Length;
    }

    private static byte[] ReadAll(Stream stream)
    {
        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
