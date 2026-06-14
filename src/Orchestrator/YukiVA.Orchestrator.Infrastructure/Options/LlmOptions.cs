using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Infrastructure.Options;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";
    public string ApiKey { get; set; } = "";
    public string SystemPrompt { get; set; } = "Ты — голосовой ассистент. Отвечай кратко, по-русски, дружелюбно.";
}
