#!/bin/sh
# NetBanner server pre-install: the service runs as its own system user.
set -e
if ! id netbanner-server >/dev/null 2>&1; then
    useradd --system --no-create-home --home-dir /var/lib/netbanner-server --shell /usr/sbin/nologin \
        --comment "NetBanner server" netbanner-server
fi
exit 0
