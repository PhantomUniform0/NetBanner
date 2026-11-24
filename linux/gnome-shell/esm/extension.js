// NetBanner for GNOME Shell 45 and later (ES modules). banner.js is generated from ../banner-core.js at build time.
import St from 'gi://St';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Clutter from 'gi://Clutter';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import { Extension } from 'resource:///org/gnome/shell/extensions/extension.js';
import { createBanner } from './banner.js';

export default class NetBannerExtension extends Extension {
    enable() {
        this._controller = createBanner({ Main, St, Gio, GLib, Clutter });
        this._controller.enable();
    }

    disable() {
        this._controller?.disable();
        this._controller = null;
    }
}
