import io
import os
import logging
import grpc
import soundfile as sf
from concurrent import futures
from TTS.api import TTS

import tts_pb2
import tts_pb2_grpc

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

GRPC_PORT = os.getenv("GRPC_PORT", "50052")
SPEAKER_WAV = os.getenv("SPEAKER_WAV", "speakers/alya.wav")
LANGUAGE = os.getenv("TTS_LANGUAGE", "ru")


class TTSServicer(tts_pb2_grpc.TTSserviceServicer):
    def __init__(self):
        logger.info("Загрузка XTTS v2...")
        self.tts = TTS("tts_models/multilingual/multi-dataset/xtts_v2").to("cuda")
        logger.info("Модель загружена")

    def GenerateAudio(self, request, context):
        if not request.text:
            context.abort(grpc.StatusCode.INVALID_ARGUMENT, "Текст не может быть пустым")

        try:
            logger.info(f"Генерация аудио для: {request.text[:50]}...")

            wav = self.tts.tts(
                text=request.text,
                speaker_wav=SPEAKER_WAV,
                language=LANGUAGE,
            )

            # numpy array → WAV bytes
            buf = io.BytesIO()
            sf.write(buf, wav, samplerate=24000, format="WAV")
            buf.seek(0)

            return tts_pb2.TTSresponse(audio=buf.read())

        except Exception as e:
            logger.error(f"Ошибка генерации: {e}")
            context.abort(grpc.StatusCode.INTERNAL, str(e))


def serve():
    server = grpc.server(futures.ThreadPoolExecutor(max_workers=2))
    tts_pb2_grpc.add_TTSserviceServicer_to_server(TTSServicer(), server)
    server.add_insecure_port(f"[::]:{GRPC_PORT}")
    server.start()
    logger.info(f"TTS сервер запущен на порту {GRPC_PORT}")
    server.wait_for_termination()


if __name__ == "__main__":
    serve()