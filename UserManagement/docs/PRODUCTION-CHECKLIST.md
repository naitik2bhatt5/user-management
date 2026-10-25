# Before going to production

The demo setup is fine for trying things out. Work through this list before real users and real data.

## Admin sign-in
- The **Address providers** page requires sign-in. Set the password outside the repo:
  ```bash
  dotnet user-secrets set "Admin:Password" "a long passphrase"
  ```
  On a server, use the environment variable `Admin__Password` instead. Optionally set `Admin__Username` (default `admin`).
- With no password set, nobody can sign in (the page stays locked).
- Five wrong attempts lock that client out for 15 minutes.
- Serve the site over **HTTPS** in production so the sign-in cookie and password are encrypted in transit.
- The **Users** pages are still open to anyone who can reach the site. Add sign-in there too if they hold real personal data.

## API keys at rest
- Provider keys are stored encrypted (ASP.NET Data Protection). The **encryption key ring** must survive restarts and redeploys, or saved keys become unreadable and must be re-entered.
- Set a fixed, backed-up folder for it (outside the web root):
  ```json
  "DataProtection": { "KeysPath": "D:\\AppKeys\\UserManagement" }
  ```
  On Windows the key files are additionally encrypted for that machine. Changing this setting on an existing install starts a new key ring, so re-enter the provider keys once.
- Several servers behind a load balancer must share the same folder.

## Amazon Location
- Key limited to the three Places actions, with an expiry date. Calendar a renewal; an expired key makes suggestions stop working with 401/403.
- Set an AWS budget alert.
- **Storage terms:** this app saves the chosen address in its own database. AWS has separate rules and pricing for storing Places results. Read the current Amazon Location terms and pricing and confirm your use fits (or switch the relevant calls to the "storage" intended use) before launch.
- Google has comparable rules for storing Places data. Check them too if Google stays in use.

## Address verification
- Google: USPS-style validation. Amazon: geocode match only (cannot tell that an apartment number is missing). Pick the provider that matches your accuracy needs.
- Verification never blocks saving a user when the provider is down.

## General
- Keep real keys out of `appsettings.json` and out of git. Use user secrets or environment variables.
- Back up the database, and rotate API keys on a schedule.
