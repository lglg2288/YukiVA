#!/bin/bash
# Usage: bash download_models.sh [large-v3] [large-v3-turbo] [large-v3-turbo-russian]
# Without arguments downloads all three.
set -e

declare -A REPOS=(
    [large-v3]="Systran/faster-whisper-large-v3"
    [large-v3-turbo]="dropbox-dash/faster-whisper-large-v3-turbo"
    [large-v3-turbo-russian]="dvislobokov/faster-whisper-large-v3-turbo-russian"
)

MODELS=("$@")
if [ ${#MODELS[@]} -eq 0 ]; then
    MODELS=(large-v3 large-v3-turbo large-v3-turbo-russian)
fi

pip install -q huggingface_hub

for model in "${MODELS[@]}"; do
    repo="${REPOS[$model]}"
    if [ -z "$repo" ]; then
        echo "Unknown model '$model'. Available: ${!REPOS[*]}" >&2
        exit 1
    fi

    echo "Downloading $model..."
    huggingface-cli download "$repo" \
        --local-dir "./models/$model" \
        --local-dir-use-symlinks False
done

echo "Done. Models saved to ./models/"
