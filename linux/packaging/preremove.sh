#!/bin/sh
# NetBanner pre-remove. Only clean up on a real removal, not on upgrade.
#   deb: $1 = remove | upgrade | ...      rpm: $1 = number of versions left after this one (0 = removal)
set -e

case "$1" in
    remove|purge|0) ;;
    *) exit 0 ;;
esac

if command -v systemctl >/dev/null 2>&1; then
    systemctl --global disable netbanner.service >/dev/null 2>&1 || true
    systemctl disable --now netbanner-sync.service >/dev/null 2>&1 || true
    # Stop running banners in logged-on sessions (otherwise they stay until logout).
    for u in $(loginctl list-users --no-legend 2>/dev/null | awk '{print $2}'); do
        systemctl --user -M "$u@" stop netbanner.service >/dev/null 2>&1 || true
    done
fi
rm -f /usr/share/gnome-shell/extensions/netbanner@netbanner
exit 0
