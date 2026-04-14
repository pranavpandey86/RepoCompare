# RepoCompare — Git Branch Synchronization Tool

A .NET 9 console application that compares two directory trees (checked-out Git branches) and identifies **truly changed files** by normalizing away encoding, BOM, and whitespace noise.

## The Problem

When two Git repositories diverge due to a manual file copy (drag & drop instead of `git clone`/`git fork`), Git sees them as having **unrelated histories**. A naive `git diff` between the two branches flags **every single file** as different — even when the vast majority are identical aside from whitespace, line endings (CRLF vs LF), or BOM markers.

This tool solves that by:
1. **Normalizing** content (strip BOM, CRLF→LF, trim trailing whitespace)
2. **Hashing** normalized content (SHA-256) for fast equality checks
3. **Categorizing** every file into one of four buckets
4. **Generating** unified diffs, sync scripts, and git workflow scripts

## Categories

| Status | Meaning |
|---|---|
| ✅ **Identical** | Same content after normalization — noise, ignore |
| 🔄 **Modified** | Genuinely different content — real changes |
| ➕ **Only in Source** | Files that exist in source but not target — need to bring over |
| ➕ **Only in Target** | Files that exist in target but not source — preserve |

## Quick Start

```bash
# Build
cd RepoCompare
dotnet build

# Run
dotnet run -- \
  --source /path/to/old-repo \
  --target /path/to/new-repo \
  --source-label "OldRepo / feature-branch" \
  --target-label "NewRepo / main" \
  --output ./comparison_report.md \
  --generate-script
```

## Output

The tool generates:
- **Console summary** — Colored overview of results
- **`comparison_report.md`** — Detailed Markdown report with inline diffs
- **`sync_changes.sh`** — Bash script to copy only truly changed files
- **`git_workflow.sh`** — Complete git workflow (branch → sync → build → commit → push)

## CLI Options

| Option | Description | Default |
|---|---|---|
| `--source, -s` | Path to the source directory | *required* |
| `--target, -t` | Path to the target directory | *required* |
| `--source-label` | Human-readable label for source | `"Source"` |
| `--target-label` | Human-readable label for target | `"Target"` |
| `--output, -o` | Report output path | `./comparison_report.md` |
| `--generate-script` | Generate sync + git scripts | `true` |
| `--no-script` | Don't generate scripts | — |
| `--verbose, -v` | Show detailed progress | `false` |

## How It Works

```
Source Dir ──┐                           ┌── ✅ Identical (noise)
             │   ┌──────────────────┐    ├── 🔄 Modified (with diff)
             ├──▶│ ContentNormalizer ├──▶├── ➕ Only in Source
             │   │  • Strip BOM      │    └── ➕ Only in Target
Target Dir ──┘   │  • CRLF → LF     │
                 │  • Trim spaces    │    ┌── comparison_report.md
                 │  • SHA-256 hash   │──▶├── sync_changes.sh
                 └──────────────────┘    └── git_workflow.sh
```

## Project Structure

```
RepoCompare/
├── RepoCompare.csproj           # .NET 9 console app (zero dependencies)
├── Program.cs                   # Entry point, CLI argument parsing
├── Models/
│   ├── ComparisonResult.cs      # Single file comparison result
│   └── ComparisonSummary.cs     # Aggregate summary with computed stats
├── Services/
│   ├── DirectoryScanner.cs      # Recursive directory walker
│   ├── FileComparer.cs          # Core comparison engine
│   └── ReportGenerator.cs       # Report + script generation
└── Utils/
    ├── ContentNormalizer.cs      # BOM, line ending, whitespace normalization
    └── DiffEngine.cs            # LCS-based unified diff engine
```

## Requirements

- .NET 9.0 SDK
- No external NuGet packages — pure .NET

## License

MIT
