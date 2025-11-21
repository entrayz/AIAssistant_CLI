using System;
using System.IO;
using System.Text.Json;

namespace ICTVisualizer.Models;

public class AIConfig
{
    public string ApiKey { get; set; } = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? string.Empty;
    public string Model { get; set; } = "z-ai/glm-4.5-air:free";
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;

    public AIConfig()
    {
        // Try load appsettings.json from app folder if exists
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("AI", out var ai))
                {
                    if (ai.TryGetProperty("ApiKey", out var keyProp))
                        ApiKey = keyProp.GetString() ?? ApiKey;
                    if (ai.TryGetProperty("Model", out var modelProp))
                        Model = modelProp.GetString() ?? Model;
                    if (ai.TryGetProperty("BaseUrl", out var urlProp))
                        BaseUrl = urlProp.GetString() ?? BaseUrl;
                    if (ai.TryGetProperty("Temperature", out var tempProp) && tempProp.TryGetDouble(out var t))
                        Temperature = t;
                    if (ai.TryGetProperty("MaxTokens", out var mtProp) && mtProp.TryGetInt32(out var mt))
                        MaxTokens = mt;
                }
            }
        }
        catch
        {
            // ignore config read errors; defaults will be used
        }

        var envKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (!string.IsNullOrEmpty(envKey))
            ApiKey = envKey;

        var envModel = Environment.GetEnvironmentVariable("OPENROUTER_MODEL");
        if (!string.IsNullOrEmpty(envModel))
            Model = envModel;

        // var envBase = Environment.GetEnvironmentVariable("OPENROUTER_BASE_URL");
        // if (!string.IsNullOrEmpty(envBase))
        //     BaseUrl = envBase;
    }
}