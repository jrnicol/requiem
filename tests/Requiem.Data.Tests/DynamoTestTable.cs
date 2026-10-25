using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Requiem.Data.Dynamo;

namespace Requiem.Data.Tests;

/// <summary>
/// A fresh, empty table in DynamoDB Local (one in-memory instance for the whole test run), with the stores
/// wired up as the app wires them. Each request's worth of work runs in its own <see cref="Scope"/>.
/// </summary>
public sealed class DynamoTestTable : IDisposable
{
    private static readonly Lazy<DynamoDbLocal> Local = new(() => DynamoDbLocal.Start());

    private readonly ServiceProvider _services;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 1, 20, 0, 0, TimeSpan.Zero));

    public DynamoTestTable()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DynamoDB:ServiceUrl"] = Local.Value.ServiceUrl,
            ["DynamoDB:TableName"] = $"requiem-test-{Guid.NewGuid():N}",
            ["DynamoDB:CreateTable"] = "true",
        }).Build();

        var services = new ServiceCollection();
        services.AddRequiemData(configuration);
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        _services = services.BuildServiceProvider();

        var options = _services.GetRequiredService<Microsoft.Extensions.Options.IOptions<DynamoOptions>>().Value;
        DynamoSchema.EnsureTableAsync(_services.GetRequiredService<Amazon.DynamoDBv2.IAmazonDynamoDB>(), options.TableName, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>One request's worth of stores; dispose when done.</summary>
    public AsyncServiceScope Scope() => _services.CreateAsyncScope();

    public void Dispose() => _services.Dispose();
}
