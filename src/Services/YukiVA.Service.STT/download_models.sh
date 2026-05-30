#!/bin/bash
set -e

pip install -q huggingface_hub

echo "Downloading large-v3..."
huggingface-cli download Systran/faster-whisper-large-v3 \
    --local-dir ./models/large-v3 \
    --local-dir-use-symlinks False

echo "Downloading large-v3-turbo..."
huggingface-cli download dropbox-dash/faster-whisper-large-v3-turbo \
    --local-dir ./models/large-v3-turbo \
    --local-dir-use-symlinks False

echo "Downloading large-v3-turbo-russian..."
huggingface-cli download dvislobokov/faster-whisper-large-v3-turbo-russian \
    --local-dir ./models/large-v3-turbo-russian \
    --local-dir-use-symlinks False

echo "Done. Models saved to ./models/"
