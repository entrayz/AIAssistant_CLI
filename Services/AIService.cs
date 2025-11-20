using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading.Tasks;
using ICTVisualizer.Models;

namespace ICTVisualizer.Services;

public class AIService
{
    private readonly AIConfig _config;
    private readonly HttpClient _http;
    private readonly CacheService _cache;

    public AIService(AIConfig config, CacheService? cache = null)
    {
        _config = config;
        _http = new HttpClient();
        _cache = cache ?? new CacheService();
    }

    public async Task<string> GetAIResponseAsync(string message, List<(string role, string content)>? context = null)
    {
        try
        {
            // Normalize question key
            var key = message.Trim();
            if (_cache.TryGet(key, out var cached))
            {
                Logger.Info($"Cache hit for: {key}");
                return cached;
            }

            if (string.IsNullOrWhiteSpace(_config.ApiKey))
            {
                Logger.Error("OpenRouter API key not set");
                return "Ошибка: API ключ OpenRouter не настроен. Установите OPENROUTER_API_KEY или используйте setkey <key>";
            }

            var requestUrl = new Uri(new Uri(_config.BaseUrl.EndsWith("/") ? _config.BaseUrl : _config.BaseUrl + "/"), "chat/completions");
            var messages = new List<object>
            {
                new { role = "system", content =
                 "Ты - ИИ ассистент, встроенный в приложение на рабочем столе. Отвечай коротко и по делу. Не используй форматирование текста в md формате. Но структурируй свой ответ без форматирования" +
                                                 "Если пользователь просит тебя выполнить действие, связанное с файловой системой (создать, прочитать, удалить, показать содержимое папки) или открыть сайт, " +
                                                 "сформулируй соответствующую команду и верни ТОЛЬКО эту команду в формате 'COMMAND: <команда>'. " +
                                                 "Например, если просят 'создай файл test.txt с текстом hello', ты должен вернуть 'COMMAND: создать файл test.txt hello'. " +
                                                 "Если просят 'что в папке C:\\Users?', верни 'COMMAND: dir C:\\Users'. " +
                                                 "Если пользователь просит открыть сайт по названию, угадай наиболее вероятный URL. Например, на запрос 'открой ютуб' верни 'COMMAND: открыть сайт youtube.com'. На запрос 'открой вк' верни 'COMMAND: открыть сайт vk.com'. " +
                                                 "Если нужно сгенерировать содержимое для файла, сгенерируй его и подставь в команду 'создать файл'. В остальных случаях отвечай как обычно." }
            };

            if (context != null)
            {
                foreach (var (role, content) in context)
                {
                    messages.Add(new { role, content });
                }
            }

            messages.Add(new { role = "user", content = message });

            var body = new
            {
                model = _config.Model,
                messages,
                temperature = _config.Temperature,
                max_tokens = _config.MaxTokens
            };

            Logger.Info($"Using model: {_config.Model}");

            var json = JsonSerializer.Serialize(body);
            using var req = new HttpRequestMessage(HttpMethod.Post, requestUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            Logger.Info($"AI request: {message}");
            // Log request URI and a minimal set of headers for debugging
            try
            {
                Logger.Info($"Request URI: {requestUrl}");
            }
            catch { }

            var resp = await _http.SendAsync(req).ConfigureAwait(false);

            var contentStr = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                Logger.Error($"AI response error: {resp.StatusCode} - {contentStr}");
                return $"Ошибка от API: {resp.StatusCode} {contentStr}";
            }

            try
            {
                Logger.Info($"AI raw response: {contentStr}");
                using var doc = JsonDocument.Parse(contentStr);
                var root = doc.RootElement;

                // Попытка извлечь ответ из стандартной структуры OpenAI-like: choices[0].message.content
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var messageElem) && messageElem.TryGetProperty("content", out var contentElem))
                    {
                        var text = contentElem.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            _cache.Set(key, text);
                            Logger.Info($"AI response cached for: {key}");
                            return text.Trim();
                        }
                    }
                    
                    // Попытка извлечь из choices[0].text
                    if (firstChoice.TryGetProperty("text", out var textElem))
                    {
                        var text = textElem.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            _cache.Set(key, text);
                            return text.Trim();
                        }
                    }
                }

                // Если не нашли в стандартных местах, ищем первое попавшееся строковое значение
                var fallback = ExtractFirstString(root);
                if (!string.IsNullOrEmpty(fallback))
                {
                    _cache.Set(key, fallback);
                    return fallback.Trim();
                }

                // Если ничего не найдено, возвращаем сырой ответ как есть
                _cache.Set(key, contentStr);
                return contentStr;
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to parse AI response: {ex.Message}");
                Logger.Info($"Raw response was: {contentStr}");

                // Save raw response to a timestamped file for inspection (helps when server returns HTML)
                try
                {
                    var safeName = $"raw_response_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                    var path = Path.Combine(AppContext.BaseDirectory, safeName);
                    File.WriteAllText(path, contentStr);
                    Logger.Info($"Raw response written to: {path}");
                }
                catch { }

                return $"Ошибка разбора ответа: {ex.Message} (raw response saved if possible)";
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"GetAIResponseAsync exception: {ex.Message}");
            return $"Ошибка: {ex.Message}";
        }

    }

    private static string? ExtractFirstString(JsonElement elem)
    {
        switch (elem.ValueKind)
        {
            case JsonValueKind.String:
                return elem.GetString();
            case JsonValueKind.Object:
                foreach (var prop in elem.EnumerateObject())
                {
                    var found = ExtractFirstString(prop.Value);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in elem.EnumerateArray())
                {
                    var found = ExtractFirstString(item);
                    if (!string.IsNullOrEmpty(found))
                        return found;
                }
                break;
        }
        return null;
    }
}
