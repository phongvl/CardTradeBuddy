# CardTradeBuddy: context for Claude

ASP.NET Core Razor Pages (.NET 10) site, `cardtradebuddy.com` / `www.cardtradebuddy.com`. Mobile-first, cheerful front page with login.
Recognises a Pokemon card from a photo (local Ollama vision model), looks it up on TCGdex, estimates the price and judges whether a
trade is fair. English and Danish. The README is the full reference; this file is the short version plus things that are easy to get wrong.

Repo: `https://github.com/phongvl/CardTradeBuddy.git`, branch `main`. Related repo: the user's own **LoginServer**
(`C:\VS Code\LoginServer`, dev `https://localhost:5001`, prod `https://secure.phong.dk`), which owns users and passwords.

## How the user wants to work

- **Ask before committing or pushing.** They say "commit and push" / "commit and push all" when ready. Commit messages end with the
  `Co-Authored-By` line from the session's attribution reminder.
- **Ask before deploying.** Deploying needs an elevated PowerShell, which a Claude session is not: give the command, they run it.
- Do not touch production data or settings on your own (production database, `appsettings.Production.json`, the LoginServer). Give
  them the steps or the SQL instead. Do not print or repeat secrets.
- Plain, direct answers. Keep README in step with code changes (it is detailed on purpose).
- Windows, PowerShell 5.1 and Git Bash. PowerShell 5.1 quirks: no `&&`, native-argument quoting is poor (pass `"` as `\"` to psql), use
  `HttpClient` with `AllowAutoRedirect=false` to see redirects, set TLS 1.2 for HTTPS calls. For multi-line Python edits write a script
  file in the scratchpad and run it (heredocs with many quotes broke in Bash once).

## Layout

```
Pages/            Index (front page), Account/ (Login, Register, ForgotPassword, ResetPassword, VerifyEmail, Logout), ProtoUpload (card
                  scanner, login required), SetLanguage, Shared/ (_Layout, _LayoutHome, _Hero + _HeroArtLogin/_HeroArtRegister,
                  _LoginSheet, _PasswordField, _FormGuard, _Turnstile)
Services/         LoginServerClient, UserPreferences, UserProfiles, BirthDate, FormGuard, FormChecks, Turnstile, ClientIp, EmailAddress,
                  PasswordRules, RememberedEmailCookie; card pipeline: CardImagePreprocessor, OllamaLlmService, TcgDexCardDataProvider,
                  PriceCalculator, TradeEvaluator, FrankfurterExchangeRateService
Data/             AppDbContext, UserPreference (user_preferences), UserProfile (user_profiles), Migrations/
Localization/     AppLanguages (en, da), LanguageCookie
Resources/        SharedResource.resx (English) + SharedResource.da.resx. Add every new text to BOTH.
Scripts/deploy.ps1  IIS deploy; wwwroot/css/home.css  all front page styles; wwwroot/js/password-toggle.js
```

## Run and test locally

- Debug in VS Code with the `https` profile: `https://localhost:7139`. The user often has a debug session running, which locks
  `bin\Debug`: build to a temp folder instead (`dotnet build -o "$env:TEMP\ctb-verify"`), copy `appsettings.Development.local.json`
  there and run the exe on a spare port (e.g. `ASPNETCORE_URLS=http://localhost:5301`, `ASPNETCORE_ENVIRONMENT=Development`). EF tools:
  `dotnet ef ... --configuration Release` for the same reason.
- Test with the built-in browser at widths 390 (phone), 768, 1280 and 1440. Reset the viewport to the desktop preset afterwards.
- The rate limiter allows 10 auth requests a minute per address: set `RateLimit__AuthPerMinute=500` when scripting tests.
- The dev LoginServer must be running for logins. Do not guess the user's dev password: register a throwaway user through the app
  instead (`@example.test` address) and delete test rows afterwards.
- Ollama (default model `qwen3-vl:8b`) runs locally for the card scanner; OpenCvSharp4 does the card detection.

## Configuration and secrets (never commit these)

| What | Dev | Production |
| --- | --- | --- |
| LoginServer client secret (`Login:ClientSecret`) | `appsettings.Development.local.json` (git-ignored) | `appsettings.Production.json` on the server |
| Database (`ConnectionStrings:Default`) | same file, database `cardtradebuddy_dev` on local PostgreSQL | same, `cardtradebuddy_prod`; deploy reads `Scripts\prod.connection.local` (git-ignored) |
| Turnstile (`Turnstile:SiteKey/SecretKey`) | Cloudflare's test keys in the local file | keys given to deploy once |
| `Login:VerifyEmail` (email confirmation) | local file | `-LoginVerifyEmail true` once |

- `appsettings.Development.local.json` is added last, so it overrides environment variables and user-secrets. It is excluded from build
  output and publish. `dotnet user-secrets` written from the MSIX-packaged Claude app is invisible to VS Code, so do not use it.
- `deploy.ps1` writes `appsettings.Production.json` (admins only), never overwrites it, and keeps stored values when a parameter is not
  given: each secret/flag is passed **once**. Tables are created at startup (`Database.MigrateAsync`), so a new database only has to exist.

## How things work

- **Login:** this app has its own login page and checks passwords server-to-server with the LoginServer (password grant; scopes
  `users.create`, `users.verify`, `passwords.reset` with client credentials). Cookie auth `cardtradebuddy.session`, "Remember me"
  = 30 days plus an email-prefill cookie. No account enumeration: "already exists" and "created" look the same.
- **Create user:** birthday required, minimum age `Account:MinimumAge` (13). Saved in `user_profiles` only for a brand-new account.
  Email confirmation is off by default (`Login:VerifyEmail`).
- **Language:** `?culture`, then the `.AspNetCore.Culture` cookie, then Accept-Language. A logged-in user's pick is saved in
  `user_preferences` and applied at the next login. Database trouble never blocks a login or a page (best effort, logged).
- **Form protection:** anti-forgery on every POST (Razor Pages default), form guard (hidden trap field + signed time stamp, 2 s to 8 h)
  and Cloudflare Turnstile on the anonymous forms, rate limit `auth` on the account pages. `FormChecks.CheckAsync` runs guard + Turnstile.
  Turnstile is off when its keys are not set.
- **Emails** (reset, confirmation) are sent by the LoginServer through Resend from the app's sender address
  (`support@cardtradebuddy.com`, set in the LoginServer admin). The Resend key lives only in the LoginServer.
- **Desktop CSS** breakpoints in `home.css`: under 560 px phone sheet; 560-899 px tablet; 900 px and up two columns. The mobile base
  sizes are deliberate (16 px inputs so iOS does not zoom): scale desktop by editing only the `min-width: 900px` block.

## Production state (as last known, 2026-10-22)

- IIS site `CardTradeBuddy` at `C:\inetpub\wwwroot\CardTradeBuddy`, app pool of the same name, HTTPS through win-acme. LoginServer is
  at `C:\inetpub\wwwroot\LoginServer` on the same server and the same PostgreSQL server (roles `loginserver_app`, `cardtradebuddy_app`).
- The app is registered in the production LoginServer as client id `cardtradebuddy`; the Resend key allows all domains.
- Pending for the user to run (elevated): `.\Scripts\deploy.ps1 -LoginVerifyEmail true -TurnstileSiteKey '...' -TurnstileSecretKey '...'`,
  which also ships the larger desktop design, language preference, Create user with birthday, form guard and Turnstile.
- IIS note: data-protection keys are ephemeral (event log says "ephemeral key repository"), so an app restart can invalidate login
  cookies and anti-forgery tokens. Persisting the keys (for example in the database) is a known open improvement.
- Existing users created before the birthday was asked for have no `user_profiles` row.

## Known pitfalls

- A local-only setting fix does not reach production: always say what the user has to run on the server.
- Browser zoom is per origin, so localhost and production can look different in size on the same CSS.
- Changing production data (the LoginServer database, user passwords, locks) is blocked by the permission system and is the user's call:
  give them the SQL. The one exception so far was creating the empty `cardtradebuddy_prod` database and role, done on their explicit request.
- `Scripts/*.local` and `appsettings.Development.local.json` are git-ignored on purpose: check `git status` before every commit.
