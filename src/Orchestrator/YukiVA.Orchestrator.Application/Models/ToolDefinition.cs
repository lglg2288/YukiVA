using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Application.Models;

/// <summary>Описание инструмента, доступного LLM (приходит от клиента).</summary>
public record ToolDefinition(
    string Name,
    string Description,
    string ParametersJsonSchema);
