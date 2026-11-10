#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ ! -f .env.oci ]]; then
    echo "Run deployment/configure-oci.sh first." >&2
    exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
    echo "Docker Engine with the Compose v2 plugin is required." >&2
    exit 1
fi

docker compose --env-file .env.oci -f deployment/docker-compose.yml config --quiet
docker compose --env-file .env.oci -f deployment/docker-compose.yml up -d --build
