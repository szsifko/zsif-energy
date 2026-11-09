#!/usr/bin/env bash
# Installs or updates zsif-energy as systemd service. Run as root inside the LXC
# from the extracted release folder: ./install.sh
set -euo pipefail

APP_DIR=/opt/zsif-energy
CONF_DIR=/etc/zsif-energy
ENV_FILE="$CONF_DIR/zsif-energy.env"
SERVICE=zsif-energy
SRC_DIR="$(cd "$(dirname "$0")" && pwd)"

if [ "$(id -u)" -ne 0 ]; then
    echo "Please run as root." >&2
    exit 1
fi

apt-get update -qq
apt-get install -y -qq --no-install-recommends ca-certificates tzdata >/dev/null

if ! id "$SERVICE" >/dev/null 2>&1; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE"
fi

if systemctl is-active --quiet "$SERVICE"; then
    systemctl stop "$SERVICE"
fi

rm -rf "$APP_DIR"
install -d -m 755 "$APP_DIR"
cp -r "$SRC_DIR/app/." "$APP_DIR/"
chown -R root:root "$APP_DIR"
chmod 755 "$APP_DIR/Zsif.Energy.Worker"

install -m 644 "$SRC_DIR/zsif-energy.service" "/etc/systemd/system/$SERVICE.service"
systemctl daemon-reload
systemctl enable "$SERVICE" >/dev/null

install -d -m 700 "$CONF_DIR"
if [ ! -f "$ENV_FILE" ]; then
    install -m 600 "$SRC_DIR/zsif-energy.env.example" "$ENV_FILE"
    echo
    echo "First install: fill in $ENV_FILE, then run:"
    echo "  systemctl start $SERVICE"
    exit 0
fi

systemctl start "$SERVICE"
echo "$SERVICE installed and started. Logs: journalctl -u $SERVICE -f"
