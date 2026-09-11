using CrashScope.Agent.Evidence.ConfigTrace;
using CrashScope.Agent.Product;
using CrashScope.Agent.Settings;
using CrashScope.Core.Evidence;
using Microsoft.Extensions.DependencyInjection;

namespace CrashScope.Agent.Tests;

public sealed class ConfigTraceProductionWiringTests : IDisposable
{
    private readonly string _localDataRoot = Path.Combine(
        Path.GetTempPath(),
        "CrashScope-production-wiring-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ProductionCompositionRegistersOneDynamicConfigTraceProviderOffByDefault()
    {
        var services = new ServiceCollection();
        ProductFeatureComposition.AddServices(services, _localDataRoot);

        using var serviceProvider = services.BuildServiceProvider();

        var evidenceProviders = serviceProvider
            .GetServices<IEvidenceProvider>()
            .ToArray();

        var configTrace = Assert.IsType<ConfigTraceEvidenceProvider>(
            Assert.Single(evidenceProviders));
        var settings = serviceProvider.GetRequiredService<CrashScopeSettingsState>();

        Assert.False(configTrace.IsEnabled);

        var configRoot = Path.Combine(_localDataRoot, "GameConfig");
        Directory.CreateDirectory(configRoot);

        await settings.UpdateConfigTraceRootAsync(configRoot);
        await settings.UpdateConfigTraceEnabledAsync(true);

        Assert.True(configTrace.IsEnabled);

        await settings.UpdateConfigTraceEnabledAsync(false);

        Assert.False(configTrace.IsEnabled);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_localDataRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(_localDataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}