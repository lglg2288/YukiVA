# YukiVA

YukiVA — голосовой AI-ассистент: WPF-клиент записывает речь, оркестратор
распознаёт её через STT, получает ответ от DeepSeek и озвучивает его через TTS.
Клиент также умеет передавать языковой модели MCP-инструменты для работы с
PostgreSQL.

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-4169E1)
![Python](https://img.shields.io/badge/Python-gRPC-3776AB)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

> Проект находится в активной разработке. Конфигурация рассчитана прежде всего
> на локальный запуск и CUDA-совместимую видеокарту NVIDIA.

## Возможности

- запись голоса в Windows-клиенте;
- распознавание русской и английской речи с помощью `faster-whisper`;
- диалог с DeepSeek с сохранением истории сессии в PostgreSQL;
- вызов MCP-инструментов языковой моделью;
- синтез ответа через Coqui XTTS v2;
- защита HTTP API пользовательским ключом.

## Архитектура

```mermaid
flowchart LR
    Client["WPF-клиент"] -->|"HTTP + WAV"| API["Orchestrator API"]
    API -->|"gRPC"| STT["STT / faster-whisper"]
    API -->|"HTTPS"| LLM["DeepSeek API"]
    API -->|"gRPC"| TTS["TTS / XTTS v2"]
    API --> DB[("PostgreSQL")]
    Client -->|"stdio, Docker"| MCP["Postgres MCP"]
```

Основные каталоги:

```text
src/
├── Client/YukiVA.Client.Wpf_                 # Windows-клиент (WPF, MVVM)
├── Orchestrator/
│   ├── YukiVA.Orchestrator.Api               # HTTP API и API-key middleware
│   ├── YukiVA.Orchestrator.Application       # сценарии приложения
│   ├── YukiVA.Orchestrator.Domain            # сущности и доменная модель
│   ├── YukiVA.Orchestrator.Infrastructure    # EF Core, DeepSeek, gRPC-клиенты
│   └── YukiVA.Orchestrator.Tests             # тесты
└── Services/
    ├── YukiVA.Service.STT                    # faster-whisper gRPC
    └── YukiVA.Service.TTS                    # XTTS v2 gRPC
```

## Стек и требования

- [.NET SDK 10](https://dotnet.microsoft.com/download);
- Docker Desktop с Docker Compose;
- PostgreSQL 17 запускается из корневого `docker-compose.yml`;
- для голосовых сервисов — NVIDIA GPU, актуальный драйвер и NVIDIA Container
  Toolkit с поддержкой `--gpus` в Docker;
- Git Bash или WSL для запуска `download_models.sh`;
- Python 3.10/3.11 нужен только при локальной подготовке модели XTTS;
- Windows для запуска WPF-клиента;
- токен DeepSeek API.

ML-модели и образцы голоса не хранятся в Git. Каталоги `models/` и
`speakers/`, а также файлы `*.wav` исключены через `.gitignore`.

## Конфигурация

Оркестратор использует стандартную конфигурацию ASP.NET Core. Любое значение из
`appsettings.json` можно переопределить переменной окружения, заменив вложенность
на двойное подчёркивание.

| Переменная | Назначение | Пример для локального запуска |
|---|---|---|
| `ConnectionStrings__Postgres` | подключение оркестратора к БД | `Host=localhost;Port=5432;Database=yukiva;Username=yukiva;Password=yukiva_dev_password` |
| `Llm__ApiKey` | секретный токен DeepSeek | `sk-...` |
| `Llm__BaseUrl` | адрес OpenAI-compatible API | `https://api.deepseek.com` |
| `Llm__Model` | модель DeepSeek | значение, доступное вашему аккаунту |
| `ApiKey__Key` | ключ, который должны присылать клиенты YukiVA | произвольная длинная случайная строка |
| `ApiKey__HeaderName` | имя HTTP-заголовка с ключом | `X-Api-Key` |
| `Services__Stt` | адрес STT gRPC | `http://localhost:50053` |
| `Services__Tts` | адрес TTS gRPC | `http://localhost:50052` |

`Llm__ApiKey` и `ApiKey__Key` не взаимозаменяемы: первый отправляется в
DeepSeek, второй защищает собственный HTTP API проекта. Если `ApiKey__Key`
оставить пустым, проверка запросов отключается.

Не записывайте секреты в `appsettings.json`. Для локальной разработки удобнее
использовать .NET User Secrets:

```powershell
$apiProject = "src/Orchestrator/YukiVA.Orchestrator.Api/YukiVA.Orchestrator.Api.csproj"

dotnet user-secrets set "Llm:ApiKey" "ВАШ_DEEPSEEK_TOKEN" --project $apiProject
dotnet user-secrets set "ApiKey:Key" "ВАШ_КЛЮЧ_ДЛЯ_YUKIVA_API" --project $apiProject
```

Для WPF-клиента задаются отдельные переменные процесса:

```powershell
$env:YUKIVA_API_URL = "http://localhost:5000"
$env:YUKIVA_API_KEY = "ВАШ_КЛЮЧ_ДЛЯ_YUKIVA_API"
```

## HTTP API

| Метод | Путь | Назначение |
|---|---|---|
| `POST` | `/api/voice?sessionId={guid}` | принять `multipart/form-data` с полем `audio` и необязательным `tools` |
| `POST` | `/api/voice/tool-result` | продолжить диалог после выполнения инструмента |
| `GET` | `/api/sessions/{guid}/messages` | получить историю сессии |

При включённой защите каждый запрос должен содержать
`X-Api-Key: ВАШ_КЛЮЧ_ДЛЯ_YUKIVA_API` (или заголовок с именем из
`ApiKey__HeaderName`).

## Запуск всего пайплайна

Все команды ниже выполняются из корня репозитория, если не указано иное.

### 1. Поднять PostgreSQL

```powershell
docker compose up -d postgres
docker compose ps
```

Контейнер создаёт БД `yukiva`, пользователя `yukiva` и публикует порт `5432`.
Данные сохраняются в Docker volume `yukiva-pgdata`.

### 2. Применить первую миграцию к пустой БД

В репозитории уже есть миграция `InitialCreate`, поэтому для новой пустой БД
достаточно восстановить зависимости и применить её:

```powershell
dotnet tool install --global dotnet-ef --version "10.*"
dotnet restore src/Orchestrator/YukiVA.Orchestrator.slnx

dotnet ef database update `
  --project src/Orchestrator/YukiVA.Orchestrator.Infrastructure/YukiVA.Orchestrator.Infrastructure.csproj `
  --startup-project src/Orchestrator/YukiVA.Orchestrator.Api/YukiVA.Orchestrator.Api.csproj
```

Если `dotnet-ef` уже установлен, вместо установки выполните
`dotnet tool update --global dotnet-ef --version "10.*"` или сразу переходите к
`database update`.

Чтобы создать самую первую миграцию заново в ветке, где каталога `Migrations`
ещё нет:

```powershell
dotnet ef migrations add InitialCreate `
  --project src/Orchestrator/YukiVA.Orchestrator.Infrastructure/YukiVA.Orchestrator.Infrastructure.csproj `
  --startup-project src/Orchestrator/YukiVA.Orchestrator.Api/YukiVA.Orchestrator.Api.csproj `
  --output-dir Migrations
```

После этого также выполните `dotnet ef database update`. Design-time factory
сейчас рассчитана на локальную БД с реквизитами из корневого compose-файла.

### 3. Подготовить и запустить STT

Скрипт скачивает три модели Whisper. Запускайте его из каталога STT — пути в
нём относительные:

```bash
cd src/Services/YukiVA.Service.STT
bash download_models.sh
docker compose build stt-large-v3-turbo-russian
docker compose up -d stt-large-v3-turbo-russian
```

Выбранный сервис доступен на `localhost:50053`. Вместо него можно запустить
`stt-large-v3` на `50051` или `stt-large-v3-turbo` на `50052`. Не запускайте
вариант STT на `50052` одновременно с локальным TTS, который использует тот же
порт хоста. Подробности и требования к VRAM находятся в
[`src/Services/YukiVA.Service.STT/README.md`](src/Services/YukiVA.Service.STT/README.md).

### 4. Подготовить и запустить TTS

TTS ожидает модель XTTS v2 в каталоге:

```text
src/Services/YukiVA.Service.TTS/models/
└── tts_models--multilingual--multi-dataset--xtts_v2/
```

В репозитории пока нет отдельного скрипта загрузки XTTS. Один из вариантов —
подготовить модель через Coqui TTS в Linux/WSL:

```bash
cd src/Services/YukiVA.Service.TTS
python3 -m venv .venv
source .venv/bin/activate
pip install --upgrade pip
pip install TTS==0.22.0
export TTS_HOME="$PWD/models"
export COQUI_TOS_AGREED=1
python -c "from TTS.api import TTS; TTS('tts_models/multilingual/multi-dataset/xtts_v2')"
```

Значение `COQUI_TOS_AGREED=1` означает принятие условий использования модели —
ознакомьтесь с ними до запуска команды.

Затем положите чистый образец нужного голоса в
`src/Services/YukiVA.Service.TTS/speakers/reference.wav`. Используйте только
голос, на применение которого у вас есть разрешение.

Текущий TTS `docker-compose.yml` содержит только имя готового образа, без секции
`build`. Поэтому первый запуск выглядит так:

```powershell
Set-Location src/Services/YukiVA.Service.TTS
docker build -t yukivaservicetts-tts:latest .
docker compose up -d tts
Set-Location ../../..
```

Альтернатива — добавить под `services.tts` строку `build: .`; модель всё равно
должна существовать до сборки, потому что Dockerfile копирует её в образ. TTS
публикуется на `localhost:50052`.

### 5. Настроить секреты и адреса сервисов

Если секреты ещё не сохранены через User Secrets, выполните команды из раздела
«Конфигурация». Затем задайте локальные gRPC-адреса для текущего окна PowerShell:

```powershell
$env:Services__Stt = "http://localhost:50053"
$env:Services__Tts = "http://localhost:50052"
```

Это важно: в текущем `appsettings.json` указаны адреса сервисов в локальной сети,
а не локальные Docker-порты.

### 6. Запустить оркестратор

```powershell
dotnet run --no-launch-profile `
  --project src/Orchestrator/YukiVA.Orchestrator.Api/YukiVA.Orchestrator.Api.csproj
```

API слушает `http://localhost:5000`. Опция `--no-launch-profile` исключает
конфликт с портом `5203`, указанным в `launchSettings.json`.

### 7. Запустить WPF-клиент

В новом окне PowerShell:

```powershell
$env:YUKIVA_API_URL = "http://localhost:5000"
$env:YUKIVA_API_KEY = "ВАШ_КЛЮЧ_ДЛЯ_YUKIVA_API"

dotnet run --project src/Client/YukiVA.Client.Wpf_/YukiVA.Client.Wpf.csproj
```

Сейчас MCP-подключение клиента использует захардкоженную тестовую строку
`postgresql://postgres:postgres@host.docker.internal:5432/demo`. Она не совпадает
с БД `yukiva` из корневого compose-файла. До вынесения настройки в конфигурацию
нужно либо создать такую тестовую БД, либо изменить строку подключения в
`McpToolProvider.cs`. Для работы обычного голосового контура MCP не требуется,
но текущий клиент инициализирует провайдер при запуске.

## Проверка и диагностика

Собрать оркестратор и выполнить тесты:

```powershell
dotnet build src/Orchestrator/YukiVA.Orchestrator.slnx
dotnet test src/Orchestrator/YukiVA.Orchestrator.slnx
```

Интеграционный тест репозитория запускает PostgreSQL через Testcontainers,
поэтому перед `dotnet test` должен работать Docker Desktop.

Посмотреть состояние контейнеров и логи:

```powershell
docker compose ps
docker compose -f src/Services/YukiVA.Service.STT/docker-compose.yml logs -f
docker compose -f src/Services/YukiVA.Service.TTS/docker-compose.yml logs -f
```

Частые проблемы:

- `401 Unauthorized` — значение `YUKIVA_API_KEY` клиента не совпадает с
  `ApiKey:Key` оркестратора;
- ответ DeepSeek `401/403` — не задан или недействителен `Llm:ApiKey`;
- ошибка подключения gRPC — проверьте `Services__Stt`, `Services__Tts` и
  опубликованные Docker-порты;
- TTS не видит голос — проверьте путь `speakers/reference.wav` внутри каталога
  TTS;
- контейнер не видит GPU — проверьте `nvidia-smi` на хосте и поддержку
  `docker run --gpus all ...`;
- `address already in use` на `50052` — одновременно запущены TTS и
  `stt-large-v3-turbo`.

Остановить компоненты:

```powershell
docker compose down
docker compose -f src/Services/YukiVA.Service.STT/docker-compose.yml down
docker compose -f src/Services/YukiVA.Service.TTS/docker-compose.yml down
```

Команда `down` не удаляет данные PostgreSQL. Для удаления volume требуется
отдельный флаг `--volumes`; используйте его только если данные больше не нужны.

## Лицензия

Проект распространяется по лицензии [MIT](LICENSE).
