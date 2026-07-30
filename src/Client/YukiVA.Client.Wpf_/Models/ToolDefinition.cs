namespace YukiVA.Client.Wpf.Models;

/// <summary>
/// Описание инструмента для LLM. Поля названы ТОЧНО как ToolDefinition на сервере
/// (Name / Description / ParametersJsonSchema) — иначе сервер их не разберёт.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema);
