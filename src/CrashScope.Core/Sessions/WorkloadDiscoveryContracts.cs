namespace CrashScope.Core.Sessions;

public enum WorkloadKind
{
    Game,
    BenchmarkOrStressTest,
    AiOrCompute,
    CreativeOrRenderer,
    GeneralApplication,
    HelperOrSystem
}

public enum WorkloadRecommendation
{
    Recommended,
    Possible,
    Unlikely
}
