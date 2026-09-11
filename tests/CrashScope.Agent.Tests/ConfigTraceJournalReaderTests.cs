using CrashScope.Agent.Evidence.ConfigTrace;
using CrashScope.Core.Evidence;

namespace CrashScope.Agent.Tests;

public sealed class ConfigTraceJournalReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "CrashScope-ConfigTraceJournalReaderTests",
        Guid.NewGuid().ToString("N"));

    public ConfigTraceJournalReaderTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Read_ParsesMeasuredSchemaAndNeverExposesSensitiveValues()
    {
        var path = Journal(
            """
            {"record_type":"session_start","schema_version":1,"observed_unix_ms":1789137294634,"root":"C:/game","label":"test","settle_ms":200}
            """,
            """
            {"record_type":"change","schema_version":1,"sequence":1,"observed_unix_ms":1789137295831,"root":"C:/game","file":{"path":"settings.json","change":"modified","before_sha256":"before","after_sha256":"after","fields":[{"key":"/Renderer","change":"modified","before":"DX12","after":"Vulkan","sensitive":false},{"key":"/api_key","change":"modified","before":"<redacted>","after":"<redacted>","sensitive":true}]}}
            """);

        var result = new ConfigTraceJournalReader().Read(path);

        Assert.True(result.HasSessionStart);
        Assert.Equal(0, result.MalformedRecordCount);
        Assert.Equal(0, result.UnsupportedSchemaCount);

        var item = Assert.Single(result.Events);
        Assert.Equal("ConfigTrace", item.Source);
        Assert.Equal("ConfigChange", item.Kind);
        Assert.Equal(EvidenceSeverity.Information, item.Severity);
        Assert.Equal(item.TimestampUtc, item.ObservedAtUtc);
        Assert.Contains("Renderer changed from DX12 to Vulkan", item.Summary);
        Assert.Contains("api_key changed (sensitive value redacted)", item.Summary);
        Assert.DoesNotContain("before_sha256", item.Details.Keys);
        Assert.DoesNotContain("after_sha256", item.Details.Keys);
        Assert.DoesNotContain("<redacted>", item.Details.Values);
    }

    [Fact]
    public void Read_SkipsMalformedPartialAndUnsupportedRecords()
    {
        var path = Journal(
            """{"record_type":"session_start","schema_version":1,"observed_unix_ms":1000,"root":"C:/game"}""",
            """{"record_type":"change","schema_version":2,"sequence":1,"observed_unix_ms":1100,"root":"C:/game","file":{"path":"x","change":"modified"}}""",
            """{"record_type":"change","schema_version":1""");

        var result = new ConfigTraceJournalReader().Read(path);

        Assert.True(result.HasSessionStart);
        Assert.Empty(result.Events);
        Assert.Equal(1, result.MalformedRecordCount);
        Assert.Equal(1, result.UnsupportedSchemaCount);
    }

    [Fact]
    public void Read_FiltersChangeRecordsToRequestedWindow()
    {
        var path = Journal(
            Change(sequence: 1, observedUnixMs: 1000, renderer: "A"),
            Change(sequence: 2, observedUnixMs: 2000, renderer: "B"),
            Change(sequence: 3, observedUnixMs: 3000, renderer: "C"));

        var result = new ConfigTraceJournalReader().Read(
            path,
            DateTimeOffset.FromUnixTimeMilliseconds(1500),
            DateTimeOffset.FromUnixTimeMilliseconds(2500));

        var item = Assert.Single(result.Events);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(2000), item.TimestampUtc);
        Assert.Contains("B", item.Summary);
    }

    [Fact]
    public void Read_MissingJournalReturnsEmptyResult()
    {
        var result = new ConfigTraceJournalReader().Read(
            Path.Combine(_root, "missing.jsonl"));

        Assert.False(result.HasSessionStart);
        Assert.Empty(result.Events);
        Assert.Equal(0, result.MalformedRecordCount);
        Assert.Equal(0, result.UnsupportedSchemaCount);
    }

    [Fact]
    public void Read_PreservesSequenceFileRootAndFieldNamesWithoutHashes()
    {
        var path = Journal(Change(sequence: 7, observedUnixMs: 5000, renderer: "DX12"));

        var item = Assert.Single(new ConfigTraceJournalReader().Read(path).Events);

        Assert.Equal("7", item.Details["sequence"]);
        Assert.Equal("settings.json", item.Details["file"]);
        Assert.Equal("C:/game", item.Details["root"]);
        Assert.Equal("/Renderer", item.Details["fields"]);
        Assert.False(item.Details.ContainsKey("before_sha256"));
        Assert.False(item.Details.ContainsKey("after_sha256"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private string Journal(params string[] lines)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string Change(
        long sequence,
        long observedUnixMs,
        string renderer) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            record_type = "change",
            schema_version = 1,
            sequence,
            observed_unix_ms = observedUnixMs,
            root = "C:/game",
            file = new
            {
                path = "settings.json",
                change = "modified",
                before_sha256 = "secret-before",
                after_sha256 = "secret-after",
                fields = new[]
                {
                    new
                    {
                        key = "/Renderer",
                        change = "modified",
                        before = "old",
                        after = renderer,
                        sensitive = false
                    }
                }
            }
        });
}