Ready-to-run builds for **Windows, macOS, Linux and Android**. No compiler needed — download the file for your platform below.

## ⚠️ Read this first

**These builds have never made a live API call.** They were written and compiled in a sandbox with no network access to the weather services. All four platforms compile cleanly and package correctly, and every parser is written to degrade rather than throw on unexpected data — but no forecast has actually been fetched, and the endpoint shapes come from documentation rather than from observed responses. The first real run is the real test.

That is why these are `0.x` versions and not `1.0.0`.

**0.1.1 fixes an Android launch crash** found on a Pixel Fold running Android 17: every button was constructing a mouse cursor, which a touch platform has no factory for, and the font resolver was asking Android for DejaVu Sans — a family that only exists on Linux. Android builds now also capture unhandled exceptions to `crash.log` in the app's files directory and show the reason on screen instead of closing silently.

If something looks wrong, the most likely culprits are the SPC hail integration (it discovers ArcGIS layer ids by name at runtime) and any NOAA product path that has moved. Every upstream URL is editable in `endpoints.json` next to your settings, so a moved path is a text edit rather than a rebuild.

---

## Windows 7 SP1 → 11

**`WindowsWeather-<version>-windows.zip`**

Unzip and run `WindowsWeather.exe`. Keep `WindowsWeather.exe.config` beside it.

- **Windows 10 / 11** — nothing else needed.
- **Windows 8 / 8.1 / 7 SP1** — install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48) first. On Windows 7 also make sure TLS 1.2 is enabled ([KB3140245](https://support.microsoft.com/kb/3140245) and its Easy Fix), or every request will fail — all four data sources require it.

## macOS 14 (Sonoma) or later, Apple silicon

**`WindowsWeather-<version>-macos-arm64.zip`**

Unzip and drag `Windows Weather.app` to Applications. The bundle is only **ad-hoc signed**, so Gatekeeper will refuse it on first launch. Either right-click the app and choose **Open**, or clear the quarantine flag:

```bash
xattr -dr com.apple.quarantine "/Applications/Windows Weather.app"
```

Proper distribution needs an Apple Developer ID certificate and notarisation; the steps are in the README.

Intel Macs: build from source with `./packaging/macos/build-app.sh --arch osx-x64`.

## Linux x64

**`WindowsWeather-<version>-linux-x64.tar.gz`** — self-contained, carries its own .NET runtime.

```bash
tar xzf WindowsWeather-*-linux-x64.tar.gz
./linux-x64/WindowsWeather
```

Needs the usual desktop libraries (`libX11`, `libICE`, `libSM`, `fontconfig`). Any normal desktop install already has them; a minimal container may not. An AppImage is attached when the build could produce one.

## Android 6.0 or later

**`WindowsWeather-<version>-android.apk`** — arm64 and 32-bit ARM. Sideload it; you will need to allow installation from unknown sources.

It asks for **one permission: internet access**. There is deliberately no location permission — you choose your places explicitly rather than being geolocated.

**It is signed with the Android debug key.** That is what makes it installable without this repository holding a private signing key, and it has two consequences worth knowing:

- It cannot be published to the Play Store.
- CI generates a fresh debug key on each run, so a future release will carry a *different* signature. **Uninstall this version before installing a newer one**, or Android will reject the upgrade. Putting a real keystore into repository secrets would fix that permanently.

---

## What the app shows

- **Now** — conditions plus the next 24 hours as a combined temperature and precipitation-chance chart, coloured by precipitation type
- **10-Day Forecast** — highs and lows as bars against the whole period's range, keeping the forecast office's own narrative
- **Precipitation** — rain, snow, freezing rain, thunderstorms and **hail**, separated out per day
- **Radar** — NWS RIDGE II imagery you can pause and scrub through
- **Jet Stream** — a 250/300 hPa wind map rendered from gridded model data, not a scraped chart image
- **Alerts** — every active watch, warning and advisory, with the complete product text

Four regions ship configured: PNW Coast & Hawaii, East Atlantic Coast, the contiguous US, and Alaska.

**A dash means a value was never issued, not that the chance is zero.** SPC hail probabilities stop at Day 3.

## Data sources

All free, public and keyless: [api.weather.gov](https://www.weather.gov/documentation/services-web-api) (forecast, forecast grid, alerts), [Open-Meteo](https://open-meteo.com/) (days 8–10, precipitation types, CAPE, upper-air winds), the [SPC convective outlook](https://www.spc.noaa.gov/products/outlook/) (hail and severe probabilities), and [NWS RIDGE II](https://radar.weather.gov/) (radar). No account, no API key, no telemetry, and no server in the middle — every install talks to NOAA directly.

**In dangerous weather, follow official NWS guidance and local emergency management. Do not rely on this or any third-party app as your only source of warnings.**
