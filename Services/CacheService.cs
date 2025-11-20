using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ICTVisualizer.Services;

public class CacheService
{
    private readonly string _path;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public CacheService(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(AppContext.BaseDirectory, "cache.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;
            var json = File.ReadAllText(_path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                lock (_lock)
                {
                    foreach (var kv in dict)
                        _cache[kv.Key] = kv.Value;
                }
            }
        }
        catch
        {
            // ignore cache load errors
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cache);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // ignore save errors
        }
    }

    public bool TryGet(string question, out string answer)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(question, out answer!);
        }
    }

    public void Set(string question, string answer)
    {
        lock (_lock)
        {
            _cache[question] = answer;
            Save();
        }
    }
}
