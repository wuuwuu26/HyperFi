#!/bin/bash
set -e

case "$1" in
    remove|purge)
        systemctl stop hyperfi-server 2>/dev/null || true
        systemctl disable hyperfi-server 2>/dev/null || true
        systemctl disable --now hyperfi-server-updater.path 2>/dev/null || true
        ;;
esac
