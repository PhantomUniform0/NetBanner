#!/bin/sh
# NetBanner server post-install (deb and rpm). Safe to run again on upgrade.
set -e

if command -v systemctl >/dev/null 2>&1; then
    systemctl daemon-reload >/dev/null 2>&1 || true
    systemctl enable netbanner-server.service >/dev/null 2>&1 || true
    # First start creates /var/lib/netbanner-server and the signing key; on upgrade this runs the new version.
    systemctl restart netbanner-server.service >/dev/null 2>&1 || true
fi

if [ ! -f /var/lib/netbanner-server/admin.json ]; then
    cat <<'EOF'

NetBanner server installed and running on port 8080.
Set the web UI admin password:
    sudo -u netbanner-server netbanner-server set-password
Then open http://<this-server>:8080 . Open the port if a firewall is active, e.g.
    sudo firewall-cmd --permanent --add-port=8080/tcp && sudo firewall-cmd --reload     (Rocky)
    sudo ufw allow 8080/tcp                                                             (Ubuntu with ufw)
Back up /var/lib/netbanner-server: it holds the signing key every client pins.
See /usr/share/doc/netbanner-server/README.md

EOF
fi
exit 0
