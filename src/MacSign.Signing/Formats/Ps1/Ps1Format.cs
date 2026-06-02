using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MacSign.Signing.Cms;

namespace MacSign.Signing.Formats.Ps1;

/// <summary>
/// Authenticode signing for PowerShell scripts (<c>.ps1</c>). The digest is
/// <c>SHA-256(UTF-16LE(content))</c> with the signature block stripped and the
/// byte-order mark included iff the file had one (verified to match osslsigncode /
/// Windows). The signature is appended as a <c># SIG #</c> base64 comment block.
/// </summary>
internal sealed partial class Ps1Format : ISignatureFormat
{
    private const string Begin = "# SIG # Begin signature block";
    private const string End = "# SIG # End signature block";

    public bool CanHandle(string path) =>
        Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase);

    public byte[] ComputeDigest(byte[] fileBytes)
    {
        var script = Decode(fileBytes);
        string content = StripSignatureBlock(script.Text);

        byte[] utf16 = Encoding.Unicode.GetBytes(content); // UTF-16LE, no BOM
        byte[] toHash = script.HasBom ? [0xFF, 0xFE, .. utf16] : utf16;
        return SHA256.HashData(toHash);
    }

    public byte[] BuildSpcIndirectData(byte[] fileDigest) =>
        SpcEncoder.BuildScriptIndirectData(fileDigest);

    public byte[] Embed(byte[] fileBytes, byte[] pkcs7Der)
    {
        var script = Decode(fileBytes);

        var sb = new StringBuilder();
        sb.Append("\r\n").Append(Begin).Append("\r\n");
        string b64 = Convert.ToBase64String(pkcs7Der);
        for (int i = 0; i < b64.Length; i += 64)
            sb.Append("# ").Append(b64, i, Math.Min(64, b64.Length - i)).Append("\r\n");
        sb.Append(End).Append("\r\n");

        // The block is appended in the file's own encoding so the file stays valid.
        byte[] block = script.Encoding.GetBytes(sb.ToString());
        return [.. fileBytes, .. block];
    }

    public bool TryExtractSignature(byte[] fileBytes, out byte[] pkcs7Der)
    {
        pkcs7Der = [];
        var match = BlockRegex().Match(Decode(fileBytes).Text);
        if (!match.Success)
            return false;

        var sb = new StringBuilder();
        foreach (var line in match.Groups["body"].Value.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
                trimmed = trimmed[1..].Trim();
            sb.Append(trimmed);
        }

        try
        {
            pkcs7Der = Convert.FromBase64String(sb.ToString());
            return pkcs7Der.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public bool TryRemoveSignature(byte[] fileBytes, out byte[] unsignedBytes)
    {
        var script = Decode(fileBytes);
        string stripped = StripSignatureBlock(script.Text);
        if (stripped.Length == script.Text.Length)
        {
            unsignedBytes = fileBytes; // no signature block present
            return false;
        }

        byte[] bom = script.HasBom ? LeadingBom(fileBytes) : [];
        unsignedBytes = [.. bom, .. script.Encoding.GetBytes(stripped)];
        return true;
    }

    private static string StripSignatureBlock(string text)
    {
        var match = Regex.Match(text, @"\r?\n" + Regex.Escape(Begin));
        return match.Success ? text[..match.Index] : text;
    }

    private static byte[] LeadingBom(byte[] b) =>
        b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? b[..3]
        : b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF)) ? b[..2]
        : [];

    private readonly record struct Script(string Text, bool HasBom, Encoding Encoding);

    private static Script Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new(Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), true, Encoding.Unicode);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new(Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), true, Encoding.BigEndianUnicode);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3), true, new UTF8Encoding(false));
        return new(Encoding.UTF8.GetString(bytes), false, new UTF8Encoding(false));
    }

    [GeneratedRegex(@"# SIG # Begin signature block\r?\n(?<body>.*?)# SIG # End signature block", RegexOptions.Singleline)]
    private static partial Regex BlockRegex();
}
