using System.Security.Cryptography;
using System.Text;

namespace RepoCompare.Utils;

/// <summary>
/// Normalizes file content to eliminate noise from encoding, BOM, line endings,
/// and trailing whitespace differences introduced by manual file copies.
/// </summary>
public static class ContentNormalizer
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    /// <summary>
    /// Reads a file, strips BOM, normalizes line endings and trailing whitespace,
    /// and returns the cleaned content as a string.
    /// </summary>
    public static string ReadAndNormalize(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        string content;

        // Detect and strip BOM, decode accordingly
        if (bytes.Length >= 3 && bytes[0] == Utf8Bom[0] && bytes[1] == Utf8Bom[1] && bytes[2] == Utf8Bom[2])
        {
            content = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else if (bytes.Length >= 2 && bytes[0] == Utf16LeBom[0] && bytes[1] == Utf16LeBom[1])
        {
            content = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == Utf16BeBom[0] && bytes[1] == Utf16BeBom[1])
        {
            content = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else
        {
            content = Encoding.UTF8.GetString(bytes);
        }

        return Normalize(content);
    }

    /// <summary>
    /// Normalizes a string: CRLF→LF, trim trailing whitespace per line,
    /// collapse trailing newlines.
    /// </summary>
    public static string Normalize(string content)
    {
        // 1. Normalize line endings to LF
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2. Trim trailing whitespace from each line
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd(' ', '\t');
        }

        // 3. Rejoin and trim trailing blank lines
        content = string.Join("\n", lines).TrimEnd('\n');

        return content;
    }

    /// <summary>
    /// Computes SHA-256 hash of normalized content for fast equality check.
    /// </summary>
    public static string ComputeHash(string normalizedContent)
    {
        var bytes = Encoding.UTF8.GetBytes(normalizedContent);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Detects if a file is binary by scanning for null bytes in the first 8KB.
    /// </summary>
    public static bool IsBinaryFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[Math.Min(stream.Length, 8192)];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        for (int i = 0; i < bytesRead; i++)
        {
            if (buffer[i] == 0) return true;
        }
        return false;
    }

    /// <summary>
    /// Splits normalized content into lines for diffing.
    /// </summary>
    public static string[] SplitLines(string normalizedContent)
    {
        if (string.IsNullOrEmpty(normalizedContent))
            return [];

        return normalizedContent.Split('\n');
    }
}
