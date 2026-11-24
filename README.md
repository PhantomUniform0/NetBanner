# NetBanner

A modern implmentation of the legacy NetBanner classification banner.
It shows a classification banner across the top of every monitor and can display readiness indicators (CPCON, FPCON,
and custom items) on the sides.

- Installers - **Windows** (MSI) and **Linux** (`.deb` / `.rpm`, GNOME and KDE): see [linux/README.md](linux/README.md)
- Managed by **Group Policy** (new ADMX), with **backward compatibility** for existing legacy NetBanner GPOs
- A **local config tool** for standalone or non-domain machines
- An optional **NetBanner server** (Linux) to manage non-domain Windows and Linux machines centrally from a web UI:
  see [linux/server/README.md](linux/server/README.md)
- .NET 10

**Status:** 3.2.0: Windows and Linux released.

## Building

Requires the .NET 10 SDK. Packages come from nuget.org (see `nuget.config`).

```powershell
./build.ps1        # tests, publishes NetBanner.exe + NetBannerConfig.exe, builds artifacts\NetBanner-<version>-x64.msi
./build.ps1 -Version X.X.X -SkipTests
./build-linux.ps1  # tests; Linux client and NetBanner server, each as .deb and .rpm, in artifacts\
```

The version comes from `<Version>` in `Directory.Build.props`. `build-linux.ps1` downloads a pinned nfpm release into `tools\.bin`.

## Installing

```powershell
msiexec /i NetBanner-3.2.0-x64.msi            # interactive
msiexec /i NetBanner-3.2.0-x64.msi /qn        # silent (SCCM / Intune / GPO software install)
msiexec /i NetBanner-3.2.0-x64.msi /qn REMOVELEGACYTASK=0 STARTBANNER=0
```

- Installs to `%ProgramFiles%\NetBanner` and creates the logon task `\NetBanner\NetBannerLogon`, which runs the banner for
  every user. By default it removes the legacy `\Microsoft\NetBanner\NetBannerLogon` task and starts the banner for users
  already logged on.
- Adds **NetBanner Configuration** to the Start menu.
- Installs the ADMX/ADML to `%WINDIR%\PolicyDefinitions` so local `gpedit.msc` shows **NetBanner 3** (skip with
  `INSTALLADMX=0`). A copy also goes to `%ProgramFiles%\NetBanner\PolicyDefinitions` for the domain central store.
- Deletes the legacy `Netbanner.admx`/`.adml` from `%WINDIR%\PolicyDefinitions` (keep them with `REMOVELEGACYADMX=0`).
- Moves legacy NetBanner settings in **local** Group Policy to the NetBanner 3 policies (backup kept; skip with
  `MIGRATELEGACYPOLICY=0`). Domain GPOs are not changed; migrate those in GPMC.
- Uninstalling keeps `%ProgramData%\NetBanner\config.json`.

## Configuring

Sources, highest precedence first:

1. **Group Policy (NetBanner 3):** copy `policy/NetBanner3.admx` to `\\<domain>\SYSVOL\<domain>\Policies\PolicyDefinitions\`
   and `policy/en-US/NetBanner3.adml` to its `en-US\` folder (or to `C:\Windows\PolicyDefinitions` for local policy).
   Settings are under *Computer Configuration → Administrative Templates → NetBanner 3*.
2. **Legacy Group Policy:** existing `Netbanner.admx` GPOs (`HKLM\SOFTWARE\Policies\Microsoft\NetBanner`) keep working.
3. **Local config file:** `%ProgramData%\NetBanner\config.json`. Changes apply live. Edit it with **`NetBannerConfig.exe`**
    Example:

```jsonc
{
  "classification": "SECRET",            // UNCLASSIFIED, CUI, CONFIDENTIAL, SECRET, TOP SECRET, TOP SECRET//SCI
  "caveats": "NOFORN",
  "leftItems":  [ { "type": "Cpcon", "value": 4 }, { "type": "Text", "value": "{hostname}" } ],
  "rightItems": [ { "type": "Text", "value": "IP {ip}", "background": "#000000", "foreground": "#FFFF00" },
                  { "type": "Fpcon", "value": "ALPHA" } ],
  "height": 14,
  "fontSize": 13,
  "fullScreenBehavior": "StayOnTop"      // or "Yield"
  // "custom": { "text": "EXERCISE", "background": "#FFFFFF", "foreground": "#000000" }  replaces the classification
}
```

Tokens in custom text: `{hostname}`, `{username}`, `{domain}`, `{ip}`.

By default the banner will show **CLASSIFICATION NOT CONFIGURED**

## Running it

The banner reads `HKLM\SOFTWARE\Policies\Microsoft\NetBanner` (legacy GPO) and updates live on `gpupdate`.
Switches:

| Switch | Effect |
|---|---|
| `--legacy-values "Classification=2;CaveatsEnabled=1;Caveats=NOFORN;InfoCon=3;FpCon=1"` | Use these values instead of the legacy registry key (implies `--no-policy`) |
| `--no-harden` | Allow standard users to end the process |
| `--exit-after <seconds>` | Quit automatically |
| `--log <file>` | Also write log messages to a file |
| `--config <file>` | Use this config file instead of `%ProgramData%\NetBanner\config.json` |
| `--no-policy` | Ignore both GPO registry keys (test a config file alone) |
