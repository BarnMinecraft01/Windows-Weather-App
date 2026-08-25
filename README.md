# Windows Weather

A weather app for **Windows 7 SP1 → 11**, **macOS**, **Linux** and **Android**
that shows the things most weather apps stopped showing: the jet stream, animated radar you can
pause, precipitation split by type, a ten-day forecast with the forecast office's
own wording, and the full text of every active NWS alert.

No account, no API key, no advertising, no telemetry.

---

## What it shows

| Tab | What is in it |
|---|---|
| **Now** | Current temperature, feels-like, wind and gusts, humidity, pressure, visibility, cloud cover, sunrise/sunset, UV — plus the next 24 hours as a combined temperature line and precipitation-chance chart, colour-coded by type. |
| **10-Day Forecast** | Ten days with highs and lows drawn as bars against the whole period's range, so a cold snap is a shape rather than something you find by comparing numbers. Selecting a day shows the NWS narrative for it. |
| **Precipitation** | Chance of **rain**, **snow**, **freezing rain**, **thunderstorms** and **hail**, day by day, with expected amounts, the SPC categorical severe risk, and peak instability (CAPE). |
| **Radar** | NWS RIDGE II imagery for the region's sectors and for the NEXRAD site nearest your location. Ten frames with play/pause and a scrub bar. |
| **Jet Stream** | A 250 hPa or 300 hPa wind map rendered from gridded model data for the selected region, at Now / +6h / +12h / +24h / +48h, with the jet core called out. |
| **Alerts** | Every active watch, warning and advisory, sorted so the dangerous ones are first, with the complete product text including the call-to-action paragraph. |

### Regions

Four regions ship configured, each with its own radar sectors, alert areas and
jet stream box:

- **PNW Coast & Hawaii** — Pacific Northwest and Pacific Southwest sectors plus
  Hawaii; the jet stream box spans the North Pacific storm track that feeds both
- **East Atlantic Coast** — Northeast and Southeast sectors, Maine through Florida
- **Contiguous US** — the lower 48
- **Alaska**

You can add any location by town, ZIP code, or a raw `latitude, longitude` pair,
and alerts can be scoped to just that point or to the whole region.

---

## Repository layout

The app is one shared engine with three UI heads. No mainstream stack covers
Windows 7 *and* everything else from a single binary — .NET 5+ dropped Windows 7,
and Avalonia 12 dropped .NET Framework — so the platform-specific part is the UI
and nothing else.

```
src/WeatherApp.Core/            the engine: Json, Net, Configuration, Models,
                                Services, Platform. No UI, no OS dependencies.
src/WeatherApp.AvaloniaShared/  palette, widgets and the two renderers the
                                Avalonia heads must not diverge on
src/WeatherApp.WinForms/        .NET Framework 4.6.2 + WinForms  → Windows 7–11
src/WeatherApp.Desktop/         .NET 10 + Avalonia 12            → macOS, Linux
src/WeatherApp.Android/         .NET 10 + Avalonia 12            → Android 6+
packaging/                      icon generator, .app and Linux build scripts
```

| Platform | Head | Packaging |
|---|---|---|
| Windows 7 / 8 / 8.1 / 10 / 11 | WinForms | single `.exe`, no installer |
| macOS 14+ (Intel & Apple silicon) | Avalonia Desktop | `.app` bundle, per-arch or universal |
| Linux (x64, arm64) | Avalonia Desktop | self-contained tarball, AppImage |
|  Android 6.0+ | Avalonia Android | APK |

There is deliberately **no browser build** — see
[docs/why-no-browser-build.md](docs/why-no-browser-build.md). It is not a gap
waiting to be filled; `api.weather.gov` sends no CORS headers, so a WebAssembly
build could not fetch a single weather alert.

The core is **shared by source glob, not as a `netstandard2.0` assembly.** .NET
Framework 4.6.2 is missing around 200 of the APIs `netstandard2.0` promises and
needs binding redirects plus NuGet packages to bridge them, which would cost the
Windows head its "builds from a bare checkout with nothing but MSBuild" property.

Roughly 4,300 lines of engine are shared verbatim by all three heads. Adding a
platform means adding a head, not forking the engine.

The Android head is **not** a port of the desktop one. A nine-column
precipitation table and a six-tab layout do not survive a narrow screen, so the
phone has four bottom-bar destinations and the precipitation breakdown lives
inside expandable forecast rows. What the two Avalonia heads *do* share is the
drawing that must not diverge — the jet stream map and the hourly chart are
rendered by the same code on both.

---

## Installing

### Windows

| Windows version | What you need |
|---|---|
| **11**, **10** (1607+) | Nothing. .NET Framework 4.6.2 or later is already present. |
| **8.1**, **8** | Install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48). |
| **7 SP1** | Install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48), and make sure TLS 1.2 is enabled — see [Windows 7 notes](#windows-7-notes). |

Download `WindowsWeather.exe` and `WindowsWeather.exe.config` from the build
artifacts, put both in a folder, and run it. No installer.

### macOS

Requires **macOS 14 (Sonoma) or later** — that is
[Microsoft's supported floor for .NET 10](https://learn.microsoft.com/dotnet/core/install/macos).
The bundle declares a minimum of 13.0 so older systems can at least try; that is
untested. Both Apple silicon and Intel are supported.

The published `.app` is **self-contained** — it carries its own .NET runtime, so
nothing needs installing first.

Because the CI build is only *ad-hoc signed*, Gatekeeper will refuse it on a
machine other than the one that built it. To run it anyway, either right-click
the app and choose **Open**, or clear the quarantine flag:

```bash
xattr -dr com.apple.quarantine "Windows Weather.app"
```

> The app is named "Windows Weather" on macOS too, inherited from the Windows
> original. Renaming it is a one-line change in `packaging/macos/Info.plist`.

### Linux

Download the tarball, unpack it and run `WindowsWeather` — it is self-contained
and carries its own .NET runtime. The AppImage, when present, runs the same way
without unpacking.

Avalonia needs the usual desktop X11/Wayland libraries present (`libX11`,
`libICE`, `libSM`, `fontconfig`). Any normal desktop install already has them; a
minimal container may not.

### Android

Install the APK from the build artifacts. It needs Android 6.0 or later and asks
for one permission: internet access. There is deliberately no location
permission — you choose your places explicitly rather than being geolocated.

### Where settings live

| Platform | Path |
|---|---|
| Windows | `%APPDATA%\WindowsWeatherApp\` |
| macOS | `~/Library/Application Support/WindowsWeatherApp/` |
| Linux | `$XDG_CONFIG_HOME/WindowsWeatherApp/`, else `~/.config/WindowsWeatherApp/` |
| Android | the app's private files directory |

---

## Building from source

### Windows head

No NuGet packages. A bare checkout builds with nothing but MSBuild, which is
deliberate: package restore is usually the first thing to break on an old machine.

```
msbuild WindowsWeatherApp.sln /p:Configuration=Release
```

Or open `WindowsWeatherApp.sln` in Visual Studio 2019 or later and press F5. The
output is a single `WindowsWeather.exe` in `src/WeatherApp.WinForms/bin/Release/`.

> The classic project file declares `NETFRAMEWORK` in `DefineConstants` by hand.
> SDK-style projects define it implicitly; classic ones do not, and without it
> the platform guards in the shared core silently compile out the wrong branch.
> `AppPaths.cs` carries an `#error` that fires if it ever goes missing again.

### macOS head

```bash
dotnet build src/WeatherApp.Desktop/WeatherApp.Desktop.csproj -c Release

# macOS: build a runnable .app bundle
./packaging/macos/build-app.sh                 # host architecture
./packaging/macos/build-app.sh --arch osx-x64  # Intel
./packaging/macos/build-app.sh --universal     # one binary for both

# Linux: build a tarball and (where possible) an AppImage
./packaging/linux/build-linux.sh                     # host architecture
./packaging/linux/build-linux.sh --arch linux-arm64
```

### Android head

```bash
dotnet workload install android
dotnet build src/WeatherApp.Android/WeatherApp.Android.csproj -c Release
```

Release builds are unsigned without a keystore. For a device build, either use a
Debug build (which is signed with the debug key) or supply your own keystore
through the standard `AndroidSigningKeyStore` properties.

### macOS packaging modes

The two packaging modes trade off against each other. **Per-architecture**
publishes normally, so every native dependency (Skia, HarfBuzz, the Avalonia
native layer) lands in `Contents/MacOS` as its own dylib — the layout Apple's
notarisation tooling expects, because each dylib can be signed in place.
**Universal** uses a single-file publish and merges the two executables with
`lipo`, which is
[the only way .NET produces a universal binary](https://learn.microsoft.com/dotnet/core/deploying/macos#universal-binaries);
the native libraries are then extracted at runtime rather than signed in place,
which makes notarisation fiddlier.

### Signing and notarising for distribution

An ad-hoc signature is enough to run the app locally. Handing it to anyone else
requires an **Apple Developer account ($99/yr)** for a Developer ID certificate.
Without notarisation, Gatekeeper blocks the app on every machine but the one
that built it. The sequence is:

```bash
codesign --force --options runtime --timestamp \
  --sign "Developer ID Application: YOUR NAME (TEAMID)" \
  "artifacts/macos/Windows Weather.app"

ditto -c -k --keepParent "artifacts/macos/Windows Weather.app" upload.zip

xcrun notarytool submit upload.zip \
  --apple-id you@example.com --team-id TEAMID --password APP_SPECIFIC_PASSWORD \
  --wait

xcrun stapler staple "artifacts/macos/Windows Weather.app"
```

---

## Where the data comes from

All four sources are free, public and keyless.

| Source | Used for |
|---|---|
| [**api.weather.gov**](https://www.weather.gov/documentation/services-web-api) (NWS) | The official 7-day forecast, the raw forecast grid (separate snow, ice, rainfall and thunder series), and all active alerts. |
| [**Open-Meteo**](https://open-meteo.com/) | Days 8–10 beyond the NWS window, the hour-by-hour split between rain and snow, CAPE, and the 250/300 hPa winds the jet stream map is drawn from. |
| [**SPC convective outlook**](https://www.spc.noaa.gov/products/outlook/) | Hail, damaging wind and tornado probabilities, and the categorical risk level, for Days 1–3. |
| [**NWS RIDGE II**](https://radar.weather.gov/) | Radar imagery. |

**Where they overlap, the NWS wins.** It is the human-reviewed product, and it is
what the watches and warnings are written against.

### How the precipitation types are derived

Worth being explicit about, because it is the part that is not simply read off
an API:

- **Any precipitation** — the NWS forecast grid's own probability where it
  reaches, otherwise the model's daily maximum.
- **Rain / snow / freezing rain** — no upstream publishes these separately, so
  they are derived: for each hour the model expects that type, take that hour's
  precipitation probability; the day's figure is the highest of them. That is an
  honest reading of *"if it precipitates during this hour, it falls as snow."*
- **Thunderstorms** — the forecast office's own `probabilityOfThunder` where it
  is issued, otherwise derived from hourly WMO weather codes.
- **Hail and severe risk** — straight from the SPC outlook. These are
  probabilities of *severe* weather within 25 miles, which is a different (and
  smaller) number than "chance of any hail."

**A dash means the value was never issued, not that the chance is zero.** SPC
hail probabilities stop at Day 3, so days 4–10 show a dash in that column.

---

## Configuration

### Identifying yourself to the NWS

The NWS API asks callers to send a contact address and throttles traffic that
does not. Setting `contact` in `settings.json` makes rate-limiting far less
likely if you refresh often:

```json
{ "contact": "you@example.com" }
```

### Changing an endpoint without rebuilding

NOAA moves image and GIS paths every few years. Every upstream URL the app uses
is listed in `endpoints.json` next to the settings file, written on first run. If
a radar or chart path changes, edit that file and restart — no rebuild needed.
Unknown keys are ignored, so the file is safe to keep across versions.

---

## Windows 7 notes

Windows 7 negotiates TLS 1.0 by default, and every endpoint here requires TLS
1.2. The app requests TLS 1.2 explicitly at startup, but SChannel has to support
it underneath. If you see "could not reach api.weather.gov" on a Windows 7
machine that is otherwise online:

1. Install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48).
2. Make sure the machine has [KB3140245](https://support.microsoft.com/kb/3140245)
   and the "Easy Fix" that enables TLS 1.2 for WinHTTP.
3. Reboot.

Errors are written to `error.log` beside the settings file.

---

## Known limitations

- **NWS coverage is the US and its territories.** Add a location outside it and
  the app says so and falls back to model data alone — there will be no official
  forecast narrative and no alerts, because none are issued.
- **The jet stream map uses a simple equirectangular projection** with longitude
  scaled by the cosine of the box's centre latitude. It keeps shapes roughly
  right over a region-sized box; it is not a conformal projection and does not
  try to be.
- **The jet stream map has no coastlines.** It is anchored with a lat/lon
  graticule and labelled cities rather than a shapefile, which keeps the core
  dependency-free.
- **SPC hail probabilities only reach Day 3.** Nothing publishes them further out.
- **macOS warning notifications are in-window toasts**, not Notification Center
  alerts. A real system notification needs a signed, bundled app and would
  silently do nothing in an unsigned development build — a worse failure than a
  toast the user can definitely see.
- **Android warning notifications are in-window toasts**, not Notification
  Center entries. One that works while the app is closed needs a notification
  channel plus a foreground service or WorkManager job to poll for alerts, which
  is real Android work rather than a UI detail; until then a toast at least
  cannot fail silently.
- **The Linux AppImage is best-effort.** appimagetool needs FUSE, which many
  containers lack, so the self-contained tarball is the guaranteed artifact.

---

## Licence

MIT — see [LICENSE](LICENSE).

Weather data is provided by the US National Weather Service and NOAA (public
domain) and by [Open-Meteo](https://open-meteo.com/) (CC BY 4.0). This app is not
affiliated with or endorsed by NOAA or the National Weather Service.

**In dangerous weather, follow official NWS guidance and local emergency
management. Do not rely on this or any third-party app as your only source of
warnings.**
