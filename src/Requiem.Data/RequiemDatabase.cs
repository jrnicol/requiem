using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Requiem.Data;

public enum DatabaseProvider { Sqlite, SqlServer }

/// <summary>
/// Picks the database provider. Each provider keeps its own migrations in its own assembly
/// (Requiem.Data.Sqlite / Requiem.Data.SqlServer) because migrations are provider-specific.
/// </summary>
public static class RequiemDatabase
{
    public const string SqliteMigrationsAssembly = "Requiem.Data.Sqlite";
    public const string SqlServerMigrationsAssembly = "Requiem.Data.SqlServer";
    public const string ConnectionStringName = "Requiem";

    public static TBuilder UseRequiemDatabase<TBuilder>(
        this TBuilder builder, DatabaseProvider provider, string connectionString)
        where TBuilder : DbContextOptionsBuilder
    {
        switch (provider)
        {
            case DatabaseProvider.Sqlite:
                builder.UseSqlite(connectionString, o => o.MigrationsAssembly(SqliteMigrationsAssembly));
                break;
            case DatabaseProvider.SqlServer:
                builder.UseSqlServer(connectionString,
                    o => o.MigrationsAssembly(SqlServerMigrationsAssembly).EnableRetryOnFailure());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(provider));
        }
        return builder;
    }

    /// <summary>
    /// Registers <see cref="RequiemDbContext"/> from configuration:
    /// <c>Database:Provider</c> (Sqlite or SqlServer) and <c>ConnectionStrings:Requiem</c>.
    /// </summary>
    /// <param name="contentRoot">
    /// A relative SQLite file path is resolved against this folder (and the folder is created),
    /// so the database lands in the same place whatever directory the app is started from.
    /// </param>
    public static IServiceCollection AddRequiemData(
        this IServiceCollection services, IConfiguration configuration, string? contentRoot = null)
    {
        var providerSetting = configuration["Database:Provider"];
        var provider = DatabaseProvider.Sqlite;
        if (providerSetting is not null && !Enum.TryParse(providerSetting, ignoreCase: true, out provider))
            throw new InvalidOperationException(
                $"Database:Provider '{providerSetting}' is not one of: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Missing connection string '{ConnectionStringName}'.");

        if (provider == DatabaseProvider.Sqlite && contentRoot is not null)
            connectionString = ResolveSqlitePath(connectionString, contentRoot);

        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<RequiemDbContext>(o => o.UseRequiemDatabase(provider, connectionString));
        return services;
    }

    private static string ResolveSqlitePath(string connectionString, string contentRoot)
    {
        var csb = new SqliteConnectionStringBuilder(connectionString);
        if (csb.DataSource is "" or ":memory:" || csb.Mode == SqliteOpenMode.Memory || Path.IsPathRooted(csb.DataSource))
            return connectionString;

        csb.DataSource = Path.GetFullPath(Path.Combine(contentRoot, csb.DataSource));
        Directory.CreateDirectory(Path.GetDirectoryName(csb.DataSource)!);
        return csb.ToString();
    }
}
