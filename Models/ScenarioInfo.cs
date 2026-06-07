using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LlamaServerLauncher.Models;

public class ScenarioInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    [JsonPropertyName("intervalSeconds")]
    public int IntervalSeconds { get; set; }

    [JsonPropertyName("profileNames")]
    public List<string> ProfileNames { get; set; } = new();
}
