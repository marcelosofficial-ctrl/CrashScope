using CrashScope.Core.Sessions;

namespace CrashScope.Agent.Sessions;

internal readonly record struct WorkloadClassification(
    WorkloadKind Kind,
    WorkloadRecommendation Recommendation,
    int Score,
    IReadOnlyList<string> Reasons);

internal static class WorkloadClassifier
{
    private static readonly string[] GamePathMarkers =
    {
        "\\steamapps\\common\\",
        "\\epic games\\",
        "\\gog galaxy\\games\\",
        "\\xboxgames\\",
        "\\riot games\\",
        "\\ea games\\",
        "\\ubisoft game launcher\\games\\"
    };

    private static readonly string[] GameMarkers =
    {
        "cyberpunk", "dayz", "r5apex", "apex", "eldenring", "minecraft",
        "witcher", "starfield", "helldivers", "baldursgate", "bg3", "warframe",
        "cs2", "valorant", "fortnite", "overwatch", "projectzomboid"
    };

    private static readonly string[] BenchmarkMarkers =
    {
        "occt", "furmark", "3dmark", "superposition", "heaven", "cinebench",
        "prime95", "aida64", "memtest", "y-cruncher", "ycruncher"
    };

    private static readonly string[] AiMarkers =
    {
        "llama", "comfyui", "stable-diffusion", "automatic1111", "webui",
        "kobold", "ollama", "lmstudio", "text-generation", "invokeai"
    };

    private static readonly string[] CreativeMarkers =
    {
        "blender", "unreal", "unity", "davinci", "resolve", "premiere",
        "afterfx", "maya", "3dsmax", "houdini", "substance"
    };

    private static readonly HashSet<string> HelperNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "spotify", "discord", "slack", "teams",
        "chatgpt", "windowsterminal", "powershell", "pwsh", "cmd", "explorer",
        "applicationframehost", "systemsettings", "textinputhost", "searchhost",
        "startmenuexperiencehost", "shellexperiencehost", "steamwebhelper", "steam",
        "epicgameslauncher", "galaxyclient", "battle.net", "upc", "ubisoftconnect",
        "eadesktop", "ealauncher", "amdow", "radeonsoftware", "gamebar",
        "gamebarftserver", "nvcontainer", "rainmeter"
    };

    internal static WorkloadClassification Classify(
        string processName,
        string? executablePath,
        string? windowTitle,
        double workingSetMiB)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        var name = processName.Trim();
        var path = executablePath ?? string.Empty;
        var title = windowTitle ?? string.Empty;
        var haystack = $"{name} {path} {title}";
        var reasons = new List<string>();

        if (HelperNames.Contains(name) || LooksLikeHelperPath(path))
        {
            reasons.Add("Known launcher, helper, shell, overlay, browser, or system UI process.");
            return new WorkloadClassification(
                WorkloadKind.HelperOrSystem,
                WorkloadRecommendation.Unlikely,
                5,
                reasons);
        }

        if (ContainsAny(haystack, BenchmarkMarkers))
        {
            reasons.Add("Name/path matches a benchmark or stress-test workload.");
            return Finalize(WorkloadKind.BenchmarkOrStressTest, 95, reasons, windowTitle, executablePath, workingSetMiB);
        }

        if (ContainsAny(haystack, AiMarkers))
        {
            reasons.Add("Name/path matches a local AI or compute workload.");
            return Finalize(WorkloadKind.AiOrCompute, 90, reasons, windowTitle, executablePath, workingSetMiB);
        }

        if (ContainsAny(path, GamePathMarkers))
        {
            reasons.Add("Executable is inside a common installed-game library path.");
            return Finalize(WorkloadKind.Game, 90, reasons, windowTitle, executablePath, workingSetMiB);
        }

        if (ContainsAny(haystack, GameMarkers))
        {
            reasons.Add("Name/path matches a known game workload pattern.");
            return Finalize(WorkloadKind.Game, 85, reasons, windowTitle, executablePath, workingSetMiB);
        }

        if (ContainsAny(haystack, CreativeMarkers))
        {
            reasons.Add("Name/path matches a renderer, game engine, or creative compute workload.");
            return Finalize(WorkloadKind.CreativeOrRenderer, 80, reasons, windowTitle, executablePath, workingSetMiB);
        }

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            reasons.Add("Has a visible top-level window but no strong workload signature.");
            return Finalize(WorkloadKind.GeneralApplication, 40, reasons, windowTitle, executablePath, workingSetMiB);
        }

        if (workingSetMiB >= 512 && executablePath is not null)
        {
            reasons.Add("Headless process uses substantial memory and has an executable path.");
            return Finalize(WorkloadKind.GeneralApplication, 30, reasons, windowTitle, executablePath, workingSetMiB);
        }

        reasons.Add("No strong game, benchmark, compute, renderer, or interactive-application signal.");
        return new WorkloadClassification(
            WorkloadKind.HelperOrSystem,
            WorkloadRecommendation.Unlikely,
            0,
            reasons);
    }

    private static WorkloadClassification Finalize(
        WorkloadKind kind,
        int baseScore,
        List<string> reasons,
        string? windowTitle,
        string? executablePath,
        double workingSetMiB)
    {
        var score = baseScore;

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            score += 3;
            reasons.Add("Has a visible top-level window.");
        }

        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            score += 2;
            reasons.Add("Executable path is available.");
        }

        if (workingSetMiB >= 1024)
        {
            score += 5;
            reasons.Add("Uses at least 1 GiB of working memory.");
        }
        else if (workingSetMiB >= 512)
        {
            score += 3;
            reasons.Add("Uses at least 512 MiB of working memory.");
        }

        score = Math.Clamp(score, 0, 100);
        var recommendation = score >= 75
            ? WorkloadRecommendation.Recommended
            : score >= 25
                ? WorkloadRecommendation.Possible
                : WorkloadRecommendation.Unlikely;

        return new WorkloadClassification(kind, recommendation, score, reasons);
    }

    private static bool LooksLikeHelperPath(string path) =>
        path.Contains("\\Windows\\SystemApps\\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("\\Windows\\ImmersiveControlPanel\\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("\\Steam\\bin\\cef\\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("\\steam\\bin\\cef\\", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
