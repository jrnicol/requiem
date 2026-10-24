using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Requiem.Data.Tests;

public class DatabaseSetupTests
{
    [Fact]
    public void Sqlite_migrations_match_the_model()
    {
        using var db = new SqliteTestDatabase();
        using var ctx = db.CreateContext();
        // Fails when the model changes without `dotnet ef migrations add` for SQLite.
        Assert.False(ctx.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Sql_server_migrations_match_the_model()
    {
        var options = new DbContextOptionsBuilder<RequiemDbContext>()
            .UseRequiemDatabase(DatabaseProvider.SqlServer, "Server=unused;Database=unused")
            .Options;
        using var ctx = new RequiemDbContext(options);
        // Compares the model to the SQL Server snapshot; never opens a connection.
        Assert.False(ctx.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Relative_sqlite_path_resolves_under_content_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "requiem-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var services = BuildServices(root, ("Database:Provider", "Sqlite"),
                ("ConnectionStrings:Requiem", "Data Source=App_Data/requiem.db"));
            using var ctx = services.GetRequiredService<RequiemDbContext>();

            var dataSource = new SqliteConnectionStringBuilder(ctx.Database.GetConnectionString()).DataSource;
            Assert.Equal(Path.Combine(root, "App_Data", "requiem.db"), dataSource);
            Assert.True(Directory.Exists(Path.Combine(root, "App_Data")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Provider_setting_selects_sql_server()
    {
        using var services = BuildServices(null, ("Database:Provider", "sqlserver"),
            ("ConnectionStrings:Requiem", "Server=unused;Database=unused"));
        using var ctx = services.GetRequiredService<RequiemDbContext>();
        Assert.True(ctx.Database.IsSqlServer());
    }

    [Fact]
    public void Unknown_provider_is_a_clear_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => BuildServices(null,
            ("Database:Provider", "Postgres"), ("ConnectionStrings:Requiem", "x")));
        Assert.Contains("Postgres", ex.Message);
    }

    [Fact]
    public void Missing_connection_string_is_a_clear_error() =>
        Assert.Throws<InvalidOperationException>(() => BuildServices(null, ("Database:Provider", "Sqlite")));

    private static ServiceProvider BuildServices(string? contentRoot, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new ServiceCollection().AddRequiemData(configuration, contentRoot).BuildServiceProvider();
    }
}
