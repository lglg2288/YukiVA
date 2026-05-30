FROM nvidia/cuda:12.1.1-cudnn8-runtime-ubuntu22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV PYTHONUNBUFFERED=1

RUN apt-get update && apt-get install -y \
    python3.11 \
    python3-pip \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip3 install --no-cache-dir -r requirements.txt

# Generate gRPC stubs at build time — no generated files in the repo
COPY stt.proto .
RUN python3 -m grpc_tools.protoc \
    -I. \
    --python_out=. \
    --grpc_python_out=. \
    stt.proto

COPY server.py .

# Bake the model into the image so it works offline after docker save/load
ARG MODEL_NAME=large-v3
COPY ./models/${MODEL_NAME} /models/${MODEL_NAME}

ENV MODEL_NAME=${MODEL_NAME}
ENV DEVICE=cuda
ENV COMPUTE_TYPE=float16
ENV BEAM_SIZE=5
ENV PORT=50051

EXPOSE 50051

CMD ["python3", "server.py"]
