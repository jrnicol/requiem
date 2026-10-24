using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Requiem.Data.SqlServer;

/// <summary>
/// Lets `dotnet ef` build the SQL Server / Azure SQL model. Run from the repo root:
/// <c>dotnet ef migrations add Name --project src/Requiem.Data.SqlServer</c>.
/// The connection string only matters for `database update`; set REQUIEM_DB to a real server.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RequiemDbContext>
{
    public RequiemDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REQUIEM_DB")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=Requiem;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<RequiemDbContext>()
            .UseRequiemDatabase(DatabaseProvider.SqlServer, connectionString)
            .Options;
        return new RequiemDbContext(options);
    }
}
