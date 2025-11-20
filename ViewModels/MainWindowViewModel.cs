using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using ICTVisualizer.Services;
using ICTVisualizer.Models;
using System.Threading.Tasks;

namespace ICTVisualizer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private AIService _aiService;
    private AIConfig _aiConfig;
    private List<(string role, string content)> _aiConversationHistory = new();
    private int _maxConversationLength = 10; // Максимальное количество сообщений (вопрос + ответ)
    private string? _pendingDeletionPath = null; // Путь к файлу/папке для подтверждения удаления
    private const int MaxFileContentContextLength = 2000; // Максимальная длина содержимого файла, передаваемого в контексте

    public MainWindowViewModel()
    {
        _aiConfig = new AIConfig();
        _aiService = new AIService(_aiConfig);
    }

    public ObservableCollection<string> AvailableCommands { get; } = new()
    {
        "спроси <вопрос> - задать вопрос ассистенту",
        "? <вопрос> - короткий вариант для вопроса",
        "открыть сайт <url> - открывает сайт по названию или url",
        "посчитать <выражение> - вычислить арифметическое выражение (например: посчитать 2+2)",
        "= <выражение> - быстрый подсчёт (например: =2*(3+4))",
        "время - показывает текущее время",
        "привет - выводит приветствие",
        "setkey <key> - установить API ключ OpenRouter (временно)",
        "setmodel <model> - установить модель ИИ (временно)",
        "showconfig - показать текущую конфигурацию ИИ",
        "создать файл <путь> [содержимое] - создает файл с текстом",
        "setcontext <value> - установить длину контекста(количество сообщений).",
        "dir <путь> - показать содержимое папки",
        "читать <путь> - прочитать содержимое файла",
        "очистить - очищает вывод и сбрасывает диалог с ИИ",
        "забыть - сбрасывает диалог с ИИ",
        "удалить <путь> - удалить файл или папку (с подтверждением)",
        "rm <путь> - удалить файл или папку (с подтверждением)"
    };

    public ObservableCollection<string> History { get; } = new();

    [ObservableProperty]
    private string output = "Привет! Я виртуальный помощник. Введите команду для выполнения.";

    [ObservableProperty]
    private string command = "";

    //Свойство для значения contextLen
    private int _contextLen;
    public int ContextLen
    {
        get => _contextLen;
        set => SetProperty(ref _contextLen, value);
    }

    [RelayCommand]
    private async Task ExecuteCommand()
    {
        if (string.IsNullOrWhiteSpace(Command))
            return;

        var original = Command;
        Output = "Выполняю!";

        var commandLower = Command.ToLower().Trim();

        // Шаг 1: Проверяем, есть ли ожидание подтверждения удаления
        if (_pendingDeletionPath != null)
        {
            if (commandLower == "да" || commandLower == "yes")
            {
                try
                {
                    if (System.IO.File.Exists(_pendingDeletionPath))
                    {
                        System.IO.File.Delete(_pendingDeletionPath);
                        Output = $"Файл удален: {_pendingDeletionPath}";
                    }
                    else if (System.IO.Directory.Exists(_pendingDeletionPath))
                    {
                        System.IO.Directory.Delete(_pendingDeletionPath, recursive: true);
                        Output = $"Папка удалена: {_pendingDeletionPath}";
                    }
                }
                catch (Exception ex)
                {
                    Output = $"Ошибка при удалении: {ex.Message}";
                }
            }
            else
            {
                Output = "Удаление отменено.";
            }

            _pendingDeletionPath = null; // Сбрасываем состояние подтверждения
            History.Insert(0, $"[{DateTime.Now:T}] > {original} -> {Output}");
            Command = string.Empty;
            return; // Завершаем выполнение
        }


        try
        {
            // Open site: "открыть сайт <url>"
            if (commandLower.StartsWith("открыть сайт") || commandLower.StartsWith("open site"))
            {
                var parts = Command.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                var url = parts.Length >= 3 ? parts[2] : string.Empty;
                if (string.IsNullOrWhiteSpace(url))
                {
                    Output = "Укажите URL после команды, например: открыть сайт google.com";
                }
                else
                {
                    if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                        url = "https://" + url;
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    Output = $"Открываю сайт: {url}";
                }
            }
            // Calculate: "посчитать <expr>" or "calc <expr>" or "=<expr>"
            else if (commandLower.StartsWith("посчитать") || commandLower.StartsWith("calc") || commandLower.StartsWith("="))
            {
                string expr = string.Empty;
                if (commandLower.StartsWith("="))
                {
                    expr = Command.Substring(1).Trim();
                }
                else
                {
                    var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    expr = parts.Length >= 2 ? parts[1] : string.Empty;
                }

                if (string.IsNullOrWhiteSpace(expr))
                {
                    Output = "Укажите выражение для вычисления, например: посчитать 2+2";
                }
                else
                {
                    try
                    {
                        // Basic expression evaluation using DataTable.Compute
                        var result = EvaluateExpression(expr);
                        Output = $"{expr} = {result}";
                    }
                    catch (Exception ex)
                    {
                        Output = $"Ошибка вычисления выражения: {ex.Message}";
                    }
                }
            }
            // Runtime config commands
            else if (commandLower.StartsWith("setkey") || commandLower.StartsWith("apikey"))
            {
                var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var key = parts.Length >= 2 ? parts[1].Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(key))
                {
                    Output = "Укажите ключ после команды: setkey SK-...";
                }
                else
                {
                    _aiConfig.ApiKey = key;
                    _aiService = new AIService(_aiConfig);
                    Output = "API ключ установлен (в этом сеансе).";
                }
            }
            else if (commandLower.StartsWith("setmodel"))
            {
                var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var model = parts.Length >= 2 ? parts[1].Trim() : string.Empty;
                if (string.IsNullOrWhiteSpace(model))
                {
                    Output = "Укажите название модели после команды: setmodel openai/gpt-4";
                }
                else
                {
                    _aiConfig.Model = model;
                    _aiService = new AIService(_aiConfig);
                    Output = $"Модель установлена: {model} (в этом сеансе).";
                }
            }
            else if (commandLower.StartsWith("showconfig"))
            {
                var masked = string.IsNullOrWhiteSpace(_aiConfig.ApiKey) ? "(не установлен)" : "(установлен)";
                Output = $"Модель: {_aiConfig.Model}\nAPI ключ: {masked}\nБаза: {_aiConfig.BaseUrl}";
            }
            // Create file: "создать файл <path> [content]"
            else if (commandLower.StartsWith("создать файл") || commandLower.StartsWith("create file"))
            {
                var parts = Command.Split(new[] { ' ' }, 4, StringSplitOptions.RemoveEmptyEntries);
                var filePath = parts.Length >= 3 ? parts[2] : string.Empty;
                var fileContent = parts.Length >= 4 ? parts[3] : string.Empty;

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Output = "Укажите путь к файлу, например: создать файл C:\\Users\\User\\Desktop\\test.txt Привет, мир!";
                }
                else
                {
                    try
                    {
                        // Декодируем escape-последовательности (например, \n, \t) перед записью
                        var decodedContent = System.Text.RegularExpressions.Regex.Unescape(fileContent);
                        await System.IO.File.WriteAllTextAsync(filePath, decodedContent);
                        Output = $"Файл успешно создан: {filePath}";
                    }
                    catch (Exception ex)
                    {
                        Output = $"Ошибка при создании файла: {ex.Message}";
                    }
                }
            }
            // Set Context Length: "setcontext <value>"
            else if (commandLower.StartsWith("setcontext"))
            {
                var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out int contextValue))
                {
                    _maxConversationLength = contextValue > 0 ? contextValue : 1;
                    Output = $"Длина контекста установлена в {_maxConversationLength} сообщений.";
                }
                else
                {
                    Output = "Укажите корректное числовое значение, например: setcontext 10";
                }
            }
            // Delete file/directory: "удалить <path>" or "rm <path>"
            else if (commandLower.StartsWith("удалить") || commandLower.StartsWith("rm") || commandLower.StartsWith("delete"))
            {
                var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var path = parts.Length >= 2 ? parts[1].Trim() : string.Empty;

                if (string.IsNullOrWhiteSpace(path))
                {
                    Output = "Укажите путь к файлу или папке для удаления.";
                }
                else if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    Output = $"Файл или папка не найден: {path}";
                }
                else
                {
                    _pendingDeletionPath = path;
                    Output = $"Вы уверены, что хотите удалить '{path}'? Введите 'да' для подтверждения.";
                    // Не очищаем Command и не пишем в историю, ждем подтверждения
                    return;
                }
            }
            // List directory: "dir <path>" or "ls <path>"
            else if (commandLower.StartsWith("dir") || commandLower.StartsWith("ls"))
            {
                var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var path = parts.Length >= 2 ? parts[1] : "."; // Current directory by default

                try
                {
                    if (System.IO.Directory.Exists(path))
                    {
                        var entries = System.IO.Directory.EnumerateFileSystemEntries(path);
                        var formattedEntries = entries.Select(e =>
                        {
                            var name = System.IO.Path.GetFileName(e);
                            // Проверяем, является ли элемент директорией
                            bool isDirectory = System.IO.Directory.Exists(e);
                            // Используем Unicode-символы для иконок
                            string icon = isDirectory ? "📁" : "📄";
                            return $"{icon} {name}";
                        });

                        var output = $"Содержимое папки '{path}':\n" + string.Join("\n", formattedEntries);
                        if (!entries.Any())
                        {
                            output = $"Папка '{path}' пуста.";
                        }
                        Output = output;
                    }
                    else
                    {
                        Output = $"Папка не найдена: {path}";
                    }
                }
                catch (Exception ex) { Output = $"Ошибка доступа к папке: {ex.Message}"; }
            }
            // Read file: "читать <path>" or "cat <path>"
            else if (commandLower.StartsWith("читать") || commandLower.StartsWith("cat"))
            {
                var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                var filePath = parts.Length >= 2 ? parts[1] : string.Empty;

                if (string.IsNullOrWhiteSpace(filePath))
                {
                    Output = "Укажите путь к файлу, например: читать C:\\file.txt";
                }
                else
                {
                    try
                    {
                        Output = await System.IO.File.ReadAllTextAsync(filePath);
                    }
                    catch (Exception ex)
                    {
                        Output = $"Ошибка при чтении файла: {ex.Message}";
                    }
                }
            }
            // Ask AI: "спроси <question>" or "? <question>"
            else if (commandLower.StartsWith("спроси") || commandLower.StartsWith("?"))
            {
                string question;
                if (commandLower.StartsWith("?"))
                {
                    question = Command.Substring(1).Trim();
                }
                else
                {
                    var parts = Command.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    question = parts.Length >= 2 ? parts[1] : string.Empty;
                }

                if (string.IsNullOrWhiteSpace(question))
                {
                    Output = "Задайте вопрос после команды, например: спроси какая погода?";
                }
                else
                {
                    Output = "Думаю...";
                    try
                    {
                        var response = await _aiService.GetAIResponseAsync(question, GetContextForAI());

                        // Добавляем вопрос и ответ в историю диалога для контекста
                        _aiConversationHistory.Add(("user", question));
                        _aiConversationHistory.Add(("assistant", response));

                        // Check if AI wants to run a command
                        if (response.StartsWith("COMMAND:"))
                        {
                            var commandToRun = response.Substring("COMMAND:".Length).Trim();
                            // ИИ предложил команду. Показываем ее в выводе и подставляем в поле ввода.
                            Output = $"ИИ предлагает команду. Нажмите 'Выполнить', чтобы запустить:\n\n{commandToRun}";
                            Command = commandToRun;

                            // Записываем в историю именно то, что спросил пользователь и что предложил ИИ
                            try
                            {
                                var entry = $"[{DateTime.Now:T}] > {original} -> {Output}";
                                History.Insert(0, entry);
                            }
                            catch { }

                            return; // Прерываем выполнение, чтобы не очищать поле Command
                        }
                        else
                        {
                            Output = response;
                        }
                    }
                    catch (Exception ex)
                    {
                        Output = $"Ошибка при обращении к ИИ: {ex.Message}";
                    }
                }
            }
            else switch (commandLower)
                {
                    case "время":
                        Output = $"Текущее время: {DateTime.Now:T}";
                        break;
                    case "привет":
                        Output = "Привет! Чем могу помочь?";
                        break;
                    case "очистить":
                        Output = "";
                        History.Clear();
                        _aiConversationHistory.Clear();
                        break;
                    case "забыть":
                        //очищаем контекст ИИ, не трогая историю
                        _aiConversationHistory.Clear();
                        Output = "Диалог с ИИ сброшен.";
                        break;
                    default:
                        Output = "Извините, я не знаю такой команды. Посмотрите список доступных команд выше.";
                        break;
                }
        }
        catch (Exception ex)
        {
            Output = $"Произошла ошибка: {ex.Message}";
        }

        // Record to history
        try
        {
            var entry = $"[{DateTime.Now:T}] > {original} -> {Output}";
            History.Insert(0, entry);
        }
        catch { }

        Command = string.Empty; // clear input
    }

    [RelayCommand]
    private async Task RunPreset(string preset)
    {
        if (string.IsNullOrWhiteSpace(preset))
            return;
        Command = preset;
        await ExecuteCommand();
    }

    private static object EvaluateExpression(string expr)
    {
        // Use DataTable.Compute for basic arithmetic evaluation
        // Allow only a restricted set of characters for safety
        var allowed = "0123456789+-*/()., ";
        foreach (var c in expr)
        {
            if (!allowed.Contains(c))
                throw new ArgumentException("Выражение содержит недопустимые символы");
        }

        // Replace comma with dot for decimal
        expr = expr.Replace(',', '.');

        var table = new System.Data.DataTable();
        // compute
        var result = table.Compute(expr, string.Empty);
        return result;
    }
    private List<(string role, string content)> GetContextForAI()
    {
        //Обрезаем историю разговоров до MaxConversationLength
        return _aiConversationHistory.TakeLast(_maxConversationLength).ToList();
    }
}
