using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Domain.Enums;

/// <summary>
/// Кто является автором сообщения.
/// </summary>
public enum MessageRole
{
    User = 0,      // сообщение от пользователя
    Assistant = 1, // ответ языковой модели
    System = 2,    // системная инструкция (задаёт поведение модели)
    Tool = 3       // результат вызова инструмента (например, данные из БД)
}
