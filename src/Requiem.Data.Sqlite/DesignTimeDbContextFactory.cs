using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Requiem.Data.Sqlite;

/// <summary>
/// Lets `dotnet ef` build the SQLite model. Run from the repo root:
/// <c>dotnet ef migrations add Name --project src/Requiem.Data.Sqlite</c>
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RequiemDbContext>
{
    public RequiemDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RequiemDbContext>()
            .UseRequiemDatabase(DatabaseProvider.Sqlite, "Data Source=design-time.db")
            .Options;
        return new RequiemDbContext(options);
    }
}
