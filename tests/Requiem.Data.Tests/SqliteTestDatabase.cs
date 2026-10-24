using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Requiem.Data.Tests;

/// <summary>
/// An in-memory SQLite database built by the real SQLite migrations, living as long as this object.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<RequiemDbContext> _options;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 23, 20, 0, 0, TimeSpan.Zero));

    /// <param name="upToMigration">Stop at this migration (by name) to test an upgrade; null applies them all.</param>
    public SqliteTestDatabase(string? upToMigration = null)
    {
        // "Foreign Keys=True" because EF only switches them on for connections it opens itself, and the
        // real database (SQLite or SQL Server) enforces them.
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=True");
        _connection.Open();
        _options = new DbContextOptionsBuilder<RequiemDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly(RequiemDatabase.SqliteMigrationsAssembly))
            .Options;
        using var db = CreateContext();
        db.Database.Migrate(upToMigration);
    }

    /// <summary>Applies the remaining migrations.</summary>
    public void MigrateToLatest()
    {
        using var db = CreateContext();
        db.Database.Migrate();
    }

    /// <summary>Runs raw SQL, e.g. to insert rows shaped like an older schema.</summary>
    public void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>A fresh context (empty change tracker) over the same database.</summary>
    public RequiemDbContext CreateContext() => new(_options, Time);

    public void Dispose() => _connection.Dispose();
}
