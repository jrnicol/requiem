using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Requiem.Data.Entities;
using Requiem.Data.Stores;

namespace Requiem.Data.Dynamo;

public static class DynamoRegistration
{
    /// <summary>
    /// Registers the DynamoDB client and the stores (SERVERLESS.md), configured from the <c>DynamoDB</c> section.
    /// Call before Identity is added, so its stores are these.
    /// </summary>
    public static IServiceCollection AddRequiemData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DynamoOptions>(configuration.GetSection(DynamoOptions.Section));
        services.AddSingleton<IAmazonDynamoDB>(sp => CreateClient(sp.GetRequiredService<IOptions<DynamoOptions>>().Value));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CatalogCache>();

        services.AddScoped<AuditBatch>();
        services.AddScoped<DynamoWriter>();
        services.AddScoped<ICatalogStore, DynamoCatalogStore>();
        services.AddScoped<ICharacterStore, DynamoCharacterStore>();
        services.AddScoped<ICampaignStore, DynamoCampaignStore>();
        services.AddScoped<ISceneStore, DynamoSceneStore>();
        services.AddScoped<IAuditStore, DynamoAuditStore>();
        services.AddScoped<IUserDirectory, DynamoUserDirectory>();
        services.AddScoped<IDatabaseSetup, DynamoDatabaseSetup>();

        services.AddScoped<IUserStore<RequiemUser>, DynamoUserStore>();
        services.AddScoped<IRoleStore<IdentityRole>, DynamoRoleStore>();
        return services;
    }

    /// <summary>Keeps the sign-in cookie's key ring in the table.</summary>
    public static IDataProtectionBuilder PersistKeysToDynamo(this IDataProtectionBuilder builder)
    {
        builder.Services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp => new ConfigureOptions<KeyManagementOptions>(options =>
            options.XmlRepository = new DynamoXmlRepository(
                sp.GetRequiredService<IAmazonDynamoDB>(), sp.GetRequiredService<IOptions<DynamoOptions>>().Value.TableName)));
        return builder;
    }

    public static IAmazonDynamoDB CreateClient(DynamoOptions options)
    {
        if (options.ServiceUrl is not { } url)
            return new AmazonDynamoDBClient(new AmazonDynamoDBConfig { RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region) });

        // DynamoDB Local accepts any credentials.
        return new AmazonDynamoDBClient(new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = url, AuthenticationRegion = options.Region });
    }
}

/// <summary>Creates the table where allowed (DynamoDB Local), then seeds the catalog.</summary>
internal sealed class DynamoDatabaseSetup(DynamoWriter table, IOptions<DynamoOptions> options, ILogger<DynamoDatabaseSetup> logger) : IDatabaseSetup
{
    public async Task InitializeAsync(CancellationToken ct)
    {
        if (options.Value.CreateTable) await DynamoSchema.EnsureTableAsync(table.Client, table.Table, ct);
        var seeded = await DynamoCatalog.SeedAsync(table, ct);
        if (seeded.Total > 0) logger.LogInformation("Seeded catalog: {Result}", seeded);
    }

    // Nothing to upgrade: the DynamoDB data starts fresh, after campaigns existed (SERVERLESS.md).
    public Task AfterFirstAccountAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>The table's shape. On AWS the CloudFormation template creates the same thing.</summary>
public static class DynamoSchema
{
    public static async Task EnsureTableAsync(IAmazonDynamoDB client, string tableName, CancellationToken ct)
    {
        try
        {
            await client.DescribeTableAsync(tableName, ct);
            return;
        }
        catch (ResourceNotFoundException)
        {
        }

        static GlobalSecondaryIndex Index(string name, string pk, string sk) => new()
        {
            IndexName = name,
            KeySchema = [new KeySchemaElement(pk, KeyType.HASH), new KeySchemaElement(sk, KeyType.RANGE)],
            Projection = new Projection { ProjectionType = ProjectionType.ALL },
        };

        try
        {
            await client.CreateTableAsync(new CreateTableRequest
            {
                TableName = tableName,
                BillingMode = BillingMode.PAY_PER_REQUEST,
                KeySchema = [new KeySchemaElement(Keys.PK, KeyType.HASH), new KeySchemaElement(Keys.SK, KeyType.RANGE)],
                AttributeDefinitions =
                [
                    new AttributeDefinition(Keys.PK, ScalarAttributeType.S), new AttributeDefinition(Keys.SK, ScalarAttributeType.S),
                    new AttributeDefinition(Keys.Gsi1PK, ScalarAttributeType.S), new AttributeDefinition(Keys.Gsi1SK, ScalarAttributeType.S),
                    new AttributeDefinition(Keys.Gsi2PK, ScalarAttributeType.S), new AttributeDefinition(Keys.Gsi2SK, ScalarAttributeType.S),
                ],
                GlobalSecondaryIndexes = [Index(Keys.Gsi1, Keys.Gsi1PK, Keys.Gsi1SK), Index(Keys.Gsi2, Keys.Gsi2PK, Keys.Gsi2SK)],
            }, ct);
        }
        catch (ResourceInUseException)
        {
            // Another instance created it first.
        }

        for (var i = 0; i < 60; i++)
        {
            if ((await client.DescribeTableAsync(tableName, ct)).Table.TableStatus == TableStatus.ACTIVE) return;
            await Task.Delay(250, ct);
        }
        throw new TimeoutException($"Table {tableName} didn't become active.");
    }
}

/// <summary>
/// Starts DynamoDB Local (Java) for development and tests. Finds Java under JAVA_HOME or Amazon Corretto's
/// install folder, and DynamoDB Local under DYNAMODB_LOCAL_HOME or %LOCALAPPDATA%\DynamoDBLocal.
/// </summary>
public sealed class DynamoDbLocal : IDisposable
{
    private readonly Process _process;

    public string ServiceUrl { get; }

    private DynamoDbLocal(Process process, int port)
    {
        _process = process;
        ServiceUrl = $"http://127.0.0.1:{port}";
    }

    /// <param name="dbPath">A folder to keep the data in, or null to keep it in memory.</param>
    public static DynamoDbLocal Start(string? dbPath = null, int? port = null)
    {
        var home = Environment.GetEnvironmentVariable("DYNAMODB_LOCAL_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamoDBLocal");
        var jar = Path.Combine(home, "DynamoDBLocal.jar");
        if (!File.Exists(jar))
            throw new InvalidOperationException($"DynamoDB Local isn't at {jar}. Set DYNAMODB_LOCAL_HOME, or see SERVERLESS.md.");

        var chosen = port ?? FreePort();
        var storage = dbPath is null ? "-inMemory" : $"-dbPath \"{Directory.CreateDirectory(dbPath).FullName}\"";
        var process = Process.Start(new ProcessStartInfo(FindJava(),
            $"-Djava.library.path=\"{Path.Combine(home, "DynamoDBLocal_lib")}\" -jar \"{jar}\" -sharedDb {storage} -port {chosen}")
        {
            WorkingDirectory = home,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("DynamoDB Local didn't start.");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var local = new DynamoDbLocal(process, chosen);
        // Stop it with whatever started it (the app or a test run), which otherwise waits on its output pipes.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => local.Dispose();
        local.WaitUntilListening();
        return local;
    }

    private void WaitUntilListening()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited) throw new InvalidOperationException($"DynamoDB Local exited with code {_process.ExitCode}.");
            try
            {
                using var socket = new TcpClient();
                socket.Connect(IPAddress.Loopback, new Uri(ServiceUrl).Port);
                return;
            }
            catch (SocketException)
            {
                Thread.Sleep(200);
            }
        }
        throw new TimeoutException("DynamoDB Local didn't start listening within 30 seconds.");
    }

    private static string FindJava()
    {
        var name = OperatingSystem.IsWindows() ? "java.exe" : "java";
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome && File.Exists(Path.Combine(javaHome, "bin", name)))
            return Path.Combine(javaHome, "bin", name);
        var corretto = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto");
        if (Directory.Exists(corretto)
            && Directory.GetDirectories(corretto).OrderDescending().Select(d => Path.Combine(d, "bin", name)).FirstOrDefault(File.Exists) is { } found)
            return found;
        return name; // on the PATH
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already disposed.
        }
    }
}
