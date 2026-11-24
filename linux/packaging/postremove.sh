#!/bin/sh
# NetBanner post-remove: take the GNOME extension lock away with the package.
# /etc/netbanner/config.json is the admin's file and is left in place.
set -e

case "$1" in
    remove|purge|0) ;;
    *) exit 0 ;;
esac

# Our dconf files are gone now. Where local.d still exists (e.g. Rocky ships one), rebuilding drops our keys.
# Where we created it (Debian/Ubuntu), the package manager removed the empty folder, and "dconf update" would leave
# the old compiled database (with the lock) behind, so delete that ourselves.
if [ -d /etc/dconf/db/local.d ]; then
    if command -v dconf >/dev/null 2>&1; then
        dconf update || true
    fi
else
    rm -f /etc/dconf/db/local
fi

# The dconf profile, if we created it and nobody changed it since.
if [ -f /var/lib/netbanner/created-dconf-profile ]; then
    if [ "$(cat /etc/dconf/profile/user 2>/dev/null)" = "$(printf 'user-db:user\nsystem-db:local')" ]; then
        rm -f /etc/dconf/profile/user
        rmdir /etc/dconf/profile 2>/dev/null || true
    fi
    rm -rf /var/lib/netbanner
fi
# Server sync state (pinned key, cached config): a reinstall pins the server's key again.
rm -rf /var/lib/netbanner
exit 0
