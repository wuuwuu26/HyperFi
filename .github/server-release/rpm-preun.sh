#!/bin/bash
# $1 = 0 means uninstall; $1 = 1 means upgrade
if [ "$1" -eq 0 ]; then
    systemctl stop hyperfi-server 2>/dev/null || true
    systemctl disable hyperfi-server 2>/dev/null || true
    systemctl disable --now hyperfi-server-updater.path 2>/dev/null || true
fi
