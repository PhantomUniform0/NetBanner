#!/bin/sh
# NetBanner server pre-remove. Only on a real removal, not on upgrade.
#   deb: $1 = remove | upgrade | ...      rpm: $1 = number of versions left after this one (0 = removal)
# /var/lib/netbanner-server is kept on purpose: it holds the signing key every client has pinned. Delete it by hand
# only if no client will ever talk to this server again.
set -e

case "$1" in
    remove|purge|0) ;;
    *) exit 0 ;;
esac

if command -v systemctl >/dev/null 2>&1; then
    systemctl disable --now netbanner-server.service >/dev/null 2>&1 || true
fi
exit 0
