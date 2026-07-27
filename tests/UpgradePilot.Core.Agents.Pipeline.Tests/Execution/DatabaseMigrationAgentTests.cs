using UpgradePilot.Core.Agents.Pipeline.Execution.DatabaseMigration;
using UpgradePilot.Core.Agents.Pipeline.Shared;
using UpgradePilot.Core.Agents.Pipeline.Tests.Fakes;
using UpgradePilot.Core.Domain.Context;

namespace UpgradePilot.Core.Agents.Pipeline.Tests.Execution;

public class DatabaseMigrationAgentTests : IDisposable
{
    private readonly string _fixtureDir = Path.Combine(Path.GetTempPath(), "upgradepilot-efcore-fixture-" + Guid.NewGuid());

    private const string FixtureCsproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.10" />
            <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.10">
              <PrivateAssets>all</PrivateAssets>
            </PackageReference>
            <PackageReference Include="SQLitePCLRaw.bundle_e_sqlite3" Version="3.0.4" />
          </ItemGroup>
        </Project>
        """;

    private const string DbContextSource = """
        using Microsoft.EntityFrameworkCore;

        namespace Fixture;

        public class SampleEntity
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
        }

        public class SampleDbContext : DbContext
        {
            public DbSet<SampleEntity> Entities => Set<SampleEntity>();

            protected override void OnConfiguring(DbContextOptionsBuilder options) =>
                options.UseSqlite("Data Source=fixture.db");
        }
        """;

    public DatabaseMigrationAgentTests()
    {
        Directory.CreateDirectory(_fixtureDir);
        File.WriteAllText(Path.Combine(_fixtureDir, "Fixture.csproj"), FixtureCsproj);
        File.WriteAllText(Path.Combine(_fixtureDir, "SampleDbContext.cs"), DbContextSource);
        File.Copy(
            Path.Combine(TestPaths.FindRepositoryRoot(), "NuGet.Config"),
            Path.Combine(_fixtureDir, "NuGet.Config"));
    }

    [Fact]
    public async Task ExecuteAsync_RealDotnetEf_GeneratesMigrationWithNoDestructiveOperations()
    {
        var projectPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        var agent = new DatabaseMigrationAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new MigrationInput(projectPath, "InitialCreate"), context);

        Assert.True(result.Output.Succeeded, result.Output.RawOutput);
        Assert.Empty(result.Output.DestructiveOperations);
        Assert.Equal(90, result.Confidence);

        var migrationsDir = Path.Combine(_fixtureDir, "Migrations");
        Assert.True(Directory.Exists(migrationsDir));
        Assert.Contains(Directory.EnumerateFiles(migrationsDir), f => f.Contains("InitialCreate") && !f.Contains("Designer"));
    }

    [Fact]
    public async Task ExecuteAsync_FlagsDestructiveOperation_WhenMigrationDropsAColumn()
    {
        var projectPath = Path.Combine(_fixtureDir, "Fixture.csproj");
        var agent = new DatabaseMigrationAgent(new SystemProcessRunner());
        var context = new UpgradeContext(Guid.NewGuid());

        await agent.ExecuteAsync(new MigrationInput(projectPath, "InitialCreate"), context);

        // Simulate the next upgrade step removing a property, forcing a DropColumn migration.
        var dbContextPath = Path.Combine(_fixtureDir, "SampleDbContext.cs");
        var updatedSource = DbContextSource.Replace("public string Name { get; set; } = \"\";", "");
        await File.WriteAllTextAsync(dbContextPath, updatedSource);

        var result = await agent.ExecuteAsync(new MigrationInput(projectPath, "RemoveName"), context);

        Assert.True(result.Output.Succeeded, result.Output.RawOutput);
        Assert.Contains(result.Output.DestructiveOperations, o => o.OperationType == "DropColumn");
        Assert.Equal(50, result.Confidence);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsZeroConfidence_WhenEfCommandFails()
    {
        var runner = new FakeProcessRunner(new ProcessRunResult(1, "", "No DbContext was found"));
        var agent = new DatabaseMigrationAgent(runner);
        var context = new UpgradeContext(Guid.NewGuid());

        var result = await agent.ExecuteAsync(new MigrationInput("/no/such/project.csproj", "Whatever"), context);

        Assert.False(result.Output.Succeeded);
        Assert.Equal(0, result.Confidence);
    }

    [Fact]
    public async Task ValidateAsync_Fails_WhenMigrationDidNotSucceed()
    {
        var agent = new DatabaseMigrationAgent(new FakeProcessRunner(new ProcessRunResult(1, "", "")));
        var context = new UpgradeContext(Guid.NewGuid());
        var failedReport = new MigrationSafetyReport("X", false, [], "error");

        var validation = await agent.ValidateAsync(failedReport, context);

        Assert.False(validation.IsValid);
    }

    public void Dispose()
    {
        if (Directory.Exists(_fixtureDir))
        {
            Directory.Delete(_fixtureDir, recursive: true);
        }
    }
}
