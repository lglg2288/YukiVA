using YukiVA.Orchestrator.Domain.Entities;
using YukiVA.Orchestrator.Domain.ValueObjects;

namespace YukiVA.Orchestrator.Domain.Interfaces;

public interface ILlmService
{
    Task<LlmResult> GenerateAsync(
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken ct = default);
}
