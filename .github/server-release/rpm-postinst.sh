#!/bin/bash
set -e

# Create system group and user for the service
if ! getent group hyperfi-server > /dev/null 2>&1; then
    groupadd --system hyperfi-server
fi
if ! id -u hyperfi-server > /dev/null 2>&1; then
    useradd --system --no-create-home --shell /sbin/nologin \
        --gid hyperfi-server hyperfi-server
fi

# Ensure correct permissions
chmod 755 /usr/lib/hyperfi-server/HyperFi.Server
chown -R root:root /usr/lib/hyperfi-server
chown -R hyperfi-server:hyperfi-server /etc/hyperfi-server

# Data directory for the service (SQLite database, caches, downloaded artwork).
# The service user has no writable home, so the server is pointed here via
# HYPERFI_DATA_DIR in the systemd unit; create it owned by the service user.
install -d -o hyperfi-server -g hyperfi-server /var/lib/hyperfi-server

systemctl daemon-reload
systemctl enable --now hyperfi-server-updater.path
