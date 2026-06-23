using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Языковая модель: по истории диалога возвращает ответ ассистента.</summary>
public interface ILlmService
{
    Task<LlmResult> CompleteAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken cancellationToken = default);
}
