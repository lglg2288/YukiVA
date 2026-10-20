namespace YukiVA.Orchestrator.Infrastructure.Services;

/// <summary>
/// Имена HTTP-клиентов. По имени IHttpClientFactory отдаёт общий пул соединений —
/// прогрев открывает соединение в том же пуле, которым потом пользуется сервис.
/// </summary>
public static class HttpClientNames
{
    public const string DeepSeek = "deepseek";
    public const string ElevenLabs = "elevenlabs";
}
