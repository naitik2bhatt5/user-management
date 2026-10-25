# Amazon Location Service setup (address suggestions)

This app can use **Amazon Location Service** instead of (or alongside) Google for address
autocomplete and address checking. You enter the key on the **Address providers** screen;
no code change or restart is needed to switch.

> AWS console labels change from time to time. If a button is named slightly differently,
> look for the same idea. Check current prices on the Amazon Location Service pricing page.

## 1. Get an AWS account and pick a region

1. Sign in at <https://console.aws.amazon.com> (create an account first if you have none).
2. In the top-right region menu choose the region you want, for example **US East (N. Virginia) `us-east-1`**.
3. **Write the region code down.** The API key only works in the region where it was created,
   and the same code goes into the app.

## 2. Create an API key

1. Open the console, search for **Amazon Location Service**, and open it.
2. In the left menu choose **API keys**, then **Create API key**.
3. **Name:** something like `usermanagement-places`.
4. **Resources / APIs:** allow the **Places** API. The app uses these actions:
   - `geo-places:Autocomplete`: the suggestions while typing
   - `geo-places:GetPlace`: filling the form after a suggestion is picked
   - `geo-places:Geocode`: the "Verify address" check

   Do not allow anything else (maps, routes, tracking). A narrow key limits the damage if it leaks.
5. **Referer restrictions:** leave empty. The app calls AWS from the server, and server calls
   carry no browser referer, so a referer rule would block every request.
6. **Expiration:** set an expiry (for example 90 days) and put the renewal date in your calendar.
   Expired keys fail with HTTP 403.
7. Choose **Create API key**, then open the key and **copy the key value** (a long string).
   Treat it like a password.

## 3. Add it in the app

1. Open the app and click **Address providers** in the header.
2. **+ Add provider**:
   - **Provider:** Amazon Location Service
   - **Name:** e.g. `Amazon - production`
   - **API key:** paste the key
   - **AWS region:** the code from step 1 (e.g. `us-east-1`)
3. **Save**, then click **Test** on that row. A green message means AWS accepted the key and returned suggestions.
4. Click **Activate**. The User form now uses Amazon (the form says "powered by Amazon").

To go back to Google: **Activate** the Google row. Only one provider is active at a time.
**Deactivate** turns suggestions off completely.

The same screen manages Google: add a row with Provider = Google Maps Platform and its key.
Until you save any row, the app keeps using the old `GoogleMaps:ApiKey` setting.

## 4. Check it from the command line (optional)

Replace the region and key, then run:

```bash
curl -s -X POST "https://places.geo.us-east-1.amazonaws.com/v2/autocomplete?key=YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{"QueryText":"350 5th Ave New York","MaxResults":3,"Filter":{"IncludeCountries":["USA"]}}'
```

You should get JSON with `ResultItems`.

## 5. Troubleshooting

| Symptom | Likely cause |
|---|---|
| **Test** says `returned 403` | Key expired, wrong region, or the Places action is not allowed on the key |
| **Test** says `returned 400` | Region or key is mistyped |
| Key accepted, zero suggestions | Key lacks `geo-places:Autocomplete` |
| Works in curl, fails in the app | Referer restriction on the key; remove it |
| Row shows "(unreadable - re-enter key)" | Encryption keys of the web server changed; edit the row and paste the key again |

## 6. Cost and safety

- **Set a budget:** AWS Billing, then **Budgets**, then a monthly cost budget with an email alert.
- **Rotate keys** before they expire: create a new key, add it as a new provider row, activate it, then delete the old row and key.
- **Storage terms:** this app saves the chosen address in its own database. AWS has separate terms and
  pricing for storing Places results ("intended use: storage"). Confirm with AWS pricing and terms that your
  usage fits before going to production.
- **Sign-in:** the Address providers page needs the admin sign-in; see PRODUCTION-CHECKLIST.md for setting the password.

## Differences from Google in this app

- **Suggestions and autofill:** equivalent.
- **Verify address:** Google uses USPS-style address validation. Amazon has no such service, so the app geocodes the
  address and judges the match quality. It can confirm an address exists and fix its formatting,
  but it will not tell you that an apartment number is missing.
