# CardTradeBuddy

A mobile-first web app for PokÃƒÂ©mon card trading. A user photographs a card, a local vision model
reads it, the card is looked up in [TCGdex](https://tcgdex.dev), and the prices from Cardmarket and TCGplayer are
turned into one estimated value in EUR. A separate function judges whether a trade between two sets of
cards is fair.

ASP.NET Core Razor Pages on .NET 10. Hosted on Windows with IIS.

## Contents

1. [Status](#status)
2. [Prerequisites](#prerequisites)
3. [Run locally](#run-locally)
4. [Configuration](#configuration)
5. [Login (LoginServer)](#login-loginserver)
6. [Deploy to IIS](#deploy-to-iis)
7. [How a card photo is recognised](#how-a-card-photo-is-recognised)
8. [How images are scaled](#how-images-are-scaled)
9. [Card lookup (TCGdex)](#card-lookup-tcgdex)
10. [How prices are calculated](#how-prices-are-calculated)
11. [How a trade is evaluated](#how-a-trade-is-evaluated)
12. [Languages](#languages)
13. [Database (preferences)](#database-preferences)
14. [Front page design](#front-page-design)
    - [Start page](#start-page)
15. [Project layout](#project-layout)
16. [Known limits](#known-limits)

## Status

| Part | State |
| --- | --- |
| Front page (`/`) and account pages | Working: log in, create user, forgot password, reset password, confirm email and log out, all through the LoginServer (see [Login](#login-loginserver)), plus the language flags. |
| Start page (`/Start`) | Working, for logged-in users only; where login ends. Two big halves: **Check a card** (opens `/Check`) and **Fair swap?** (shown as "Coming soon": there is no page behind it yet). See [Start page](#start-page). |
| Check a card page (`/Check`) | Working, for logged-in users only. Take a photo or upload one, see the card, set, rarity and the estimated price. See [Check a card page](#check-a-card-page). |
| Test page (`/ProtoUpload`) | Working, for logged-in users only. Upload a card photo, pick an Ollama model, see what was read, the card and its prices. Uploads are never saved to disk. |
| `TradeEvaluator` | Working as a function. Not connected to any page yet. |
| Storing trades or other per-user data | Not built. The app has no database of its own; users live in the LoginServer. |

## Prerequisites

**To develop:** .NET 10 SDK, and [Ollama](https://ollama.com) running locally with at least one vision model
(the default is `qwen3-vl:8b`; `ollama pull qwen3-vl:8b`).

**For login:** the [LoginServer](../LoginServer) running, with this app registered in it (see [Login](#login-loginserver)).

**On the server:** IIS with the [ASP.NET Core Hosting Bundle for .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0),
the Microsoft Visual C++ Redistributable (OpenCV's native library needs it), and Ollama reachable from the server.

## Run locally

```bash
dotnet run
```

- `http://localhost:5030` and `https://localhost:7139` (see `Properties/launchSettings.json`).
- `/` is the front page (the login box), `/ProtoUpload` is the card recognition test page. It needs a login, so first put the client secret in the git-ignored `appsettings.Development.local.json` next to the project (see [Login](#login-loginserver)).
- In VS Code, press F5: `.vscode/launch.json` builds first and opens the browser.

## Configuration

| Setting | Default | Meaning |
| --- | --- | --- |
| `Ollama:BaseUrl` | `http://localhost:11434/` | Where Ollama runs. Set in `appsettings.json` or the environment variable `Ollama__BaseUrl`. |
| `Login:Authority` | `https://secure.phong.dk` | The LoginServer's address. |
| `Login:ClientId` | `cardtradebuddy` | The client id this app is registered under in the LoginServer. |
| `Login:ClientSecret` | (none) | The app's secret in the LoginServer. **Never in the repo:** `appsettings.Development.local.json` (git-ignored) in development, `appsettings.Production.json` on the server. |
| `Login:VerifyEmail` | `false` | `true`: new users must click a link in an email before they can log in. Needs the app's email sender set up in the LoginServer. |
| `ConnectionStrings:Default` | (none) | The PostgreSQL database for per-user preferences (see [Database](#database-preferences)). **Never in the repo**, like the login secret. Without it the site works, but preferences are not saved. |
| `Account:MinimumAge` | `13` | The youngest age that may create a user (checked against the birthday given at sign-up). |
| `Turnstile:SiteKey` | (none) | Cloudflare Turnstile's public key (goes in the page). With `Turnstile:SecretKey` it switches the check on. |
| `Turnstile:SecretKey` | (none) | Turnstile's private key, used to check the visitor's answer with Cloudflare. **Never in the repo**, like the other secrets. |
| `Network:BehindCloudflare` | `false` | `true`: trust Cloudflare's `CF-Connecting-IP` header as the visitor's address (for the rate limit). Only set it if the site really is behind Cloudflare. |
| `RateLimit:AuthPerMinute` | `10` | Requests per minute from one address to the login and account pages. |
| `RateLimit:ScanPerMinute` | `12` | Requests per minute from one logged-in user to the Check a card page. |
| `Album:PageSize` | `50` | How many cards the album page shows per page (1 to 200). |
| `Check:Model` | `qwen3-vl:8b` | The Ollama model the Check page uses to read cards. |

Settings that must not be in the repo live outside it. In development: the git-ignored `appsettings.Development.local.json` in the project folder, for example `{ "Login": { "Authority": "https://localhost:5001", "ClientSecret": "..." } }` (`dotnet user-secrets` works too). On the server: `appsettings.Production.json` in the site folder, which `deploy.ps1` writes and never overwrites.

Other values are constants in code: the default model (`ProtoUpload.cshtml.cs`), the supported languages
(`Localization/AppLanguages.cs`), and the tuning numbers described below.

## Login (LoginServer)

CardTradeBuddy has no user database. Users live in the **LoginServer** (`C:\VS Code\LoginServer`, deployed at
`https://secure.phong.dk`), a single-sign-on server built on OpenIddict and PostgreSQL. CardTradeBuddy is registered
there as an app with **its own login page**: the login box on this site sends the credentials from this app's
**server** (never from the browser) to the LoginServer, which decides. The LoginServer's own README is the full
reference for its API; `MyPaintingBook` uses the same API and was the model for this code.

### What the app calls

| Action | Calls to the LoginServer |
| --- | --- |
| Log in | `POST /connect/token` with `grant_type=password`, the client id and secret, the email and password. On success the reply holds a token with the user's id, email, app and admin flags, from which the app starts its own session. |
| Create user | A service token (`grant_type=client_credentials`, scope `users.create`), then `POST /api/users` with email and password (plus `verifyEmail`, `culture` and `verifyPath` when email confirmation is on). The LoginServer decides what the new user is: never an admin, and with access to this app only. |
| Confirm email | A service token (scope `users.verify`), then `POST /api/users/verify-email` with the token from the email link. |
| Forgot password | A service token (scope `passwords.reset`), then `POST /api/password-reset/request`; the LoginServer sends the email. The link opens `/Account/ResetPassword?token=...`, which calls `POST /api/password-reset/confirm`. |

Code: `Services/LoginServerClient.cs`. The pages are in `Pages/Account/` (Login, Register, ForgotPassword,
ResetPassword, VerifyEmail, Logout); the front page shows the same login box.

### Sessions

- A successful login starts a session cookie `cardtradebuddy.session` (HttpOnly, SameSite Lax, Secure in production).
  Without "Remember me" it is a session cookie that ends when the browser closes, or after 8 hours without use. With
  "Remember me" it lasts **30 days** (renewed while the user keeps coming back), and the email is remembered in a second
  cookie, `cardtradebuddy.email`, to prefill the login box. A remembered session is not re-checked with the LoginServer,
  so a user who is switched off there can stay logged in here until the cookie ends.
- Logging in here creates no session on the LoginServer, so there is no single sign-on to other apps.
- **Show/hide password:** every password field has an eye button (`Pages/Shared/_PasswordField.cshtml`, `wwwroot/js/password-toggle.js`).
  Pressing it shows what was typed and pressing again hides it; a visible password is hidden again when the form is sent.
  Without JavaScript the button stays hidden, so there is never a button that does nothing.
- `/ProtoUpload` requires a login (`AuthorizePage` in `Program.cs`). A visitor who is not logged in is sent to
  `/Account/Login`, and after logging in returns to the page they asked for, or to `/Start` (`LoginForm.HomePath`). `/Start` also requires a login.

### Protections

- **Anti-forgery tokens** on every form post (language switch, login, create user, forgot and reset password, confirm email,
  log out, card upload), and logging out is a POST, so another site cannot log anyone out. Razor Pages validates the token on
  every POST by default; a post without it gets HTTP 400.
- **Form guard** (`Services/FormGuard.cs`, partial `Pages/Shared/_FormGuard.cshtml`) on the public forms (login, create user,
  forgot and reset password): a hidden trap field that people never see and bots fill in, plus a signed time stamp. A post that
  comes in less than 2 seconds after the form was shown, more than 8 hours after, or with the trap filled, is refused with
  "The form was not sent. Reload the page and try again." This only stops simple bots; the rate limit and the LoginServer's lockout do the rest.
- **Cloudflare Turnstile** (`Services/Turnstile.cs`, partial `Pages/Shared/_Turnstile.cshtml`) on the forms an anonymous visitor
  submits: login (front page and `/Account/Login`), create user, forgot password, reset password and confirm email. The widget gets a
  one-time token from Cloudflare; the server checks it with Cloudflare (`siteverify`, with the visitor's address) before doing
  anything else with the post, and refuses with "We could not check that you are a person" if it is missing, wrong, used before or
  expired. If Cloudflare cannot be reached the post is refused too (the widget could not have loaded either). Not on the language
  switch, log out and the card upload: those are one-click or for logged-in users, and have the anti-forgery token.
  `FormChecks` runs the form guard and Turnstile in one call. Turnstile is **off when the keys are not set**, so the site still works.
- **Rate limit:** 10 requests a minute per address to the login and account pages (`RateLimit:AuthPerMinute`); more get
  HTTP 429. The LoginServer also locks an account for 15 minutes after 5 wrong passwords.
- **No account enumeration:** creating a user with an existing email, and asking for a password reset for an unknown
  email, give the same answer as the normal case.
- **Open redirects:** the return address after login and after a language switch is only accepted if it is a path on this site.
- **Age limit:** creating a user needs a birthday, and the user must be at least `Account:MinimumAge` years old (13 by default).
  The birthday is checked on the server (the date field's limits are only a convenience) and never shown to anyone.
- **Passwords** must be at least 10 characters (the LoginServer's rule). They are never echoed back to the page or logged.
- **Email links** (confirm email) only show a button; the address is confirmed when the button is pressed, because mail
  programs and virus scanners open links automatically and must not be able to use them up.
- **The client secret** is never in the repo.

### Turnstile setup

1. In the Cloudflare dashboard open **Turnstile** and **Add widget**: name `CardTradeBuddy`, hostnames `cardtradebuddy.com` and
   `www.cardtradebuddy.com`, widget mode **Managed**. Copy the **site key** and the **secret key**.
2. On the server, once: `.\Scripts\deploy.ps1 -TurnstileSiteKey '<site key>' -TurnstileSecretKey '<secret key>'` (saved in
   `appsettings.Production.json`, kept on later deployments).
3. **Development:** Cloudflare publishes test keys that work on any host: site key `1x00000000000000000000AA` and secret key
   `1x0000000000000000000000000000000AA` always pass (the widget shows a "testing only" banner). Put them under `Turnstile` in
   `appsettings.Development.local.json`. The secret `2x0000000000000000000000000000000AA` always fails, to try the error message.
4. If every post is refused after deploying, the log says `Turnstile:SecretKey is not accepted by Cloudflare`: the keys do not match
   (or the widget does not list the host name the site is opened on).

### Set it up (once)

1. **Register the app in the LoginServer:** `https://secure.phong.dk` -> Admin -> Apps -> Register app.
   - Name `CardTradeBuddy`, Client ID `cardtradebuddy` (must match `Login:ClientId`).
   - Redirect URI `https://cardtradebuddy.com/signin-oidc`. This app never uses it (there is no redirect login), but the
     LoginServer needs one and builds the links in its emails (password reset, email confirmation) from the **origin of
     the first redirect address**, so use the address people really use (`cardtradebuddy.com`, not `www`).
   - Tick **Require a client secret** (leave the secret empty to have one generated), **Allow this app to have its own
     login page** and **Allow this app to create users**.
2. **Email (for forgot password, and for email confirmation if you use it):** verify `cardtradebuddy.com` in Resend
   (DNS records in Cloudflare), then fill in **Email sender address** on the app's page in the LoginServer (for example
   `support@cardtradebuddy.com`).
3. **Copy the secret:** on the app's page in the LoginServer click **Show secret**.
4. **Give the secret to this app.** On the server, once: `.\Scripts\deploy.ps1 -LoginClientSecret "<secret>"` (saved in
   `appsettings.Production.json`, kept on later deployments). For development: put it in `appsettings.Development.local.json` in the project folder
   (`{ "Login": { "Authority": "https://localhost:5001", "ClientSecret": "<secret>" } }`).
5. **Access:** users created through the app get access to it automatically. An existing LoginServer user needs
   Users -> the user -> App access -> tick CardTradeBuddy.
6. **Email confirmation (optional):** once the email sender works, deploy with `-LoginVerifyEmail true`. With it off, a new
   user can log in straight away. With it on, a new user must first click the link in the email, and creating users fails
   if the app has no email sender in the LoginServer. **Give the flag once:** it is saved in `appsettings.Production.json`
   and kept on every later deployment, so a plain `.\Scripts\deploy.ps1` does not reset it. Switch it off again with
   `-LoginVerifyEmail false`. (A user who never gets the mail can be confirmed by hand in the LoginServer: Users -> the user ->
   Email confirmed.)

### Development and trouble-shooting

- **Local setup.** The development LoginServer (`C:\VS Code\LoginServer`, `https://localhost:5001`) registers this app at
  startup from its user-secrets (`dotnet user-secrets list --project src/LoginServer`, the keys `Bootstrap:Apps:cardtradebuddy:*`:
  client id, name, redirect URIs for `https://localhost:7139`, own login, user creation, the secret and the mail sender).
  This app has `Login:Authority` (`https://localhost:5001`) and the same `Login:ClientSecret` in `appsettings.Development.local.json`.
  (The secret is also shown in the LoginServer admin: Apps -> CardTradeBuddy -> Show secret.)
  The LoginServer only registers an app when its client id does not exist yet, so later changes are made in its admin
  pages. Restart this app after changing its settings; the LoginServer needs no restart once the app is registered.
  If a login says the client id is invalid, check which server the app uses: in Development it logs `Login settings: Authority=...`
  at startup and the error message names the server. A tool that is not running as you can write `dotnet user-secrets` to a
  place your editor does not read, which is one reason for preferring the local file.
- **Emails in development** are not sent: the LoginServer writes them to its log (look for `DEV EMAIL`). The links in them
  point at `https://localhost:7139`, so run this app on that address (the `https` launch profile) to follow them.
  To try email confirmation locally: set `"VerifyEmail": true` under `Login` in `appsettings.Development.local.json`
  (or `dotnet user-secrets set "Login:VerifyEmail" "true"`).
- Against the deployed LoginServer on `secure.phong.dk` the app has to be registered first (see "Set it up" above); the
  links in emails then point at `https://cardtradebuddy.com`.
- The app logs a warning at startup if `Login:ClientSecret` is missing, and in Development it logs the LoginServer address it will use.
  Every login attempt is logged with the email, the server that answered and the outcome (`InvalidCredentials`, `LockedOut`,
  `NoAccess`, `NotVerified`, ...), never the password: look for `Login for ...` in the console. A wrong password, a lockout and a
  user without access to the app can all be told apart there.
- `appsettings.Development.local.json` overrides environment variables and user-secrets (it is added last). It is not copied to
  `bin\` or into a publish, and `deploy.ps1` never copies `*.local.json` to the server.
- The LoginServer's own login page and this app check passwords with the same function and the same rules, so the same
  password gives the same result. If a login works there but not here, compare the databases: the development LoginServer
  (`https://localhost:5001`, database `loginserver`) and the one on `secure.phong.dk` (`loginserver_prod`) keep separate users
  and passwords.
- In Development, when the login service is "not available", the message ends with `[dev detail: ...]` saying why, for
  example a wrong client id or secret, or a setting that is not ticked for the app in the LoginServer. Visitors never see it.
- Tested end to end against the development LoginServer: create user (also twice with the same email), log in, wrong
  password, the account lockout, forgot and reset password (with the emailed link, used once only), and email
  confirmation. Not yet run against the production LoginServer, where the app still has to be registered.

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
7. Mirrors the published files into the site folder with `robocopy /MIR`. Kept untouched: `logs\`, `.well-known\` and `appsettings.Production.json` (the server-side settings, including the LoginServer secret). If `-LoginClientSecret` and friends are given, that file is written or updated now; it is readable only by administrators and the app pool.
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
| `-LoginClientSecret` | (keep the stored one) | The LoginServer client secret. Give it once; it is saved in `appsettings.Production.json` and kept on later deployments. |
| `-DatabaseConnectionString` | (keep the stored one) | The PostgreSQL connection string for preferences, or put it on one line in `Scripts\prod.connection.local`. Saved in `appsettings.Production.json`. See [Database](#database-preferences). |
| `-TurnstileSiteKey`, `-TurnstileSecretKey` | (keep the stored ones) | Cloudflare Turnstile keys. Give them once; saved in `appsettings.Production.json` and kept on later deployments. See [Turnstile setup](#turnstile-setup). |
| `-LoginVerifyEmail` | (keep the stored one) | `true` or `false`: sets `Login:VerifyEmail`. Give it once; it is saved in `appsettings.Production.json` and kept on later deployments. |
| `-BehindCloudflare` | off | Sets `Network:BehindCloudflare` to true. |
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
  -> FrankfurterExchangeRateService: USD -> EUR rate (and EUR -> DKK for the Danish page)
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

- Text lives in `Resources/SharedResource.resx` (default, English) and `Resources/SharedResource.da.resx` (Danish),
  and pages read it by key (`@T["Welcome"]`; code uses `IStringLocalizer<SharedResource>`).
- Which language is used, per request: `?culture=da` in the URL, else the `.AspNetCore.Culture` cookie, else the
  browser's `Accept-Language` header, else English.
- The flags in the upper-left corner are buttons that post their language to `/set-language`. It stores the choice in
  the cookie for one year and returns to the page the visitor came from. It works without JavaScript.
- When a **logged-in** user picks a language, it is also saved as their preferred language (see [Database](#database-preferences)).
  At the next login, on any device, the saved language replaces the browser's cookie. A visitor who is not logged in only gets the cookie.
- The page's `lang` attribute follows the chosen language.
- Numbers also follow the language, so on `/ProtoUpload` a Danish visitor sees `0,04` and an English one `0.04`.
- **Currency follows the language:** a Danish page shows prices in kroner (`23,87 kr.`), every other language in euros (`€12.34`).
  The prices are still worked out in EUR (see [How prices are calculated](#how-prices-are-calculated)) and converted only for display,
  with the day's EUR -> DKK rate from Frankfurter (`GetEurToDkkAsync`, cached 6 hours). The raw marketplace figures keep their own
  currency and label (TCGplayer USD). If the rate cannot be fetched the Danish page falls back to euros rather than showing a wrong number.
  The sample price on the Start page is a resource text (`StartTagPrice`).

**To add a language:** add its code to `Localization/AppLanguages.cs`, add a `SharedResource.<code>.resx` next to the
existing ones, and add a flag button to the language switch in `Pages/Shared/_Hero.cshtml`.

## Database (preferences)

The app has its own PostgreSQL database for settings that belong to a user. People and passwords stay in the LoginServer; this
database only knows a user by the LoginServer user id (the `NameIdentifier` claim).

| Table `user_preferences` | Type | Meaning |
| --- | --- | --- |
| `UserId` (primary key) | `varchar(64)` | The LoginServer user id. |
| `Language` | `varchar(10)`, null | The language the user last picked (`en`, `da`), or null. |
| `UpdatedAt` | `timestamptz` | When the row last changed. |

| Table `user_profiles` | Type | Meaning |
| --- | --- | --- |
| `UserId` (primary key) | `varchar(64)` | The LoginServer user id. |
| `BirthDate` | `date` | The birthday given when the user was created. Saved only for a brand-new account (never for an "already exists" answer). |
| `CreatedAt` | `timestamptz` | When the row was saved. |

Users created before the birthday was asked for have no `user_profiles` row.

| Table `albums` | Type | Meaning |
| --- | --- | --- |
| `Id` (primary key) | `uuid` | |
| `UserId` | `varchar(64)` | The LoginServer user id of the owner (indexed). |
| `Name` | `varchar(100)` | The default album is stored as "My cards" and shown as "My cards" / "Mine kort" (resource `DefaultAlbumName`). |
| `IsDefault` | `boolean` | The album cards go to when none is chosen. A unique partial index (`IX_albums_UserId_default`) allows only one per user. |
| `CreatedAt` | `timestamptz` | |

| Table `cards` | Type | Meaning |
| --- | --- | --- |
| `Id` (primary key) | `uuid` | |
| `AlbumId` | `uuid`, not null | The **one** album the card is in (foreign key to `albums`, deleting an album deletes its cards). |
| `AddedAt` | `timestamptz` | |
| `CardId`, `LocalId`, `Name`, `Category`, `Rarity`, `Illustrator`, `Description` | text | What TCGdex said about the card, for example `sv10-001`. |
| `SetId`, `SetName`, `SetOfficialCardCount` | text, int | The set. |
| `ImageUrl`, `SetLogoUrl` | text | Addresses of the card picture and the set logo on TCGdex's servers. The user's own photo is **never** stored. |
| `Hp`, `Stage`, `Types` (`text[]`), `Retreat` | | Card facts. |
| `Finish` | text | The version the user said they have: `Normal`, `Holofoil` or `ReverseHolofoil` (chosen when the card is added). The price is for this finish. |
| `FinishAssumed` | `boolean` | Always false now (the user chooses the finish); kept for cards saved while the finish was still guessed from the photo. |
| `EstimatedPriceEur` | `numeric(12,2)` | The calculated price in EUR for `Finish`, null when there was none. |
| `Pricing` | `jsonb` | Every marketplace figure TCGdex had (Cardmarket EUR, TCGplayer USD), as received (`CardPricing`). |
| `PricesCheckedAt` | `timestamptz` | **When the price was last looked up.** A later price refresh should update `Pricing`, `EstimatedPriceEur` and this. |

Owning two copies of a card means two rows. A user has albums through `albums.UserId`; `cards` has no user column of its own.
`Services/AlbumService.cs` creates the default album on first use (a race between two requests is handled by the unique index) and
adds cards. Users cannot create more albums yet: `AddCardAsync(userId, albumId?, result)` already takes an album, so a later
"choose an album" only has to pass the id.

A new setting is a new column: add a property to `Data/UserPreference.cs`, then `dotnet ef migrations add <Name> -o Data/Migrations`
(the tool is in `.config/dotnet-tools.json`; run `dotnet tool restore` once; add `--configuration Release` if the debugger has the
Debug build locked).

- **Tables are created at startup** (`Database.MigrateAsync`), so a new database only has to exist. If the database is missing or
  unreachable the site still starts and logins work: the error is logged and preferences are simply not saved or loaded.
- **Saving:** `/set-language` writes the language when the user is logged in (`Services/UserPreferences.cs`).
- **Loading:** at login, `Account/Login` reads the saved language and sets the language cookie to it.
- **Development:** create an empty database (`create database cardtradebuddy_dev;`) and put the connection string in the git-ignored
  `appsettings.Development.local.json`: `{ "ConnectionStrings": { "Default": "Host=localhost;Port=5432;Database=cardtradebuddy_dev;Username=...;Password=..." } }`.
- **Production:** create an empty `cardtradebuddy_prod` database, then deploy once with `-DatabaseConnectionString '...'` (or put the
  connection string on one line in `Scripts\prod.connection.local`, git-ignored). It is saved in `appsettings.Production.json` and kept
  on later deployments. The script refuses a connection string that names a `_dev` database.

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

### Start page

The page after login (`Pages/Start.cshtml`, styles at the end of `home.css`). The header has the language flags, the logo with
"Signed in as ...", and Log out. Below it are the two modes, each a half of the screen with its own pastel backdrop and picture:

| Mode | Look | Goes to |
| --- | --- | --- |
| Check a card | Blue backdrop, two cards under a magnifying glass, tags for set and price (`Pages/Shared/_StartArtCheck.cshtml`) | `/Check` |
| Fair swap? | Orange backdrop, two kids lying on a rug trading cards (`_StartArtSwap.cshtml`) | Nothing yet: shown with a "Coming soon" badge and not a link. Make it an `<a asp-page="...">` like the other half when the page exists. |

- **Phone (under 560 px):** the halves are stacked and both fit on one screen: the pictures give way before the text does
  (`max-height` on `.mode-art`). **Tablet:** the same, in a centered column. **Desktop (900 px and up):** side by side; the half under
  the pointer grows a little (not with reduced-motion).
- The pictures are inline SVGs, so there are no image files and they scale to any width. The texts are in the resource files
  (`StartCheckTitle`, `StartSwapText`, ...), English and Danish.
- The language flags are the shared partial `_LanguageSwitch.cshtml` (also used by the hero on the login pages).
- **Saved cards icon** (`Pages/Shared/_SavedCardsIcon.cshtml`): two stacked cards with a bookmark, in the header of Start and Check, shown only
  when logged in. It opens the [album page](#album-page). Texts: `SavedCards` (en "Saved cards", da "Gemte kort").
- `/ProtoUpload` (the developer test page with the model dropdown and raw data) is unchanged and still reachable directly.

### Check a card page

`/Check` (`Pages/Check.cshtml(.cs)`, script `wwwroot/js/check.js`, styles at the end of `home.css`). In the same style as the Start page:
a blue panel with a viewfinder picture, **Take a photo** and **Upload an image** (phone), and on desktop a dashed drop box with
**Upload an image** and **Use camera**.

1. **Choose.** Take a photo, pick a file, or drag one into the box. The script shrinks big photos to 2400 px on the long side
   (JPEG) before sending, and puts the file in the one posted field `Photo`. **Take a photo** opens the camera screen below.
   Where the live camera cannot be used (no https, no camera, access refused) a phone opens its own camera app
   (`<input capture="environment">`) and a computer opens the file picker.
2. **Preview.** The chosen photo is shown with **Check this card** and **Choose another**.
3. **Check.** The form posts to the same page. Meanwhile (a few seconds) the photo stays on the page with a scan animation (a light bar
   sweeping up and down over it, with a faint blue tint), the buttons are hidden, and the text "Finding the card..." is shown below it.
   `Services/CardChecker.cs` does the work (same steps as ProtoUpload: card detection and the compact image, one model call, an
   upside-down retry, TCGdex lookup, price calculation). Photos are read in memory and never saved.
4. **Answer.** The card image, name, set, number, rarity, the estimated price for the detected finish (in kroner on a Danish
   page) and the price per finish. If the finish could not be seen, it says which version the price is for. A photo that cannot be
   used (not an image, over 12 MB, not a Pokémon card, unreadable, not in the list, reader offline) gives a message and the choose
   screen again.
5. **Add to album.** Under the answer the user must say **which version they have**: three tiles with the finish icons, Normal, Holofoil and
   Reverse holofoil (grey means no shimmer: normal is all grey, holofoil has the rainbow shimmer and a star inside the picture, reverse
   holofoil has it on the frame around a plain picture; the icons are the sprite `Pages/Shared/_FinishSprite.cshtml`). Nothing is chosen
   beforehand (the photo cannot tell foil reliably), the browser will not send the form until one is picked (`required`), and the server
   refuses a missing or unknown value. **Add to my album** then puts the card in the user's default album (created the first time),
   saved with that finish and the price calculated for exactly that finish. The result travels in a signed, sealed hidden field (data
   protection, valid 30 minutes), so what is added cannot be changed from the browser; a tampered or too old field gives "This result
   is too old to add". The same card and finish added twice within 30 seconds (a double press or a refresh) is saved once. After adding,
   the page says "Added to My cards as Holofoil" and "Check another card" becomes the main button.

**The camera screen** (full screen, `getUserMedia`, back camera on phones): the live picture with a dark shade around a card-shaped
outline (63:88, a rounded rectangle in the middle, `CardFrame.rect` in `wwwroot/js/card-frame.js`).

- **Green layer.** Four times a second a small copy of the picture is checked (`CardFrame.analyze`). The card does not have to
  match the outline exactly: it is accepted when its size is within 12% of the outline, its centre within 8%, and it is card shaped.
  The card is found in two ways, and either is enough: (1) its four edges, searched within 12% of each side of the outline (the outer
  edge on each line); (2) for flat, low-contrast surfaces such as a white desk where the border hardly shows, the extent of the busy
  part of the picture (artwork and print). The inside must also be sharp (a blurred or out-of-focus picture stays white). After
  three good looks in a row the outline turns green with a green layer and the hint says "Looks good. Take the picture now". If a
  card is found but is the wrong size the hint says "Move a little closer" or "Move a little further away". A card turned more than
  about 10 degrees stays white. It is a hint only: the shutter works at any time, and the server still reads the card. The thresholds
  are the constants at the top of `card-frame.js`.
- **Tuning on a real phone.** Open `/Check?camdebug=1`: the hint then also shows what the check measures, for example
  `[0.47/0.77/0.95/0.62 detail 46 ok]` (how sure each edge is, top/right/bottom/left; the sharpness; and `ok`, `small`, `big`, `off`
  or `-` for nothing found).
- **Crop.** The shutter cuts the full-resolution camera picture to the outline plus a 3% margin before it goes on to the preview.
- Close with the X or Escape. The outline lines up with the picture whatever the screen shape: the video uses `object-fit: contain`
  and the overlay is an SVG whose `viewBox` is the picture's own pixel size.
- The live camera needs https (localhost counts). Production is https.

Settings: `Check:Model` (default `qwen3-vl:8b`; the first image-reading model that is installed is used if it is not), and the rate
limit `RateLimit:ScanPerMinute` (12 per logged-in user a minute). The card prompt is a copy of the one in `ProtoUpload.cshtml.cs`:
change both together (ProtoUpload could be made to use `CardChecker` later).

### Album page

`/Album` (`Pages/Album.cshtml(.cs)`, styles at the end of `home.css`), for logged-in users; the saved cards icon in the header opens it.
It shows the user's **default album** (always the default for now; `?album=<id>` is ready for "choose an album" and gives 404 for an album
that is not the user's). The header has the language flags on the left (like the front page and Start), the album's name ("My cards" / "Mine kort") and a back arrow to Start on the right (the Check page's header is the same, with the saved cards icon before the back arrow).

- **Thumbnails.** The cards as small pictures with their name under each, newest added first. The picture is TCGdex's small size
  (the stored `.../high.webp` address is read as `.../low.webp`), loaded lazily. The grid fits as many 100 px-wide thumbnails as there
  is room for: three on a normal phone, never fewer than two, seven or more on desktop. Long names are cut after two lines; a card
  without a picture address gets a dashed "?" box.
- **Version icon.** Each thumbnail has a small icon in its bottom-right corner showing which version the user said they have: normal,
  holofoil or reverse holofoil (the same icons as in the "which version" choice, `_FinishSprite`), with the name as tooltip and screen
  reader text. Cards saved before the version was chosen (finish unknown) have no icon.
- **Empty slots.** The last row is filled up with empty, dashed card slots (`wwwroot/js/album.js`), so an album with few cards does not look
  bare. The script reads the real number of columns, which depends on the screen, and redoes the slots when the window changes size.
- **Paging.** `Album:PageSize` cards per page (50 by default, kept between 1 and 200); `?p=2` is the page. Below the cards: Previous, the
  first, last and nearby page numbers, Next. A page number that is too high goes to the last page, a bad one to page 1.
- **Empty.** Without cards it says so and offers "Check a card". Looking never creates an album: the default album is created when the
  first card is added.
- **Not yet:** the cards are not clickable. Showing a card's details comes later.
- Reading is `AlbumService.GetPageAsync`: it fetches only id, name and picture address, so the price data is not loaded for a list.

## Project layout

```
Pages/
  Index.cshtml(.cs)          Front page: the login box
  Account/                   Login, Register (with birthday), ForgotPassword, ResetPassword, VerifyEmail, Logout
  SetLanguage.cshtml(.cs)    Where the language flags post to
  Start.cshtml(.cs)          The page after login: Check a card / Fair swap? (login required)
  Check.cshtml(.cs)          Check a card: take or upload a photo, see the card and price (login required)
  Album.cshtml(.cs)          The album: card thumbnails with names, paged (login required)
  ProtoUpload.cshtml(.cs)    Card recognition test page (login required)
  Shared/_Layout.cshtml      HTML shell (language attribute, optional Head section)
  Shared/_LayoutHome.cshtml  The cheerful look used by the front page and the account pages
  Shared/_LanguageSwitch.cshtml   The language flags
  Shared/_StartArtCheck.cshtml, _StartArtSwap.cshtml   The two pictures on the Start page
  Shared/_Hero.cshtml        Flags, title, tagline and the illustration (_HeroArtLogin, or _HeroArtRegister when a page sets ViewData["HeroArt"] = "register")
  Shared/_FormGuard.cshtml   The hidden trap field, time stamp and Turnstile widget inside the public forms
  Shared/_Turnstile.cshtml   The Cloudflare Turnstile widget
  Shared/_LoginSheet.cshtml  The login box
  Shared/_PasswordField.cshtml  A password field with the show/hide eye button
Services/
  LoginServerClient.cs       Login, create user, confirm email, password reset (calls the LoginServer)
  ClientIp.cs, EmailAddress.cs, PasswordRules.cs, RememberedEmailCookie.cs   Small helpers for the login
  UserPreferences.cs         Reads and saves a logged-in user's preferences (the preferred language)
  UserProfiles.cs, BirthDate.cs   Saves and checks the birthday given at sign-up
  CardChecker.cs             Photo in, card and price out, for the Check page
  FormGuard.cs               Trap field and time stamp check for the public forms
  Turnstile.cs               Cloudflare Turnstile: settings and the server-side token check
  FormChecks.cs              Runs the form guard and Turnstile for a form post
  CardImagePreprocessor.cs   Find the card, straighten it, build the compact image (OpenCvSharp)
  OllamaLlmService.cs        Vision model calls and model list (ILlmService)
  TcgDexCardDataProvider.cs  Card lookup, set cross-check, stale-price retry (ICardDataProvider)
  CardData.cs                The card and its prices
  PriceCalculator.cs         Estimated price per finish, outlier filtering
  FrankfurterExchangeRateService.cs   USD -> EUR and EUR -> DKK rates, cached (IExchangeRateService)
  TradeEvaluator.cs          Fair-trade check and TradeOptions
Localization/              AppLanguages (supported languages), LanguageCookie (sets the language cookie)
Data/                      AppDbContext, UserPreference (table user_preferences) and Migrations/
Resources/                   SharedResource.resx (English) and SharedResource.da.resx (Danish)
Scripts/deploy.ps1           IIS setup and deployment
wwwroot/css/home.css         Front page styles
wwwroot/js/password-toggle.js  The eye button on password fields
wwwroot/js/check.js, card-frame.js  The Check page script, and the camera outline and "card is in the frame" check
```

## Known limits

- There is no storage of trades or other per-user data yet, and no admin or profile pages. There is no bot check (such as
  Cloudflare Turnstile) on the login and create user forms, only the rate limit and the LoginServer's account lockout.
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
