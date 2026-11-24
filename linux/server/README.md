# NetBanner server

Central banner configuration for intended for machines without Group Policy.
You set a **default**, **groups** and **per-machine** overrides in a web UI; each machine fetches its own signed
config every 60 seconds.
The server is Linux only (Debian 12/13, Ubuntu 24.04/26.04, Rocky 9/10, x86_64).

## Install

```sh
sudo apt install ./netbanner-server_3.2.0-1_amd64.deb      # Debian / Ubuntu
sudo dnf install ./netbanner-server-3.2.0-1.x86_64.rpm     # Rocky
sudo -u netbanner-server netbanner-server set-password     # web UI admin password (8+ characters)
```

Open `http://<server>:8080` and sign in.

## Point machines at it

On each machine, set the server in the local config:

- **Windows:** NetBanner Configuration → *NetBanner server* → `http://<server>:8080` → Save.
- **Linux:** add `"server": { "url": "http://<server>:8080" }` to `/etc/netbanner/config.json`.

## The signing key

The server signs every config with its own key (created on first start). Each machine trusts the key it sees on first
contact, similar to a SSH host key, and from then on accepts only configs signed with it.

- Fingerprint: shown in the web UI, or `sudo -u netbanner-server netbanner-server fingerprint`.
- **Back up `/var/lib/netbanner-server`** (data, key, admin password). Uninstalling the package keeps it.
- If the key ever changes (new server, lost data), machines keep their last banner and refuse the new key until an
  administrator clears the pinned key on each one: NetBanner Configuration → *Forget server key* (Windows), or
  `sudo netbanner forget-server-key` (Linux).

## Operations

| | |
|---|---|
| Service | `systemctl status netbanner-server`, logs: `journalctl -u netbanner-server` |
| Port | 8080. To change: `sudo systemctl edit netbanner-server`, add `[Service]` / `Environment=ASPNETCORE_URLS=http://0.0.0.0:9000` |
| Data | `/var/lib/netbanner-server/data.json` (can be edited directly; the server picks it up) |
| Transport | Plain HTTP. Configs are signed, so they can't be altered in transit, but they can be read on the network. |
