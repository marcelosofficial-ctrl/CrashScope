using CrashScope.Agent.Sessions;
using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Tests;

public sealed class WorkloadClassifierTests
{
    [Theory]
    [InlineData("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "CrashScope - Google Chrome")]
    [InlineData("Spotify", @"C:\Program Files\WindowsApps\Spotify\Spotify.exe", "Spotify Premium")]
    [InlineData("WindowsTerminal", @"C:\Program Files\WindowsApps\WindowsTerminal.exe", "Windows PowerShell")]
    [InlineData("steamwebhelper", @"C:\STEAM\bin\cef\steamwebhelper.exe", "Steam")]
    [InlineData("TextInputHost", @"C:\Windows\SystemApps\TextInputHost.exe", "Windows Input Experience")]
    [InlineData("ApplicationFrameHost", @"C:\Windows\System32\ApplicationFrameHost.exe", "Settings")]
    [InlineData("SystemSettings", @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe", "Settings")]
    [InlineData("amdow", null, "amd dvr overlay")]
    public void KnownHelpersAreUnlikely(
        string name,
        string? path,
        string title)
    {
        var result = WorkloadClassifier.Classify(name, path, title, 512);

        Assert.Equal(WorkloadKind.HelperOrSystem, result.Kind);
        Assert.Equal(WorkloadRecommendation.Unlikely, result.Recommendation);
        Assert.True(result.Score < 25);
    }

    [Theory]
    [InlineData("Cyberpunk2077", @"C:\STEAM\steamapps\common\Cyberpunk 2077\bin\x64\Cyberpunk2077.exe")]
    [InlineData("UnknownGameExe", @"D:\SteamLibrary\steamapps\common\Mystery Game\Mystery.exe")]
    [InlineData("eldenring", @"D:\Games\ELDEN RING\Game\eldenring.exe")]
    public void GamesAreRecommended(string name, string path)
    {
        var result = WorkloadClassifier.Classify(name, path, name, 2048);

        Assert.Equal(WorkloadKind.Game, result.Kind);
        Assert.Equal(WorkloadRecommendation.Recommended, result.Recommendation);
        Assert.True(result.Score >= 75);
    }

    [Theory]
    [InlineData("OCCT", @"C:\Tools\OCCT\OCCT.exe", WorkloadKind.BenchmarkOrStressTest)]
    [InlineData("llama-server", @"M:\LocalAI\llama.cpp\llama-server.exe", WorkloadKind.AiOrCompute)]
    [InlineData("ComfyUI", @"M:\AI\ComfyUI\ComfyUI.exe", WorkloadKind.AiOrCompute)]
    [InlineData("blender", @"C:\Program Files\Blender Foundation\Blender\blender.exe", WorkloadKind.CreativeOrRenderer)]
    public void DiagnosticComputeWorkloadsAreRecommended(
        string name,
        string path,
        WorkloadKind expectedKind)
    {
        var result = WorkloadClassifier.Classify(name, path, null, 1024);

        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(WorkloadRecommendation.Recommended, result.Recommendation);
    }

    [Fact]
    public void UnknownInteractiveAppRemainsPossibleInsteadOfBeingHidden()
    {
        var result = WorkloadClassifier.Classify(
            "CustomTool",
            @"C:\Tools\CustomTool.exe",
            "Custom Tool",
            256);

        Assert.Equal(WorkloadKind.GeneralApplication, result.Kind);
        Assert.Equal(WorkloadRecommendation.Possible, result.Recommendation);
    }
}
