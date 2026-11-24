# Vendored front-end files

Copied unmodified from npm, so the web UI works without internet access. The server embeds and serves them under
`/vendor/`. Update deliberately: download the new tarball, check its integrity against the registry, replace the files
and update this table.

| Files | Package | Integrity (npm `dist.integrity`) | License |
|---|---|---|---|
| `clarity/clr-ui.min.css` | `@clr/ui` 18.4.0 (Clarity Design System CSS, https://clarity.design, source https://github.com/vmware-clarity/ng-clarity) | `sha512-cR9L5reAbUWIoXacUEXzlsvZCQBFvfFvS8jXEkgIpq7jfwi4hVy6Y6hPO6GuNmecNXHOY+cTr4e1QD8Be27p+w==` | MIT (declared in the package's `package.json`; the package ships no license file, see the source repository) |
| `clarity-city/ClarityCity-{Regular,Medium,SemiBold,Bold}.woff2` | `@cds/city` 1.1.0 (Clarity City font) | `sha512-S9K+Q39BGOghyLHmR0Wdcmu1i1noSUk8HcvMj+3IaohZw02WFd99aPTQDHJeseXrXZP3CNovaSlePI0R11NcFg==` | SIL OFL 1.1 (`clarity-city/LICENSE`) |

`clarity-city/fonts.css` is ours: `@font-face` rules for the four weights above.
