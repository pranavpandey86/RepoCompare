using System.Text;

namespace RepoCompare.Utils;

/// <summary>
/// LCS-based diff engine that produces unified diff output.
/// Compares two sequences of lines and generates human-readable diffs.
/// </summary>
public static class DiffEngine
{
    public enum DiffLineType { Context, Added, Removed }

    public record DiffLine(DiffLineType Type, string Content);

    /// <summary>
    /// Computes the diff between source and target lines using the LCS algorithm.
    /// Returns a list of DiffLine entries (Context, Added, Removed).
    /// </summary>
    public static List<DiffLine> ComputeDiff(string[] sourceLines, string[] targetLines)
    {
        int n = sourceLines.Length;
        int m = targetLines.Length;

        // Safety: for very large files, fall back to a simple line-by-line comparison
        if ((long)n * m > 25_000_000)
        {
            return SimpleDiff(sourceLines, targetLines);
        }

        // ── Compute LCS table (bottom-up DP) ──
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
        {
            for (int j = m - 1; j >= 0; j--)
            {
                if (sourceLines[i] == targetLines[j])
                    lcs[i, j] = lcs[i + 1, j + 1] + 1;
                else
                    lcs[i, j] = Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        // ── Backtrack to produce the diff ──
        var result = new List<DiffLine>();
        int si = 0, ti = 0;
        while (si < n && ti < m)
        {
            if (sourceLines[si] == targetLines[ti])
            {
                result.Add(new DiffLine(DiffLineType.Context, sourceLines[si]));
                si++;
                ti++;
            }
            else if (lcs[si + 1, ti] >= lcs[si, ti + 1])
            {
                result.Add(new DiffLine(DiffLineType.Removed, sourceLines[si]));
                si++;
            }
            else
            {
                result.Add(new DiffLine(DiffLineType.Added, targetLines[ti]));
                ti++;
            }
        }
        while (si < n)
        {
            result.Add(new DiffLine(DiffLineType.Removed, sourceLines[si]));
            si++;
        }
        while (ti < m)
        {
            result.Add(new DiffLine(DiffLineType.Added, targetLines[ti]));
            ti++;
        }

        return result;
    }

    /// <summary>
    /// Fallback diff for very large files — simple line-by-line comparison.
    /// </summary>
    private static List<DiffLine> SimpleDiff(string[] sourceLines, string[] targetLines)
    {
        var result = new List<DiffLine>();
        var targetSet = new HashSet<string>(targetLines);
        var sourceSet = new HashSet<string>(sourceLines);

        // Show lines unique to source as removed, lines unique to target as added
        foreach (var line in sourceLines)
        {
            if (!targetSet.Contains(line))
                result.Add(new DiffLine(DiffLineType.Removed, line));
            else
                result.Add(new DiffLine(DiffLineType.Context, line));
        }
        foreach (var line in targetLines)
        {
            if (!sourceSet.Contains(line))
                result.Add(new DiffLine(DiffLineType.Added, line));
        }

        return result;
    }

    /// <summary>
    /// Formats a diff as a unified diff string with hunks, suitable for display
    /// in a Markdown report or terminal.
    /// </summary>
    /// <param name="diffLines">Output from ComputeDiff.</param>
    /// <param name="filePath">Relative file path for the header.</param>
    /// <param name="sourceLabel">Label for the source (e.g., "source").</param>
    /// <param name="targetLabel">Label for the target (e.g., "main").</param>
    /// <param name="contextLines">Number of context lines around changes.</param>
    public static string FormatUnifiedDiff(
        List<DiffLine> diffLines,
        string filePath,
        string sourceLabel = "a",
        string targetLabel = "b",
        int contextLines = 3)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- {sourceLabel}/{filePath}");
        sb.AppendLine($"+++ {targetLabel}/{filePath}");

        // Find hunks: contiguous changes with context
        var hunks = ExtractHunks(diffLines, contextLines);

        foreach (var hunk in hunks)
        {
            // Count source/target lines in this hunk
            int srcStart = hunk.SourceStart + 1; // 1-indexed
            int tgtStart = hunk.TargetStart + 1;
            int srcCount = hunk.Lines.Count(l => l.Type is DiffLineType.Context or DiffLineType.Removed);
            int tgtCount = hunk.Lines.Count(l => l.Type is DiffLineType.Context or DiffLineType.Added);

            sb.AppendLine($"@@ -{srcStart},{srcCount} +{tgtStart},{tgtCount} @@");

            foreach (var line in hunk.Lines)
            {
                char prefix = line.Type switch
                {
                    DiffLineType.Added => '+',
                    DiffLineType.Removed => '-',
                    _ => ' '
                };
                sb.AppendLine($"{prefix}{line.Content}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts hunks (groups of changes with surrounding context) from a flat diff.
    /// </summary>
    private static List<Hunk> ExtractHunks(List<DiffLine> diffLines, int contextLines)
    {
        var hunks = new List<Hunk>();

        // Find indices of all change lines
        var changeIndices = new List<int>();
        for (int i = 0; i < diffLines.Count; i++)
        {
            if (diffLines[i].Type != DiffLineType.Context)
                changeIndices.Add(i);
        }

        if (changeIndices.Count == 0)
            return hunks;

        // Group changes into hunks (merge if within 2*contextLines of each other)
        var groups = new List<(int Start, int End)>();
        int groupStart = changeIndices[0];
        int groupEnd = changeIndices[0];

        for (int i = 1; i < changeIndices.Count; i++)
        {
            if (changeIndices[i] - groupEnd <= contextLines * 2 + 1)
            {
                groupEnd = changeIndices[i];
            }
            else
            {
                groups.Add((groupStart, groupEnd));
                groupStart = changeIndices[i];
                groupEnd = changeIndices[i];
            }
        }
        groups.Add((groupStart, groupEnd));

        // Build hunks with context
        foreach (var (start, end) in groups)
        {
            int hunkStart = Math.Max(0, start - contextLines);
            int hunkEnd = Math.Min(diffLines.Count - 1, end + contextLines);

            // Compute source/target line numbers at hunk start
            int srcLine = 0, tgtLine = 0;
            for (int i = 0; i < hunkStart; i++)
            {
                if (diffLines[i].Type is DiffLineType.Context or DiffLineType.Removed)
                    srcLine++;
                if (diffLines[i].Type is DiffLineType.Context or DiffLineType.Added)
                    tgtLine++;
            }

            var hunkLines = new List<DiffLine>();
            for (int i = hunkStart; i <= hunkEnd; i++)
            {
                hunkLines.Add(diffLines[i]);
            }

            hunks.Add(new Hunk(srcLine, tgtLine, hunkLines));
        }

        return hunks;
    }

    private record Hunk(int SourceStart, int TargetStart, List<DiffLine> Lines);
}
