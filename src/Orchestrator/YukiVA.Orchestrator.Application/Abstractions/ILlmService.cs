using YukiVA.Orchestrator.Application.Models;
using YukiVA.Orchestrator.Domain.Entities;

namespace YukiVA.Orchestrator.Application.Abstractions;

/// <summary>Языковая модель: по истории диалога потоково отдаёт ответ ассистента.</summary>
public interface ILlmService
{
    /// <summary>
    /// Дельты текста по мере генерации, затем (если были) один <see cref="LlmToolCalls"/>
    /// со всеми вызовами, последним — <see cref="LlmCompleted"/>.
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<Message> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct);
}
