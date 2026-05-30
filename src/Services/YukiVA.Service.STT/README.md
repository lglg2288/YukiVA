# YukiVA STT Service

gRPC-сервис распознавания речи на базе faster-whisper. Поддерживает русский, английский и code-switching между ними.

## Архитектура

Один Docker-образ, два контейнера с разными моделями:

| Сервис | Модель | Порт | VRAM (FP16) |
|---|---|---|---|
| `stt-large-v3` | large-v3 | 50051 | ~3.1 GB |
| `stt-large-v3-turbo` | large-v3-turbo | 50052 | ~1.6 GB |

Модель **запекается в образ** при сборке — после `docker save/load` интернет не нужен.

## gRPC API

```protobuf
// Распознать аудио → текст
rpc ConvertToText (STTrequest) returns (STTresponse);

// Узнать загруженность сервиса
rpc GetStatus (StatusRequest) returns (StatusResponse);
```

`GetStatus` возвращает: название модели, количество запросов в очереди, флаг обработки прямо сейчас. Удобно для роутинга — перед отправкой аудио спроси оба сервиса и выбери менее загруженный.

Аудио передаётся как `bytes` — формат любой, который понимает ffmpeg (WAV, OGG, MP3...).

## Первый запуск (на сервере с интернетом)

```bash
# 1. Скачать модели
bash download_models.sh
# Создаст: ./models/large-v3/ и ./models/large-v3-turbo/

# 2. Собрать образы
docker compose build

# или только один:
docker compose build stt-large-v3
docker compose build stt-large-v3-turbo
```

## Экспорт и перенос на локальную машину

```bash
# На сервере — экспортировать
docker save stt-large-v3:latest | gzip > stt-large-v3.tar.gz
docker save stt-large-v3-turbo:latest | gzip > stt-large-v3-turbo.tar.gz

# Локально — загрузить
docker load < stt-large-v3.tar.gz
docker load < stt-large-v3-turbo.tar.gz
```

## Запуск

```bash
docker compose up stt-large-v3              # только large-v3
docker compose up stt-large-v3-turbo        # только turbo
docker compose up                           # оба
docker compose up --build stt-large-v3      # пересобрать и запустить
```

## Проверка VRAM при старте

При запуске сервис проверяет свободную VRAM через `nvidia-smi`. Если памяти не хватает — контейнер сразу падает с сообщением вида:

```
Not enough VRAM: 1200 MB free, 3500 MB required. Exiting.
```

> **Важно:** пороги `MIN_VRAM_MB` прикинуты на глаз и не учитывают, что параллельно может работать другой сервис (например, оба контейнера на одном GPU). Если контейнер не стартует — проверь `MIN_VRAM_MB` в `docker-compose.yml` и реальную загрузку памяти через `nvidia-smi`.

## Выбор GPU (несколько видеокарт)

В `docker-compose.yml` у каждого сервиса есть поле `device_ids` — указывай индекс нужной карты:

```yaml
devices:
  - driver: nvidia
    device_ids: ['0']   # или ['1'], ['2'] и т.д.
    capabilities: [gpu]
```

Узнать индексы своих карт:
```bash
nvidia-smi --query-gpu=index,name --format=csv
```

Внутри контейнера назначенная карта всегда видна как `cuda:0`, поэтому VRAM-чек работает корректно — видит только её.

## ENV переменные

| Переменная | Дефолт | Описание |
|---|---|---|
| `MODEL_NAME` | `large-v3` | Название модели (влияет на путь `/models/{MODEL_NAME}`) |
| `DEVICE` | `cuda` | `cuda` или `cpu` |
| `COMPUTE_TYPE` | `float16` | `float16` / `int8_float16` / `int8` |
| `BEAM_SIZE` | `5` | Качество vs скорость. 1 = быстро/хуже, 5 = стандарт |
| `MIN_VRAM_MB` | `0` | Минимум свободной VRAM для старта. 0 = проверка отключена |
| `PORT` | `50051` | gRPC порт внутри контейнера |
