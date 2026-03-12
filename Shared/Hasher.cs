using System.Security.Cryptography;
using System.Text;

namespace GraphRagCli.Shared;

public static class Hasher
{
    public static string Hash(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Computes a name-independent hash of a type/method body.
    /// Strips the declaration line (everything before the first '{'),
    /// keeping only the implementation. This survives renames and moves.
    /// </summary>
    public static string HashCodeBody(string sourceText)
    {
        var firstBrace = sourceText.IndexOf('{');
        if (firstBrace < 0) return Hash(sourceText);
        var body = sourceText[(firstBrace + 1)..].TrimEnd().TrimEnd('}');
        return Hash(body.Trim());
    }
}

