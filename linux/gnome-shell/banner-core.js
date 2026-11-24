// NetBanner GNOME Shell extension: shared logic for GNOME 40-44 (legacy imports) and 45+ (ES modules).
// Plain script on purpose: the build appends an "export" for the 45+ flavour; the legacy importer exposes
// top-level functions as-is. Keep it free of import/export statements and class fields (GNOME 40's GJS).
//
// The banner text and colours come from "netbanner render" (the same .NET code as on Windows), so precedence,
// validation and the CLASSIFICATION NOT CONFIGURED default live in one place. If netbanner can't be run, the
// banner shows CLASSIFICATION NOT CONFIGURED rather than nothing.

function createBanner(deps) {
    const { Main, St, Gio, GLib, Clutter } = deps;

    const RENDER_COMMAND = ['/usr/bin/netbanner', 'render'];
    // The local config, and the server config written by root's sync service.
    const WATCH_DIRS = ['/etc/netbanner', '/var/lib/netbanner'];
    const FALLBACK = {
        text: 'CLASSIFICATION NOT CONFIGURED', background: '#FFFFFF', foreground: '#000000',
        left: [], right: [], height: 16, fontSize: 13, fullScreenBehavior: 'StayOnTop',
    };

    const safeColor = (value, fallback) => /^#[0-9A-Fa-f]{6}$/.test(value || '') ? value : fallback;
    const px = n => `${Math.max(1, Math.round(Number(n) || 0))}px`;

    class Controller {
        constructor() {
            this._data = null;
            this._actors = [];
            this._signals = [];
            this._refreshId = 0;
            this._dirMonitors = [];
            this._running = false;
        }

        enable() {
            this._running = true;
            this._connect(Main.layoutManager, 'monitors-changed', () => this._rebuild());

            for (const dir of WATCH_DIRS) {
                try {
                    const monitor = Gio.File.new_for_path(dir).monitor_directory(Gio.FileMonitorFlags.NONE, null);
                    this._dirMonitors.push(monitor);
                    this._connect(monitor, 'changed', () => this._scheduleRefresh());
                } catch (e) {
                    logError(e, 'NetBanner: cannot watch ' + dir);
                }
            }

            // {ip} and similar tokens can change with the network.
            this._connect(Gio.NetworkMonitor.get_default(), 'network-changed', () => this._scheduleRefresh());
            this._refresh();
        }

        disable() {
            this._running = false;
            if (this._refreshId) {
                GLib.source_remove(this._refreshId);
                this._refreshId = 0;
            }

            for (const [obj, id] of this._signals) {
                obj.disconnect(id);
            }

            this._signals = [];
            for (const monitor of this._dirMonitors) {
                monitor.cancel();
            }

            this._dirMonitors = [];

            this._destroyActors();
        }

        _connect(obj, signal, handler) {
            this._signals.push([obj, obj.connect(signal, handler)]);
        }

        _scheduleRefresh() {
            if (this._refreshId) {
                GLib.source_remove(this._refreshId);
            }

            // Editors write files in several steps; wait for them to finish.
            this._refreshId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 500, () => {
                this._refreshId = 0;
                this._refresh();
                return GLib.SOURCE_REMOVE;
            });
        }

        _refresh() {
            let proc;
            try {
                proc = Gio.Subprocess.new(RENDER_COMMAND,
                    Gio.SubprocessFlags.STDOUT_PIPE | Gio.SubprocessFlags.STDERR_SILENCE);
            } catch (e) {
                logError(e, 'NetBanner: cannot run ' + RENDER_COMMAND[0]);
                this._show(FALLBACK);
                return;
            }

            proc.communicate_utf8_async(null, null, (p, res) => {
                let data = FALLBACK;
                try {
                    const [, stdout] = p.communicate_utf8_finish(res);
                    data = JSON.parse(stdout);
                } catch (e) {
                    logError(e, 'NetBanner: bad output from netbanner render');
                }

                if (this._running) {
                    this._show(data);
                }
            });
        }

        _show(data) {
            this._data = data;
            this._rebuild();
        }

        _destroyActors() {
            for (const actor of this._actors) {
                actor.destroy(); // also removes it from the panel box / chrome
            }

            this._actors = [];
        }

        _rebuild() {
            this._destroyActors();
            if (!this._data) {
                return;
            }

            const lm = Main.layoutManager;
            const yieldToFullScreen = this._data.fullScreenBehavior === 'Yield';
            for (const monitor of lm.monitors) {
                const actor = this._buildBanner(monitor);
                this._actors.push(actor);
                if (monitor.index === lm.primaryIndex) {
                    // On the primary monitor the banner goes above the top bar inside the panel box, which already
                    // reserves screen space; the top bar moves down and maximized windows stop below both.
                    lm.panelBox.insert_child_at_index(actor, 0);
                } else {
                    lm.addChrome(actor, { affectsStruts: true, trackFullscreen: yieldToFullScreen });
                    actor.set_position(monitor.x, monitor.y);
                }
            }
        }

        _buildBanner(monitor) {
            const d = this._data;
            const fg = safeColor(d.foreground, '#000000');
            const height = px(d.height || 16);
            const font = px(d.fontSize || 13);

            const banner = new St.Widget({
                name: 'netbanner',
                layout_manager: new Clutter.BinLayout(),
                style: `background-color: ${safeColor(d.background, '#FFFFFF')}; height: ${height};`,
                reactive: false,
                clip_to_allocation: true,
            });
            banner.set_width(monitor.width);

            const labels = [];
            const center = new St.Label({
                text: d.text || FALLBACK.text,
                style: `color: ${fg}; font-size: ${font}; font-weight: bold;`,
                x_align: Clutter.ActorAlign.CENTER,
                y_align: Clutter.ActorAlign.START,
                x_expand: true,
                y_expand: true,
            });
            labels.push(center);
            banner.add_child(center);
            banner.add_child(this._side(d.left || [], Clutter.ActorAlign.START, fg, font, labels));
            banner.add_child(this._side(d.right || [], Clutter.ActorAlign.END, fg, font, labels));

            // The text's line box (~17px for 13px text) is taller than the 14px strip. GNOME would pin it to the
            // top and let it hang into the top bar, so centre each label's text vertically on the strip instead.
            banner.connect('notify::allocation', () => {
                const stripHeight = banner.height;
                for (const label of labels) {
                    const [, natural] = label.get_preferred_height(-1);
                    label.translation_y = Math.floor((stripHeight - natural) / 2);
                }
            });
            return banner;
        }

        _side(items, align, defaultFg, font, labels) {
            const box = new St.BoxLayout({ x_align: align, y_align: Clutter.ActorAlign.FILL, x_expand: true, y_expand: true });
            for (const item of items) {
                const bg = item.background ? `background-color: ${safeColor(item.background, '#000000')};` : '';
                const fg = safeColor(item.foreground, defaultFg);
                const cell = new St.Bin({ style: `${bg} padding: 0 10px;`, y_expand: true });
                const label = new St.Label({
                    text: item.text || '',
                    style: `color: ${fg}; font-size: ${font}; font-weight: 600;`,
                    y_align: Clutter.ActorAlign.START,
                });
                labels.push(label);
                cell.set_child(label);
                box.add_child(cell);
            }

            return box;
        }
    }

    return new Controller();
}
