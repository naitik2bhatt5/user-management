# User Management (ASP.NET Core MVC + SQL Server, inline SQL)

A single-screen user manager: list users, add, edit, delete one, and delete many with checkboxes.
Data access is plain ADO.NET (`Microsoft.Data.SqlClient`) with inline, **parameterized** SQL. No Entity Framework.

## Run it (Visual Studio only, no SQL Server Management Studio needed)

1. **Prerequisites:** Visual Studio 2022 with the **ASP.NET and web development** workload. That workload includes .NET 8 and **SQL Server Express LocalDB**. If LocalDB is missing, open Visual Studio Installer → Modify → Individual components → tick *SQL Server Express LocalDB*.
2. Open `UserManagement/UserManagement.csproj` in Visual Studio and press **F5**.

That's it. On startup the app runs `database/CreateDatabase.sql` against LocalDB, which creates the `UserManagement` database, the `States` lookup (50 states + DC), the `Users` table and five sample users. The script is re-runnable, so every later start just confirms the database is there (`Data/DatabaseInitializer.cs`).

To look at the data inside Visual Studio: **View → SQL Server Object Explorer** → `(localdb)\MSSQLLocalDB` → Databases → UserManagement → Tables → right-click `dbo.Users` → *View Data*.

### Using a full SQL Server instead
Change `ConnectionStrings:UserManagement` in `UserManagement/appsettings.json`, for example:
- SQL Express: `Server=localhost\\SQLEXPRESS;Database=UserManagement;Trusted_Connection=True;TrustServerCertificate=True;`
- SQL login / Docker: `Server=localhost,1433;Database=UserManagement;User Id=sa;Password=...;TrustServerCertificate=True;`

The database is still created automatically. If you prefer to run the script yourself, set `"Database": { "AutoCreate": false }` and run `sqlcmd -S <server> -E -i database/CreateDatabase.sql`.

From the command line instead of Visual Studio: `cd UserManagement` then `dotnet run`, and open http://localhost:5180.

## Google address autocomplete and validation (optional)

When a Google API key is configured, the Add/Edit form behaves like Amazon or Walmart checkout:

- **Autocomplete:** typing in Address Line 1 shows matching U.S. street addresses with the typed part in bold. Pick one with the mouse, or with the arrow keys and Enter. The street, city, state, Zip and Zip+4 are filled in and briefly highlighted, then focus jumps to Address Line 2 for an apartment or suite.
- **Review your address:** on save, the address is checked with the Google Address Validation API (USPS CASS data).
  - Known address: saves straight away with a green "Verified address" badge.
  - Google corrected or completed something, for example a missing Zip+4: a dialog shows **Suggested address** next to **Address as entered**, with the changes highlighted.
  - Unknown address, or a building that needs a unit number: a dialog explains what could not be confirmed. The user can edit the address or save it as entered.
  - If Google is unreachable, saving is never blocked.
- **Verify address** button: checks the address at any time without saving.
- An existing user's saved address is not re-checked unless it is edited.

The API key stays on the server. The browser only calls `/Address/Autocomplete`, `/Address/Place/{id}` and `/Address/Validate` (`Controllers/AddressController.cs`), and those proxy to Google (`Services/GoogleAddressService.cs`). Without a key, these endpoints return 404 and the form works exactly as before.

### Getting a key
1. Go to https://console.cloud.google.com/, create a project and turn on billing. Google gives a free monthly credit.
2. Under **APIs & Services → Library**, enable **Places API (New)** and **Address Validation API**.
3. Under **APIs & Services → Credentials**, choose **Create credentials → API key**. Under *API restrictions*, limit the key to those two APIs. Because the key is only used from the server, don't add a website restriction.
4. Give the key to the app. Either:
   - In Visual Studio, right-click the project, choose **Manage User Secrets**, and paste:
     ```json
     { "GoogleMaps": { "ApiKey": "YOUR_KEY" } }
     ```
     This keeps the key out of Git.
   - Or put it in `appsettings.json` under `"GoogleMaps": { "ApiKey": "..." }`. Don't commit that file with the key.
5. Restart the app. The address field placeholder changes to "Start typing your street address".

## Project layout

```
database/CreateDatabase.sql        tables, constraints, state seed, sample rows
UserManagement/
  Program.cs                       DI + MVC routing (default: Users/Index)
  Data/DatabaseInitializer.cs      runs CreateDatabase.sql at startup (creates DB, tables, seed data)
  Data/SqlConnectionFactory.cs     opens SqlConnection from appsettings
  Data/UserRepository.cs           all user SQL: select, insert (OUTPUT INSERTED.UserId), update, delete, bulk delete
  Data/StateRepository.cs          state dropdown + existence check
  Models/User.cs                   fields, validation attributes, server-side business rules, normalization
  Controllers/UsersController.cs   Index, Create, Edit (form posts), Delete + DeleteMultiple (AJAX/JSON)
  Controllers/AddressController.cs Google proxy endpoints (autocomplete, place details, validate)
  Services/GoogleAddressService.cs calls Google and turns its verdict into verified / suggested / unverified
  Views/Users/Index.cshtml         the grid
  Views/Users/Form.cshtml          shared Add/Edit form
  wwwroot/js/site.js               fetch helper (antiforgery header, error handling), flash messages, debounce
  wwwroot/js/users-list.js         grid behaviour
  wwwroot/js/user-form.js          form behaviour
  wwwroot/js/address-autocomplete.js  address suggestions dropdown + "Review your address" dialog
```

## How each requirement is met

| Requirement | Implementation |
|---|---|
| List of users | `UsersController.Index` → `UserRepository.GetAllAsync` (`SELECT ... ORDER BY LastName, FirstName`) |
| Add / Edit | One `Form.cshtml` for both; POST → validate → `INSERT` / `UPDATE`, then redirect with a flash message |
| Delete user | Row **Delete** button → confirm → `fetch` POST `/Users/Delete/{id}` → row fades out, no page reload |
| Delete multiple | Checkboxes + **Delete selected (n)** → confirm listing the names → POST `/Users/DeleteMultiple` with `{ ids: [...] }` → one `DELETE ... WHERE UserId IN (@Id0, @Id1, ...)` |
| Required fields | Enforced three times: JS, DataAnnotations on `User`, and `NOT NULL` in SQL |
| State dropdown | Loaded from the `States` table; server also checks the code exists (FK in SQL too) |
| Zip5 / Zip4 | 5 digits required (not `00000`), 4 digits optional; CHECK constraints in SQL |

## JavaScript features (the part being judged)

**Users list (`users-list.js`)**
- Select-all checkbox with a true **indeterminate** state when only some rows are checked.
- **Shift+click** selects a range of rows.
- Live **search** (debounced) across name, phone (formatted or raw digits), address, city, state, zip, DOB. Multiple words are AND-ed. Esc clears.
- Select-all only affects visible rows, and rows hidden by the search are unchecked, so a bulk delete never removes users you can't see.
- **Sortable columns** (click or Enter on a header), ascending/descending, dates sorted chronologically, `aria-sort` set.
- **Delete selected (n)** button shows a live count and is disabled when nothing is selected.
- AJAX deletes send the antiforgery token in a header; buttons are locked while a request is in flight; rows animate out; counts, empty state and "no matches" state update; a message reports if some users were already deleted by someone else; network and server errors are shown instead of failing silently.

**Add / Edit form (`user-form.js`)**
- Validation on blur, then live as you type once a field has been touched; on submit every field is checked, an **error summary** lists problems (click to jump), and focus moves to the first invalid field.
- Rules match the server: names (letters, spaces, `-`, `'`, `.`), max lengths, DOB must be a real date, not in the future and not before 1900, phone must be a valid 10-digit U.S. (NANP) number, zip rules above.
- **Phone mask**: formats to `(212) 555-0143` as you type, keeps the caret in the right place, accepts pasted `+1 212-555-0143`.
- **Zip**: digits only; typing the 5th digit jumps to Zip+4; Backspace in an empty Zip+4 jumps back; pasting `12345-6789` fills both boxes.
- DOB picker capped at today; shows the computed **age**.
- **Unsaved-changes guard** on Cancel and on leaving the page; **double-submit guard** ("Saving..." and disabled button).
- Server-side errors (if JS is bypassed) are picked up on load and shown in the same places and summary.

## Security / data notes
- Every value goes through `SqlParameter` with an explicit type and size; the bulk delete generates only parameter *names*.
- All POSTs (form and AJAX) use `[ValidateAntiForgeryToken]`.
- Input is normalized server-side (trim, collapse spaces, phone stored as 10 digits) before validation and storage.
- The server re-validates everything; the JS is for user experience, not trust.
