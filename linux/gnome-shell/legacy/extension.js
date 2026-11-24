// NetBanner for GNOME Shell 40-44 (legacy imports). banner.js is copied from ../banner-core.js at build time.
const ExtensionUtils = imports.misc.extensionUtils;
const Me = ExtensionUtils.getCurrentExtension();
const Main = imports.ui.main;
const { St, Gio, GLib, Clutter } = imports.gi;

let controller = null;

function init() {
}

function enable() {
    controller = Me.imports.banner.createBanner({ Main, St, Gio, GLib, Clutter });
    controller.enable();
}

function disable() {
    if (controller) {
        controller.disable();
        controller = null;
    }
}
