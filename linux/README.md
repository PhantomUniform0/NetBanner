# NetBanner for Linux

Classification banner at the top of every monitor.

Supported: Debian 12/13, Ubuntu 24.04/26.04, Rocky Linux 9/10, x86_64, with GNOME or KDE Plasma
(Wayland or X11).

## Install

```sh
sudo apt install ./netbanner_3.2.1-1_amd64.deb      # Debian / Ubuntu
sudo dnf install ./netbanner-3.2.1-1.x86_64.rpm     # Rocky
```

On KDE the banner appears right away for users who are logged on.
On GNOME it appears at the next login, because GNOME only loads new extensions then.

## Configure

Create `/etc/netbanner/config.json` (root-owned, mode 644). Same format as the Windows `config.json`; see
`/usr/share/doc/netbanner/config.example.json`. Changes apply within a second, no restart or re-login.

```sh
sudo cp /usr/share/doc/netbanner/config.example.json /etc/netbanner/config.json
sudo nano /etc/netbanner/config.json
netbanner validate      # shows the resulting banner and any problems
```

The default banner will show **CLASSIFICATION NOT CONFIGURED** (black on white)

## Central management (optional)

To manage machines from a NetBanner server instead of editing each `config.json`, add the server to the config:

```json
{ "server": { "url": "http://netbanner-server:8080" }, "classification": "UNCLASSIFIED" }
```

The root service `netbanner-sync` fetches this machine's signed config every 60 seconds.
Server setup: see the NetBanner server README.

## How it works

| Desktop | How the banner is drawn |
|---|---|
| GNOME (40+) | GNOME Shell extension `netbanner@netbanner`, enabled for all users and locked with dconf. On the main monitor it sits above the top bar; also shown on the lock screen. |
| KDE Plasma | `netbanner banner`, an X11 dock window with a reserved strip (runs through XWayland on Wayland). Started by the systemd user unit `netbanner.service` and restarted if killed. |

## Notes

- The package locks GNOME's list of enabled extensions to NetBanner only (distro extensions such as Ubuntu's dock are
  enabled separately and keep working). To allow other extensions, create `/etc/dconf/db/local.d/99-site`:
  ```ini
  [org/gnome/shell]
  enabled-extensions=['netbanner@netbanner', 'other@extension']
  ```
  then run `sudo dconf update`. Don't edit `00-netbanner`; the package replaces it on upgrade.
- After a GNOME major-version upgrade across 44 → 45 (e.g. Debian 12 → 13), reinstall the package so the right
  extension flavour is linked.
- Removing the package keeps `/etc/netbanner/config.json`.
