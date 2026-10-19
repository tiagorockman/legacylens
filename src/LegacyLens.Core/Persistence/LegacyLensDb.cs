using System.Text.Json;
using LegacyLens.Core.Analysis.Models;
using LegacyLens.Core.BusinessLogic.Models;
using Microsoft.Data.Sqlite;

namespace LegacyLens.Core.Persistence;

/// <summary>
/// Embedded SQLite persistence (TDD §4.3). Phase 1 populates the `projects`, `symbols`,
/// and `reports` tables; `business_logic` and `transformations` arrive in Phases 2–3.
/// </summary>
public sealed class LegacyLensDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public LegacyLensDb(string databasePath)
    {
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        EnsureSchema();
    }

    public void SaveBaseline(BaselineResult baseline)
    {
        using var transaction = _connection.BeginTransaction();
        var createdUtc = DateTime.UtcNow.ToString("O");

        var errorsByProject = baseline.ProjectSymbols.ToDictionary(
            p => NormalizeName(p.ProjectName),
            p => p.CompilationErrorCount,
            StringComparer.OrdinalIgnoreCase);

        foreach (var project in baseline.Graph.Projects)
        {
            var errors = errorsByProject.GetValueOrDefault(NormalizeName(project.Name));
            Execute(
                """
                INSERT INTO projects (run_id, solution_path, name, file_path, target_framework,
                    project_references, package_references, document_count, build_status,
                    appcat_incident_count, created_utc)
                VALUES ($run, $sln, $name, $path, $tfm, $refs, $pkgs, $docs, $build, $appcat, $created)
                """,
                ("$run", baseline.RunId),
                ("$sln", baseline.SolutionPath),
                ("$name", project.Name),
                ("$path", project.FilePath),
                ("$tfm", project.TargetFramework),
                ("$refs", JsonSerializer.Serialize(project.ProjectReferences)),
                ("$pkgs", JsonSerializer.Serialize(project.PackageReferences.Select(p => $"{p.Name}@{p.Version}"))),
                ("$docs", project.DocumentCount),
                ("$build", errors == 0 ? "OK" : $"{errors} error(s)"),
                ("$appcat", baseline.AppCat?.Incidents.Count ?? 0),
                ("$created", createdUtc));
        }

        foreach (var symbol in baseline.ProjectSymbols.SelectMany(p => p.Symbols))
        {
            Execute(
                """
                INSERT INTO symbols (run_id, project_name, fqn, kind, file_path, complexity,
                    is_deprecated, created_utc)
                VALUES ($run, $project, $fqn, $kind, $path, $complexity, $deprecated, $created)
                """,
                ("$run", baseline.RunId),
                ("$project", symbol.ProjectName),
                ("$fqn", symbol.FullyQualifiedName),
                ("$kind", symbol.Kind.ToString()),
                ("$path", (object?)symbol.FilePath ?? DBNull.Value),
                ("$complexity", symbol.Complexity),
                ("$deprecated", symbol.IsDeprecated ? 1 : 0),
                ("$created", createdUtc));
        }

        foreach (var rule in baseline.Constraints?.Results ?? [])
        {
            Execute(
                """
                INSERT INTO constraints (run_id, policy, rule_id, title, kind, verdict,
                    projects_evaluated, violations, unresolved, reason, created_utc)
                VALUES ($run, $policy, $rule, $title, $kind, $verdict, $projects, $violations, $unresolved, $reason, $created)
                """,
                ("$run", baseline.RunId),
                ("$policy", baseline.Constraints!.Policy.SourceName),
                ("$rule", rule.RuleId),
                ("$title", rule.Title),
                ("$kind", rule.Kind.ToString()),
                ("$verdict", rule.Verdict.ToString()),
                ("$projects", rule.ProjectsEvaluated),
                ("$violations", rule.Violations.Count),
                ("$unresolved", rule.Unresolved.Count),
                ("$reason", (object?)rule.Reason ?? DBNull.Value),
                ("$created", createdUtc));
        }

        Execute(
            """
            INSERT INTO reports (run_id, solution_path, total_incidents, project_count,
                symbol_count, wall_clock_seconds, created_utc)
            VALUES ($run, $sln, $incidents, $projects, $symbols, $seconds, $created)
            """,
            ("$run", baseline.RunId),
            ("$sln", baseline.SolutionPath),
            ("$incidents", baseline.AppCat?.Incidents.Count ?? 0),
            ("$projects", baseline.Graph.Projects.Count),
            ("$symbols", baseline.ProjectSymbols.Sum(p => p.Symbols.Count)),
            ("$seconds", baseline.Elapsed.TotalSeconds),
            ("$created", createdUtc));

        transaction.Commit();
    }

    /// <summary>
    /// Persists extracted domain rules (TDD §4.3 `business_logic`), keyed to the symbol they were
    /// recovered from so they can be injected into later transformation prompts for that code path.
    /// </summary>
    /// <remarks>
    /// The rubric scores and the model snapshot are stored with each rule rather than alongside the
    /// run, because the confidence of an extraction belongs to that extraction: a later reviewer must
    /// be able to see which model produced a given statement and how it scored.
    /// </remarks>
    public void SaveBusinessRules(string runId, IEnumerable<BusinessRule> rules)
    {
        using var transaction = _connection.BeginTransaction();
        var createdUtc = DateTime.UtcNow.ToString("O");

        foreach (var rule in rules)
        {
            Execute(
                """
                INSERT INTO business_logic (run_id, symbol_id, fqn, project, file_path, kind, statement,
                    score_hallucination, score_completeness, score_readability, score_usefulness,
                    model_snapshot, extracted_utc, created_utc)
                VALUES ($run, $symbol, $fqn, $project, $file, $kind, $statement,
                    $hallucination, $completeness, $readability, $usefulness,
                    $model, $extracted, $created)
                """,
                ("$run", runId),
                ("$symbol", rule.SymbolId),
                ("$fqn", rule.FullyQualifiedName),
                ("$project", rule.Project),
                ("$file", (object?)rule.File ?? DBNull.Value),
                ("$kind", rule.Kind.ToString()),
                ("$statement", rule.Statement),
                ("$hallucination", (object?)rule.Score?.Hallucination ?? DBNull.Value),
                ("$completeness", (object?)rule.Score?.Completeness ?? DBNull.Value),
                ("$readability", (object?)rule.Score?.Readability ?? DBNull.Value),
                ("$usefulness", (object?)rule.Score?.Usefulness ?? DBNull.Value),
                ("$model", rule.ModelSnapshot),
                ("$extracted", rule.ExtractedUtc.ToString("O")),
                ("$created", createdUtc));
        }

        transaction.Commit();
    }

    private void EnsureSchema()
    {
        Execute(
            """
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                solution_path TEXT NOT NULL,
                name TEXT NOT NULL,
                file_path TEXT NOT NULL,
                target_framework TEXT,
                project_references TEXT,
                package_references TEXT,
                document_count INTEGER NOT NULL DEFAULT 0,
                build_status TEXT,
                appcat_incident_count INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS symbols (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                project_name TEXT NOT NULL,
                fqn TEXT NOT NULL,
                kind TEXT NOT NULL,
                file_path TEXT,
                complexity INTEGER NOT NULL DEFAULT 0,
                is_deprecated INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS reports (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                solution_path TEXT NOT NULL,
                total_incidents INTEGER NOT NULL DEFAULT 0,
                project_count INTEGER NOT NULL DEFAULT 0,
                symbol_count INTEGER NOT NULL DEFAULT 0,
                wall_clock_seconds REAL NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS constraints (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                policy TEXT NOT NULL,
                rule_id TEXT NOT NULL,
                title TEXT NOT NULL,
                kind TEXT NOT NULL,
                verdict TEXT NOT NULL,
                projects_evaluated INTEGER NOT NULL DEFAULT 0,
                violations INTEGER NOT NULL DEFAULT 0,
                unresolved INTEGER NOT NULL DEFAULT 0,
                reason TEXT,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS business_logic (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                run_id TEXT NOT NULL,
                symbol_id TEXT NOT NULL,
                fqn TEXT NOT NULL,
                project TEXT NOT NULL,
                file_path TEXT,
                kind TEXT NOT NULL,
                statement TEXT NOT NULL,
                score_hallucination INTEGER,
                score_completeness INTEGER,
                score_readability INTEGER,
                score_usefulness INTEGER,
                model_snapshot TEXT NOT NULL,
                extracted_utc TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_business_logic_run_symbol ON business_logic (run_id, symbol_id);
            CREATE INDEX IF NOT EXISTS ix_symbols_run_fqn ON symbols (run_id, fqn);
            CREATE INDEX IF NOT EXISTS ix_projects_run ON projects (run_id);
            """);
    }

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static string NormalizeName(string name)
    {
        var index = name.IndexOf(" (", StringComparison.Ordinal);
        return index > 0 && name.EndsWith(')') ? name[..index] : name;
    }

    public void Dispose() => _connection.Dispose();
}
