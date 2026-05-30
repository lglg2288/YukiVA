FROM pytorch/pytorch:2.2.0-cuda12.1-cudnn8-runtime

RUN apt-get update && \
    apt-get install -y --no-install-recommends \
    build-essential \
    gcc \
    g++ \
    libsndfile1 \
    git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app/

RUN pip install --no-cache-dir --upgrade pip setuptools wheel

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
RUN pip install --no-cache-dir TTS==0.22.0

COPY ./models/tts_models--multilingual--multi-dataset--xtts_v2 /root/.local/share/tts/tts_models--multilingual--multi-dataset--xtts_v2
COPY . .

RUN python -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. tts.proto

CMD ["python", "server.py"]