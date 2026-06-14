using System;
using System.Collections.Generic;
using System.Text;

namespace YukiVA.Orchestrator.Infrastructure.Options;

/// <summary>Адреса всех внешних сервисов оркестратора. Биндится из секции "Services".</summary>
public class ServiceEndpointsOptions
{
    public const string SectionName = "Services";

    public string Stt { get; set; } = "http://localhost:50051";
    public string Tts { get; set; } = "http://localhost:50052";
}
