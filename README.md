# CardTradeBuddy

A mobile-first web app for Pokémon card trading. A user photographs a card, a local vision model
reads it, the card is looked up in [TCGdex](https://tcgdex.dev), and the prices from Cardmarket and TCGplayer are
turned into one estimated value in EUR. A separate function judges whether a trade between two sets of
cards is fair.

ASP.NET Core Razor Pages on .NET 10. Hosted on Windows with IIS.

## Contents

1. [Status](#status)
2. [Prerequisites](#prerequisites)
3. [Run locally](#run-locally)
4. [Configuration](#configuration)
5. [Deploy to IIS](#deploy-to-iis)
6. [How a card photo is recognised](#how-a-card-photo-is-recognised)
7. [How images are scaled](#how-images-are-scaled)
8. [Card lookup (TCGdex)](#card-lookup-tcgdex)
9. [How prices are calculated](#how-prices-are-calculated)
10. [How a trade is evaluated](#how-a-trade-is-evaluated)
11. [Languages](#languages)
12. [Front page design](#front-page-design)
13. [Project layout](#project-layout)
14. [Known limits](#known-limits)

## Status

| Part | State |
| --- | --- |
| Front page (`/`): title, illustration, login box, language flags | Design only. The fields can be typed in, but Log in, Create user and "I forgot my password" do nothing yet. The language flags do work. |
| Test page (`/ProtoUpload`) | Working. Upload a card photo, pick an Ollama model, see what was read, the card and its prices. Uploads are never saved to disk. |
| `TradeEvaluator` | Working as a function. Not connected to any page yet. |
| Users, login, trades storage | Not built. |

## Prerequisites

**To develop:** .NET 10 SDK, and [Ollama](https://ollama.com) running locally with at least one vision model
(the default is `qwen3-vl:8b`; `ollama pull qwen3-vl:8b`).

**On the server:** IIS with the [ASP.NET Core Hosting Bundle for .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0),
the Microsoft Visual C++ Redistributable (OpenCV's native library needs it), and Ollama reachable from the server.

## Run locally

```bash
dotnet run
```

- `http://localhost:5030` and `https://localhost:7139` (see `Properties/launchSettings.json`).
- `/` is the front page, `/ProtoUpload` is the card recognition test page.
- In VS Code, press F5: `.vscode/launch.json` builds first and opens the browser.

## Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Ollama:BaseUrl` | `http://localhost:11434/` | Where Ollama runs. Set in `appsettings.json` or the environment variable `Ollama__BaseUrl`. |

Other values are constants in code: the default model (`ProtoUpload.cshtml.cs`), the supported languages
(`Localization/AppLanguages.cs`), and the tuning numbers described below.

## Deploy to IIS

Everything is done by one script, run from an **elevated PowerShell** on the server, from the project folder:

```bash
.\Scripts\deploy.ps1
```

It is safe to run again and again. What it does, in order:

1. Creates or updates the app pool `CardTradeBuddy` ("No Managed Code", runs as the built-in
   `IIS AppPool\CardTradeBuddy` account, so no password is stored anywhere).
2. Creates the folder `C:\inetpub\wwwroot\CardTradeBuddy` and gives the pool account read access to it
   and modify access to `logs\`.
3. Creates or updates the site `CardTradeBuddy` with HTTP bindings for `cardtradebuddy.com` and
   `www.cardtradebuddy.com` on port 80.
4. Adds HTTPS bindings (port 443, SNI) for both names, if it can find a certificate (see below).
5. Runs `dotnet publish -c Release` into a temporary folder.
6. Puts `app_offline.htm` in the site so IIS stops the app and releases its files.
7. Mirrors the published files into the site folder with `robocopy /MIR`. Kept untouched: `logs\` and `.well-known\`.
8. Removes `app_offline.htm` and the temporary folder, then makes sure the pool and site are started.
   The removal also happens if copying fails, so the site is never left showing the offline page by mistake.

### Parameters

| Parameter | Default | Meaning |
| --- | --- | --- |
| `-Target` | `C:\inetpub\wwwroot\CardTradeBuddy` | Site folder |
| `-HostNames` | `cardtradebuddy.com`, `www.cardtradebuddy.com` | Host names to bind |
| `-SiteName`, `-PoolName` | `CardTradeBuddy` | IIS names |
| `-Port`, `-HttpsPort` | `80`, `443` | Ports |
| `-CertThumbprint` | (auto-detect) | Use this certificate for HTTPS |
| `-CreateSelfSignedCert` | off | Create a self-signed certificate (testing only, browsers warn) |
| `-SkipHttps` | off | HTTP bindings only |
| `-PoolCredential` | (built-in identity) | Run the pool as a specific account: `-PoolCredential (Get-Credential)` |
| `-SkipIisSetup` | off | Only publish and copy, do not touch IIS |

### HTTPS and Let's Encrypt

The script looks for a certificate that is valid for **every** host name, in the `My` and `WebHosting`
certificate stores (a wildcard such as `*.cardtradebuddy.com` counts, but does not cover the bare domain).
If none is found, HTTPS is skipped with a warning and HTTP keeps working.

To get a free certificate with [win-acme](https://www.win-acme.com):

1. Point both host names at the server's public IP, and open ports 80 and 443.
2. Run the deploy script once, so the site and its HTTP bindings exist.
3. Run `wacs.exe` as Administrator, choose **N** (new certificate), then **1** (single binding of an IIS site),
   pick the site and include both host names.
4. win-acme stores the certificate in the **WebHosting** store, adds the HTTPS bindings and creates a
   scheduled task that renews it automatically. If win-acme is already installed for other sites, use that
   same `wacs.exe`: its existing renewal task covers the new certificate too.

win-acme writes its validation files to `.well-known\` in the site folder, which is why the deploy script's
`robocopy` skips that folder. Rerunning `deploy.ps1` rebinds the newest matching certificate, so a renewed
one is picked up.

### Checking a deployment

```bash
powershell -Command "Import-Module WebAdministration; Get-WebBinding -Name CardTradeBuddy | Select protocol, bindingInformation, certificateHash"
```

Then open `https://cardtradebuddy.com` from another network. Logs are written to `logs\` when enabled.

## How a card photo is recognised

`/ProtoUpload` runs this pipeline for each uploaded photo (one model call in the normal case):

```
photo
  -> check the file type by its first bytes (JPEG, PNG, GIF, BMP, WebP)
  -> CardImagePreprocessor: find the card, straighten it, build one compact image
  -> Ollama (vision model, JSON reply): isPokemonCard, name, setCode, languageCode,
                                        cardNumber, localId, finish, upsideDown
  -> TcgDexCardDataProvider: find the card, cross-check the set
  -> FrankfurterExchangeRateService: USD -> EUR rate
  -> PriceCalculator: estimated price per finish
```

- **Model call:** `OllamaLlmService` posts to Ollama's `/api/chat` with the image as base64, `stream: false` and
  `format: "json"`, so the reply is always valid JSON. The prompt asks for JSON in an exact shape and says
  not to "correct" the set code and language code into real words. The timeout is 5 minutes.
- **Model list:** the dropdown lists only installed models that report the `vision` capability.
- **Upside-down cards:** the prompt tells the model to check orientation first and answer only
  `{"upsideDown": true}` if the text is upside down. The app then turns the card 180 degrees and asks once
  more. This takes about 2 extra seconds. Without that instruction the model spent about 45 seconds
  deciphering upside-down text.
- **Validation of the reply:** the set code must be 2 to 5 letters or digits, the language one of
  EN, DE, FR, IT, ES, PT, JA, and the card number must look like `001/182` (letters allowed, e.g. `TG01/TG30`).
  Anything else is treated as not read.
- **Finish:** the model also guesses `normal`, `holofoil`, `reverseHolofoil` or `unknown`. This is the least
  reliable field, because foil is hard to judge from a photo. The page shows `unknown holofoil` when unsure,
  and prices are still calculated for every finish.

## How images are scaled

The model is slow and less accurate on large photos, and the set code and card number are tiny. A 12-megapixel
photo (3024 x 4158) costs the model about 12,700 prompt tokens and took about 16 seconds; the image built
below takes about 2 to 5 seconds. So the model never sees the original photo. `CardImagePreprocessor` builds
one image that contains only what has to be read:

1. **Decode.** OpenCV applies the photo's EXIF rotation, so a sideways phone photo comes out upright.
2. **Find the card.** On a copy reduced to at most 900 pixels on its longest side: grayscale, 5x5 blur,
   Canny edges (thresholds 40 and 120), dilate twice to close gaps, find the outer contours, and keep the
   largest convex four-corner shape that:
   - covers at least **5%** of the photo, and
   - has the shape of a card: its short side divided by its long side is within **0.12** of 63 / 88 (0.716).
3. **Straighten.** The four corners are mapped to an upright rectangle (perspective correction). Its width is the
   card's measured width, limited to between 300 and 1500 pixels; its height is width x 88 / 63. A card lying
   on its side is turned so the short edge is on top.
4. **Cut three bands** from the straightened card (fractions of its height):

   | Band | From | To | Final width |
   | --- | --- | --- | --- |
   | Top strip (name, HP, type) | 0% | 13% | 1280 px |
   | Artwork thumbnail (to judge foil) | 13% | 55% | 480 px, centred on a grey 1280 px band |
   | Bottom strip (set code, language, number) | 86% | 100% | 1280 px |

   The strips are enlarged with bicubic interpolation (or reduced with area averaging when the card is
   larger than 1280 px). The attacks and rules text in the middle are left out on purpose.
5. **Stack** the three bands with 16 px grey gaps and encode as JPEG, quality 92. The result is roughly
   1280 x 800 px and about 200 KB.

**If no card outline is found** (cluttered background, card the same colour as the table), the whole photo is used
as the card. That works when the card fills the frame. A landscape photo with no outline is assumed to be a
card on its side and is turned 90 degrees.

The page has a collapsible "Image sent to the model" section, and shows whether the outline was found, so you
can see exactly what the model saw.

## Card lookup (TCGdex)

`TcgDexCardDataProvider` implements `ICardDataProvider.GetCardByNameAndIdAsync(name, localId, setCode, languageCode, setTotal)`.

- **Language.** The printed language code selects the TCGdex language: `EN`->`en`, `DE`->`de`, `FR`->`fr`,
  `IT`->`it`, `ES`->`es`, `PT`->`pt`, `JA`/`JP`->`ja`. Anything else falls back to English, which also keeps
  unvalidated model output out of the request URL. Japanese cards must be searched with the Japanese name.
- **Find candidates.** Only the name is sent to the API, as `cards?name=...`, because TCGdex's own `localId`
  filter is a loose "contains" match that has been seen to return nothing for valid IDs. The app keeps the
  cards whose name is an exact (case-insensitive) match and whose number matches. Numbers that are purely
  numeric are compared by value, so `25` equals `025`; others (`TG01`, `SWSH062`) are compared as text.
- **Pick the right set.** The same name and number often exist in several sets. When a set code or total
  is available, each candidate's set is loaded and scored: a matching set total (the 182 in `001/182`) is
  worth 2 points, a matching set code (`DRI`) 1 point. The total counts for more because digits are read more
  reliably than the small letters. The best score wins; with no evidence, the first candidate is used.
  The result carries a `MatchNote`, shown on the page, for example *"Picked by set total 182; the read set
  code "DRL" does not match DRI and was ignored"*, or a warning if neither matches.
- **Stale prices.** TCGdex runs on several servers that do not always hold the same price snapshot, so some
  answers have no TCGplayer prices. The app requests the card up to **4** times, stops as soon as TCGplayer
  prices are present, and keeps the answer with the newest Cardmarket timestamp.
- **Image.** TCGdex returns the image URL without an extension; the app uses `{image}/high.webp`.

## How prices are calculated

TCGdex returns two price sources per card:

| Source | Currency | Fields |
| --- | --- | --- |
| Cardmarket (Europe) | EUR | trend, average, low, 1/7/30-day averages. The plain fields are for the **normal** print; the `-holo` fields are for the foil print and are shared by holofoil and reverse holofoil. |
| TCGplayer (US) | USD | low, mid, high, market, direct low, with a separate block for `normal`, `holofoil` and `reverse-holofoil` |

`PriceCalculator` turns this into one estimate per finish (`CalculateAll`) or for one finish (`Calculate`).

1. **Which figures count.**
   - Cardmarket: trend, 7-day average, 30-day average and overall average. Falls back to the low if none exist.
   - TCGplayer: the market price (based on recent sales) and the mid. Falls back to the low.
   - The highest listing is never used; it is usually one seller's hopeful price. The lowest listing is only a
     last resort, because it may be a damaged copy.
2. **Drop outliers.** A figure is kept when it is within **50%** of the reference value, **or** within **0.10**
   of it. The absolute part means a few cents of difference on a cheap card never counts as an outlier
   (0.02 against 0.07 is 250% apart but only 5 cents). The reference is the median of the Cardmarket figures,
   and the market price for TCGplayer. The kept figures are averaged. Zeros are treated as missing.
3. **Convert** TCGplayer to EUR with the day's USD->EUR rate.
4. **Combine.** The two EUR figures are averaged, unless they disagree by more than the same tolerance
   (for example 107 EUR against 14 EUR). Then no combined figure is given (`AverageEur` is null and the page
   says "marketplaces disagree") and the per-marketplace figures are still shown.

**Example** (made-up numbers), normal print, rate 0.90:

| | Figures | Result |
| --- | --- | --- |
| Cardmarket | trend 12.00, 7-day 11.50, 30-day 13.00, average 40.00 | median 12.50; tolerance 6.25; 40.00 is dropped; mean of the rest = **12.17 EUR** |
| TCGplayer | market 14.00, mid 15.00 | reference 14.00; both kept; mean 14.50 USD = **13.05 EUR** |
| Combined | 12.17 and 13.05 | within tolerance; mean = **12.61 EUR** |

**Finish handling.** TCGplayer's reverse-holofoil figure is only used for the reverse finish; Cardmarket's foil
figures are only offered when TCGplayer also lists that foil finish, because Cardmarket cannot tell holofoil
from reverse holofoil. If the finish is `unknown`, the normal print is assumed when the card has one,
otherwise the cheapest finish with data; the result is then marked `FinishAssumed`. A card with only normal
and reverse prints produces no holofoil row.

**Exchange rate.** `FrankfurterExchangeRateService` calls the free, key-less
[Frankfurter](https://frankfurter.dev) API (European Central Bank reference rates) and caches the rate for
6 hours, since the ECB publishes once per working day. If the rate cannot be fetched, TCGplayer is simply not
converted and the average uses Cardmarket only.

## How a trade is evaluated

`TradeEvaluator.Evaluate(youGive, youReceive, options)` takes two lists of prices in EUR and returns the values,
the difference, the allowed difference and a verdict: `Fair`, `FavorsYou` or `FavorsThem`. There is no
official formula for a fair trade; these are tunable rules of thumb.

1. **Consolidation.** The side with **more cards** is discounted by **3% for each extra card**, at most **15%**.
   Many small cards are harder to sell on than one card of the same total price. Only the difference in card
   count matters: 3 cards against 3 cards gets no discount.
2. **Tolerance shrinks as the trade gets bigger.** With `V` the larger effective side:

   ```
   allowed % = MinPercent + (MaxPercent - MinPercent) / (1 + V / HalfwayValue)
   allowed   = max(MinAbsolute, V x allowed %)
   ```

   Defaults: `MaxPercent` 25%, `MinPercent` 5%, `HalfwayValue` 50 EUR, `MinAbsolute` 1 EUR.
   Cheap trades get a wide percentage, expensive ones a narrow percentage, but the allowed amount in EUR
   still grows with the trade:

   | Trade value V | Allowed % | Allowed in EUR |
   | --- | --- | --- |
   | 10 | 21.7% | 2.17 |
   | 25 | 18.3% | 4.58 |
   | 100 | 11.7% | 11.67 |
   | 400 | 7.2% | 28.89 |
   | 1000 | 6.0% | 59.52 |

3. **Verdict.** `difference = effective received - effective given`. If its size is within the allowance the
   trade is `Fair`; otherwise a positive difference is `FavorsYou` and a negative one `FavorsThem`.

| Trade | Result |
| --- | --- |
| Give 8, get 9.50 | Fair (15.8% difference, 21.8% allowed) |
| Give 400, get 430 | Fair (7.0% against 7.1% allowed) |
| Give 400, get 440 | Favors you (9.1% against 7.0% allowed) |
| Give 100, get 3 cards worth 100 in total | Fair (the 3 cards count as 94; 6% against 11.7% allowed) |
| Give 100, get 3 cards worth 90 in total | Favors them (the 3 cards count as 84.60; 15.4% against 11.7% allowed) |

The numbers live in `TradeOptions`, so they can be tuned without touching the logic. The evaluator knows
nothing about condition, language or demand, only prices.

## Languages

English and Danish, using standard ASP.NET Core localization.

- Text lives in `Resources/Pages/Index.resx` (default, English) and `Resources/Pages/Index.da.resx` (Danish),
  and the page reads it by key (`@Localizer["Welcome"]`).
- Which language is used, per request: `?culture=da` in the URL, else the `.AspNetCore.Culture` cookie, else the
  browser's `Accept-Language` header, else English.
- The flags in the upper-left corner are buttons that post their language to the page. The handler stores the
  choice in the cookie for one year and reloads the page. It works without JavaScript.
- The page's `lang` attribute follows the chosen language.
- Numbers also follow the language, so on `/ProtoUpload` a Danish visitor sees `0,04` and an English one `0.04`.

**To add a language:** add its code to `Localization/AppLanguages.cs`, add a `Index.<code>.resx` next to the
existing ones, and add a flag button to the language switch in `Pages/Index.cshtml`.

## Front page design

Mobile first: the base CSS in `wwwroot/css/home.css` is the phone layout, and wider screens only add to it.

| Screen width | Layout |
| --- | --- |
| Under 560 px (phone) | One column. The login box is a white sheet rising from the bottom over the kids' shoulders. |
| 560 to 899 px (tablet) | The same column, with the sheet as a floating rounded card. |
| 900 px and up (desktop) | Two columns: illustration on the left, a smaller login card on the right. The flags are fixed to the window's upper-left corner. |

Notes: the text typed inside the fields stays at 16 px, because anything smaller makes Safari on iPhone
zoom into the page when a field is tapped. The illustration is an inline SVG and scales to any width. Safe-area
padding keeps the buttons clear of the iPhone home bar.

## Project layout

```
Pages/
  Index.cshtml(.cs)          Front page (design only, language switch works)
  ProtoUpload.cshtml(.cs)    Card recognition test page
  Shared/_Layout.cshtml      HTML shell (language attribute, optional Head section)
Services/
  CardImagePreprocessor.cs   Find the card, straighten it, build the compact image (OpenCvSharp)
  OllamaLlmService.cs        Vision model calls and model list (ILlmService)
  TcgDexCardDataProvider.cs  Card lookup, set cross-check, stale-price retry (ICardDataProvider)
  CardData.cs                The card and its prices
  PriceCalculator.cs         Estimated price per finish, outlier filtering
  FrankfurterExchangeRateService.cs   USD -> EUR rate, cached (IExchangeRateService)
  TradeEvaluator.cs          Fair-trade check and TradeOptions
Localization/AppLanguages.cs Supported languages
Resources/Pages/             .resx text files per language
Scripts/deploy.ps1           IIS setup and deployment
wwwroot/css/home.css         Front page styles
```

## Known limits

- The front page does nothing yet: no users, login, password reset or storage of trades.
- Card detection can fail on a cluttered background or when the card is the same colour as the table; then the
  whole photo is used. Cards photographed upside down rely on the model noticing (one extra call).
- The layout assumption is a modern card: set code and number at the bottom, name at the top. Older layouts
  read less reliably.
- The model often cannot tell holofoil from reverse holofoil in a photo, so the finish is a guess. Prices are
  therefore shown for every finish, and TCGdex can tell which prints a card actually has.
- Prices come from TCGdex; the data can be days old (the TCGplayer timestamps were several days behind
  Cardmarket's in testing) and, for rarely traded cards, very thin. The calculator handles this only by dropping
  outliers and by flagging marketplaces that disagree.
- Only English-name searches work for English cards, and Japanese names for Japanese cards; the printed
  name has to match the language code read from the card.
- HEIC photos (iPhone's default format) are not supported and are rejected as "not a supported image". iOS
  usually converts photos to JPEG when it uploads them through a browser, but if a HEIC file does arrive it
  cannot be read.
