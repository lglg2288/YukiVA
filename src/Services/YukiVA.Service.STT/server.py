import grpc
import os
import subprocess
import sys
import tempfile
import threading
from concurrent import futures

import stt_pb2
import stt_pb2_grpc
from faster_whisper import WhisperModel

MODEL_NAME   = os.environ.get("MODEL_NAME",   "large-v3")
DEVICE       = os.environ.get("DEVICE",       "cuda")
COMPUTE_TYPE = os.environ.get("COMPUTE_TYPE", "float16")
BEAM_SIZE    = int(os.environ.get("BEAM_SIZE", "5"))
PORT         = int(os.environ.get("PORT",      "50051"))
MIN_VRAM_MB  = int(os.environ.get("MIN_VRAM_MB", "0"))

# Model is baked into the image at build time — no download at runtime
MODEL_PATH   = f"/models/{MODEL_NAME}"


def check_vram():
    if MIN_VRAM_MB == 0:
        return
    try:
        out = subprocess.check_output(
            ["nvidia-smi", "--query-gpu=memory.free", "--format=csv,noheader,nounits"],
            text=True,
        )
        free_mb = int(out.strip().splitlines()[0])
        if free_mb < MIN_VRAM_MB:
            print(
                f"Not enough VRAM: {free_mb} MB free, {MIN_VRAM_MB} MB required. Exiting.",
                flush=True,
            )
            sys.exit(1)
        print(f"VRAM OK: {free_mb} MB free (need {MIN_VRAM_MB} MB).")
    except FileNotFoundError:
        print("nvidia-smi not found, skipping VRAM check.")
    except Exception as e:
        print(f"VRAM check error: {e}, continuing.")


class STTServicer(stt_pb2_grpc.STTserviceServicer):
    def __init__(self):
        print(f"Loading {MODEL_NAME} from {MODEL_PATH} on {DEVICE} ({COMPUTE_TYPE})...")
        self.model = WhisperModel(
            MODEL_PATH,
            device=DEVICE,
            compute_type=COMPUTE_TYPE,
        )
        self._pending    = 0
        self._processing = False
        self._counter_lock   = threading.Lock()
        self._inference_lock = threading.Lock()
        print(f"Model {MODEL_NAME} ready.")

    def ConvertToText(self, request, context):
        with self._counter_lock:
            self._pending += 1

        self._inference_lock.acquire()
        with self._counter_lock:
            self._pending   -= 1
            self._processing = True

        try:
            text = self._transcribe(request.audio)
            return stt_pb2.STTresponse(text=text)
        except Exception as e:
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))
            return stt_pb2.STTresponse()
        finally:
            with self._counter_lock:
                self._processing = False
            self._inference_lock.release()

    def GetStatus(self, request, context):
        with self._counter_lock:
            return stt_pb2.StatusResponse(
                model_name=MODEL_NAME,
                queue_size=self._pending,
                is_processing=self._processing,
            )

    def _transcribe(self, audio_bytes: bytes) -> str:
        # Write to temp file so ffmpeg inside faster-whisper can auto-detect format
        with tempfile.NamedTemporaryFile(delete=False) as f:
            f.write(audio_bytes)
            tmp_path = f.name
        try:
            segments, _ = self.model.transcribe(
                tmp_path,
                beam_size=BEAM_SIZE,
                vad_filter=True,   # skip silence — faster on real speech
                language=None,     # auto-detect — needed for RU/EN code-switching
            )
            return "".join(seg.text for seg in segments)
        finally:
            os.unlink(tmp_path)


def serve():
    server = grpc.server(
        futures.ThreadPoolExecutor(max_workers=10),
        options=[
            ("grpc.max_receive_message_length", 100 * 1024 * 1024),
            ("grpc.max_send_message_length",    100 * 1024 * 1024),
        ],
    )
    stt_pb2_grpc.add_STTserviceServicer_to_server(STTServicer(), server)
    server.add_insecure_port(f"[::]:{PORT}")
    server.start()
    print(f"STT gRPC server listening on :{PORT}")
    server.wait_for_termination()


if __name__ == "__main__":
    check_vram()
    serve()
