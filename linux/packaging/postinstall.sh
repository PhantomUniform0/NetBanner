#!/bin/sh
# NetBanner post-install (deb and rpm). Must be safe to run again on upgrade.
set -e

EXT_DIR=/usr/share/gnome-shell/extensions/netbanner@netbanner
SRC=/usr/share/netbanner/gnome-shell

# GNOME: link the extension flavour that matches the installed GNOME Shell (40-44 legacy, 45+ ES modules).
if command -v gnome-shell >/dev/null 2>&1; then
    major=$(gnome-shell --version 2>/dev/null | sed -n 's/^GNOME Shell \([0-9][0-9]*\).*/\1/p')
    flavour=esm
    if [ -n "$major" ] && [ "$major" -lt 45 ]; then
        flavour=legacy
    fi
    mkdir -p /usr/share/gnome-shell/extensions
    rm -rf "$EXT_DIR"
    ln -s "$SRC/$flavour" "$EXT_DIR"
fi

# GNOME: system dconf database that force-enables the extension. Debian/Ubuntu ship no user profile by default.
if [ ! -f /etc/dconf/profile/user ]; then
    mkdir -p /etc/dconf/profile /var/lib/netbanner
    printf 'user-db:user\nsystem-db:local\n' > /etc/dconf/profile/user
    touch /var/lib/netbanner/created-dconf-profile   # so removal can take it away again
elif ! grep -q '^system-db:local$' /etc/dconf/profile/user; then
    printf 'system-db:local\n' >> /etc/dconf/profile/user
fi
if command -v dconf >/dev/null 2>&1; then
    dconf update || true
fi

# KDE and other X11 desktops: start the banner with every graphical session.
if command -v systemctl >/dev/null 2>&1; then
    systemctl --global enable netbanner.service >/dev/null 2>&1 || true
    # Start (or, on upgrade, restart) the banner for users who are already logged on, like the Windows MSI does.
    # On GNOME the unit's ExecCondition makes this a no-op; there the extension appears at the next login.
    for u in $(loginctl list-users --no-legend 2>/dev/null | awk '{print $2}'); do
        systemctl --user -M "$u@" daemon-reload >/dev/null 2>&1 || true
        systemctl --user -M "$u@" restart netbanner.service >/dev/null 2>&1 || true
    done
fi

mkdir -p /etc/netbanner /var/lib/netbanner
chmod 755 /etc/netbanner /var/lib/netbanner

# NetBanner server sync (root). Restarted on upgrade so it runs the new version.
if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload >/dev/null 2>&1 || true
    systemctl enable netbanner-sync.service >/dev/null 2>&1 || true
    systemctl restart netbanner-sync.service >/dev/null 2>&1 || true
fi
exit 0
