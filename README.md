# Windows Weather

A desktop weather app for **Windows 7 SP1, 8, 8.1, 10 and 11** that shows the
things most weather apps stopped showing: the jet stream, animated radar you can
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

## Installing

### Requirements

| Windows version | What you need |
|---|---|
| **11**, **10** (1607+) | Nothing. .NET Framework 4.6.2 or later is already present. |
| **8.1**, **8** | Install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48). |
| **7 SP1** | Install [.NET Framework 4.8](https://dotnet.microsoft.com/download/dotnet-framework/net48), and make sure TLS 1.2 is enabled — see [Windows 7 notes](#windows-7-notes). |

### Running it

Download `WindowsWeather.exe` (and `WindowsWeather.exe.config` alongside it) from
the build artifacts, put both in a folder, and run it. There is no installer and
nothing is written outside your user profile.

Settings live in `%APPDATA%\WindowsWeatherApp\`.

---

## Building from source

The solution has **no NuGet packages**. A bare checkout builds with nothing but
MSBuild, which is deliberate: package restore is usually the first thing to break
on an old machine.

```
msbuild WindowsWeatherApp.sln /p:Configuration=Release
```

Or open `WindowsWeatherApp.sln` in Visual Studio 2019 or later (any edition,
including Community) and press F5. The C# language version used is 7.3.

The output is a single self-contained `WindowsWeather.exe` in
`src/WeatherApp/bin/Release/`.

### Why .NET Framework 4.6.2 and WinForms

It is the only mainstream stack that still covers Windows 7 through 11 from one
binary. .NET 5 and later dropped Windows 7; Electron dropped it at version 23;
Python dropped it at 3.9. 4.6.2 is also the lowest version that enables TLS 1.2
by default, which every endpoint the app talks to requires — so it is a
functional floor, not a preference.

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

This is worth being explicit about, because it is the part that is not simply
read off an API:

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
does not. Setting `contact` in `%APPDATA%\WindowsWeatherApp\settings.json` makes
rate-limiting far less likely if you refresh often:

```json
{ "contact": "you@example.com" }
```

### Changing an endpoint without rebuilding

NOAA moves image and GIS paths every few years. Every upstream URL the app uses
is listed in `%APPDATA%\WindowsWeatherApp\endpoints.json`, which is written on
first run. If a radar or chart path ever changes, edit that file and restart —
no rebuild needed. Unknown keys are ignored, so the file is safe to keep across
versions.

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

Errors are written to `%APPDATA%\WindowsWeatherApp\error.log`.

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
  graticule and labelled cities rather than a shapefile, which keeps the app
  dependency-free.
- **SPC hail probabilities only reach Day 3.** Nothing publishes them further out.

---

## Licence

MIT — see [LICENSE](LICENSE).

Weather data is provided by the US National Weather Service and NOAA (public
domain) and by [Open-Meteo](https://open-meteo.com/) (CC BY 4.0). This app is not
affiliated with or endorsed by NOAA or the National Weather Service.

**In dangerous weather, follow official NWS guidance and local emergency
management. Do not rely on this or any third-party app as your only source of
warnings.**
