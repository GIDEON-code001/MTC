#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [[ -e .env.oci ]]; then
    echo ".env.oci already exists; refusing to overwrite deployment secrets." >&2
    exit 1
fi

if ! command -v openssl >/dev/null 2>&1; then
    echo "openssl is required to generate deployment secrets." >&2
    exit 1
fi

read -r -p "Public domain name (DNS A record must point to this VM): " APP_DOMAIN
if [[ ! "$APP_DOMAIN" =~ ^([A-Za-z0-9-]+\.)+[A-Za-z]{2,}$ ]]; then
    echo "Enter a valid public domain name without a scheme or path." >&2
    exit 1
fi

read -r -p "Email address for HTTPS certificate notices: " ACME_EMAIL
if [[ ! "$ACME_EMAIL" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]]; then
    echo "Enter a valid email address." >&2
    exit 1
fi

read -r -p "Bootstrap administrator email: " BOOTSTRAP_EMAIL
if [[ ! "$BOOTSTRAP_EMAIL" =~ ^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$ ]]; then
    echo "Enter a valid administrator email address." >&2
    exit 1
fi

read -r -s -p "Bootstrap administrator password (12+ letters/numbers): " BOOTSTRAP_PASSWORD
printf '\n'
if [[ ${#BOOTSTRAP_PASSWORD} -lt 12 || ! "$BOOTSTRAP_PASSWORD" =~ ^[A-Za-z0-9]+$ ]]; then
    echo "Use a password of at least 12 alphanumeric characters." >&2
    exit 1
fi

POSTGRES_PASSWORD="$(openssl rand -hex 32)"
JWT_SIGNING_KEY="$(openssl rand -hex 32)"
umask 077
printf '%s\n' \
    "APP_DOMAIN=$APP_DOMAIN" \
    "ACME_EMAIL=$ACME_EMAIL" \
    "POSTGRES_DB=bodaboss" \
    "POSTGRES_USER=bodaboss" \
    "POSTGRES_PASSWORD=$POSTGRES_PASSWORD" \
    "JWT_SIGNING_KEY=$JWT_SIGNING_KEY" \
    "BOOTSTRAP_EMAIL=$BOOTSTRAP_EMAIL" \
    "BOOTSTRAP_PASSWORD=$BOOTSTRAP_PASSWORD" > .env.oci
chmod 600 .env.oci
unset POSTGRES_PASSWORD JWT_SIGNING_KEY BOOTSTRAP_PASSWORD

echo "Created .env.oci with restrictive permissions. Keep it private and out of Git."
