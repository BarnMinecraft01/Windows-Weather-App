# Why there is no browser (WebAssembly) build

Avalonia 12 targets WebAssembly, and the shared core is plain C# with no
platform dependencies, so a browser head looks like it should be nearly free.
It is not, and the blocker is not something the code can work around.

## The blocker

In a WebAssembly build, `HttpClient` is implemented on top of the browser's
`fetch()`. That means every request is subject to the same-origin policy, and a
cross-origin request only succeeds if the server opts in with CORS headers.

**`api.weather.gov` does not send `Access-Control-Allow-Origin`.** This is a
long-standing, documented limitation, discussed repeatedly in the NWS API's own
issue tracker ([#312](https://github.com/weather-gov/api/discussions/312),
[#739](https://github.com/weather-gov/api/discussions/739)).

There is a second problem stacked on top of it. The NWS asks callers to identify
themselves with a meaningful `User-Agent`, and browsers do not let page script
set that header — it is refused at the CORS preflight stage. So even if the
origin check were relaxed, the app could not identify itself the way the service
asks it to.

## Why that is fatal rather than inconvenient

`api.weather.gov` is not one source among four. It supplies:

- the official forecast and its narrative,
- the raw forecast grid that the snow, ice and thunder series come from, and
- **every watch, warning and advisory.**

A weather app whose entire reason for existing is to surface NWS products,
running in a mode where it cannot reach NWS products, is not a reduced version
of this app. It is a different and much worse one.

Open-Meteo is designed for browser use and would almost certainly work. That
would leave a build with model data and no alerts — which is precisely the
trade-off this project was started to avoid.

## The workaround, and why it was rejected

The standard answer is a CORS proxy: a small server that fetches on the client's
behalf and adds the headers. It works, and it changes what the project is:

- **It needs hosting.** Something has to run, be paid for, and be kept up.
- **It centralises the traffic.** Every user's requests go through one endpoint,
  which then has to satisfy the NWS rate limits for all of them at once.
- **It sees every user's location.** Today the app talks directly to NOAA from
  the user's own machine and nobody in the middle learns where they live. A
  proxy makes that untrue, and a weather app's request log is a location
  history.

The desktop and Android heads are keyless, serverless and have no operator. A
browser head would need all three, for the sake of a build that could not show
warnings anyway.

## What would change this

- The NWS enabling CORS on `api.weather.gov`. That is the whole fix, and it has
  been asked for for years.
- Or a decision that a hosted service is acceptable — at which point the proxy
  is reasonable engineering, and this note should be revisited.

Until then: **no browser head.** The four native heads cover Windows 7 through
11, macOS, Linux and Android, and all four talk to NOAA directly.
