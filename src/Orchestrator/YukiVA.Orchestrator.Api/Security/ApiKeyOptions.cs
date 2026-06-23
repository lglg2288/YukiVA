namespace YukiVA.Orchestrator.Api.Security;

public class ApiKeyOptions
{
    public const string SectionName = "ApiKey";

    /// <summary>Секретный ключ, который должен прислать клиент. Пусто = защита выключена.</summary>
    public string Key { get; set; } = "";

    /// <summary>Имя заголовка с ключом.</summary>
    public string HeaderName { get; set; } = "X-Api-Key";
}
