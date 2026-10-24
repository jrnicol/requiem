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

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<RequiemDbContext>()
            .UseSqlite(_connection, o => o.MigrationsAssembly(RequiemDatabase.SqliteMigrationsAssembly))
            .Options;
        using var db = CreateContext();
        db.Database.Migrate();
    }

    /// <summary>A fresh context (empty change tracker) over the same database.</summary>
    public RequiemDbContext CreateContext() => new(_options, Time);

    public void Dispose() => _connection.Dispose();
}
