# Dixon Command Center API

The API uses the existing SQL Server schema in `Dixon_Command_Center`. Create or verify tables with `Database/schema.sql`. For an existing database, run `Database/007_plant_customer.sql`, then `Database/008_super_admin_hierarchy.sql`, before `Database/seed.sql` or starting the updated API. The first migration backfills plant/customer mappings. The second adds the `SuperAdmin` role and promotes the first existing Admin to SuperAdmin; remaining Admins are plant-scoped. Then use `Database/seed.sql` for the roles, plant, plant/customer assignments, categories, models, and production lines. The seed is rerunnable and includes a guarded correction for the existing Acer-SMT/Gigabyte-FATP line mapping error. On a new database, run `Database/schema.sql` and `Database/seed.sql`; initial setup creates the first SuperAdmin.

For an existing database, run `Database/013_user_profile.sql` to add phone verification fields and profile photos before using the profile page. Run `Database/014_approval_batches_and_annotations.sql` to add submission-cycle IDs and field-level review audit before using consolidated Approver reviews.

Run `Database/016_daily_report_non_numeric_values.sql` on existing databases before deploying the API version that supports `N/P` and `N/A` values in daily reports.

## Viewer access assignment rollout

For an existing database, review and run `Database/018_viewer_access_assignments.sql`, then `Database/019_viewer_plant_customer_assignments.sql`, as part of a planned rollout. Migration 018 creates explicit All Plants / All Customers flags and normalized plant/customer assignment tables; it does not modify existing report or user rows. Migration 019 adds plant-specific customer assignments and preserves currently valid pairs from existing specific-plant Viewer configurations. The migrations intentionally do not infer a Viewer’s previous broad scope. Migration 018's final query lists Viewers without assignments; review that list and agree each user's intended scope with administrators before deployment. After deploying the API and Admin UI, assign every existing Viewer through Admin > Users & access before restoring their Viewer reporting access. Until an assignment is saved, the Viewer reporting APIs return forbidden. Plan for this explicit fail-closed transition; do not silently grant global access or allow Viewers to resume with an unreviewed scope.

Plant Admin is the existing plant-scoped `Admin` role. It can configure Viewers whose home plant is its own and assign only that plant; only SuperAdmin can grant All Plants or configure multiple plants. In Specific Plants mode, select one or more plants and choose each plant's customers independently. The same customer can be assigned at one selected plant and withheld at another. With All Plants + Specific Customers, the selected customer list applies wherever those customers are mapped, including future plant mappings. Selections are validated against active plants, customers, and `PlantCustomer` mappings, then saved atomically; obsolete pair assignments cannot grant access after mappings or plant access change.

Viewer plants, customers, daily progress, trends, and Excel exports all use the same database-backed assignment. Generic contributor/reference catalog and daily-report routes are not available to the Viewer role; other roles retain their existing route access.

## Local configuration

Set the SQL Server connection string in the environment so credentials are not committed:

```powershell
$env:ConnectionStrings__DixonDb = "Server=YOUR_SERVER;Database=Dixon_Command_Center;Trusted_Connection=True;TrustServerCertificate=True"
```

The API uses the `http` launch profile at `http://localhost:5211`. In development, it generates a temporary signing key if `Jwt:Secret` is not configured, so issued tokens expire or become invalid after the API restarts. For a stable local key, set a random secret before starting:

```powershell
$env:Jwt__Secret = "replace-with-a-long-random-secret"
dotnet run --launch-profile http
```

Outside development, `Jwt:Secret` is required. Configure `Frontend:Origin` if Vite is not running at `http://localhost:5173`.

Phone number changes are sent by SMS and saved only after a successful OTP verification. Configure Twilio-compatible credentials as environment variables before enabling this flow:

```powershell
$env:Sms__AccountSid = "your-account-sid"
$env:Sms__AuthToken = "your-auth-token"
$env:Sms__FromPhoneNumber = "+1XXXXXXXXXX"
$env:Sms__OtpSigningKey = "a-long-random-secret"
```

`Sms:OtpSigningKey` protects stored OTP hashes; `Jwt:Secret` is used as a fallback. SMS provider credentials are not stored in source control. Codes expire after five minutes, with a resend cooldown and hourly limit.

## API routes

- `GET /api/auth/setup-status` reports whether first-admin setup is required.
- `POST /api/auth/bootstrap` creates the first administrator exactly once.
- `POST /api/auth/login` returns a 30-minute bearer token.
- `GET /api/auth/me` returns the authenticated identity.
- `GET /api/catalog/customers` returns customers with models, categories, and production lines.
- `GET /api/admin/catalog/plants/{id}/customers` and `PUT /api/admin/catalog/plants/{id}/customers` manage a plant's customer associations (Admin role).
- `GET /api/admin/users`, `/roles`, and `/plants` are scoped to the Admin's plant; SuperAdmins can manage users across plants.
- `GET /api/admin/users/{id}/viewer-access` and `PUT /api/admin/users/{id}/viewer-access` read and replace a Viewer’s explicit plant/customer scope. In Specific Plants mode, the update body uses `plantCustomerAssignments` entries with `plantId` and `customerId`; Plant Admin assignments are limited to the Admin’s plant.
- `POST /api/admin/users` lets SuperAdmins create plant Admins. Plant Admins can create only non-admin roles in their own plant.
- `GET /api/approver/dashboard`, `/daily-reports`, and `/activity` return plant/customer-assigned metrics, paginated report entries, and review events for Approver pages.
- `GET /api/approvals/batch/{id}` returns all submitted line/model entries for a reporting batch. Its `/approve` and `/reject` actions record one transactional batch decision; reject requests can contain RED/YELLOW field annotations.
- Viewer reporting supports date-range trends scoped to the selected plant, brand and process. The period comparison page compares a selected month or year with the preceding period. `GET /api/viewer/export` fills the first sheet of `Templates/DixonDailyReportTemplate.xlsx`, retains the supplied workbook's styles and remaining sheet layouts, and clears their sample report values; the template is copied to build and publish output.

## Viewer analytics workspace

For an existing database, run `Database/020_viewer_analytics_saved_views.sql` before using personal saved views. This additive migration creates only the per-user analytics configuration table.

The Viewer-only analytics API uses the same assigned plant/customer scope as the existing Viewer endpoints and reads production/KPI snapshot values from `DailyReport` with `KPI_Daily` taking precedence when present:

- `GET /api/viewer/analytics/filters` returns dependent customer, category, line and model options.
- `GET /api/viewer/analytics/overview` returns current and preceding equivalent-range summaries, daily trends and customer/category/line breakdowns.
- `GET /api/viewer/analytics/chart-data` returns server-aggregated data for allow-listed dimensions, metrics and aggregation functions.
- `GET /api/viewer/analytics/data` returns filtered, searchable, sortable and paginated report rows.
- `GET /api/viewer/analytics/export` exports the complete filtered result to Excel, up to 10,000 rows.
- `/api/viewer/analytics/saved-views` supports user-owned list/create/update/delete operations; requests always use the authenticated user ID.

Saved-view filters are revalidated by analytics APIs when reopened. The existing daily Viewer report and its template-based Excel export remain available.

Run the frontend from `Command-Center-Frontend` with `npm run dev`. Set `VITE_API_URL` if the API is hosted at a different URL.

## Approval workflow

Run `Database/003_approval_queue.sql` once on the existing `Dixon_Command_Center` database.

API:
- `GET /api/approvals/pending` — pending reports for the logged-in Approver's assigned brands.
- `GET /api/approvals/history` — recently reviewed reports for assigned brands.
- `GET /api/approvals/{id}` — full KPI detail.
- `POST /api/approvals/{id}/approve` — approve and lock.
- `POST /api/approvals/{id}/reject` — reject with a required comment.
- `GET /api/contributor/dashboard?days=7` — contributor activity/history.

Plant customer access is controlled by `PlantCustomer`; individual user assignments remain controlled by `UserCustomer`. Both are enforced by the backend catalog/reporting APIs.

This backend uses parameterized ADO.NET SQL rather than EF Core entities/migrations. Apply numbered, idempotent SQL scripts for existing SQL Server databases. New submissions share a batch key within plant/date/customer/category/contributor scope; older pending entries retain one-row review behavior. Field annotations preserve the report row, model, KPI field, original value, reviewer comment, severity, and corrected value after resubmission.

## Daily Performance Excel import

On the Daily Report page, choose FATP or SMT in the process dropdown before uploading the workbook. The importer selects the matching UPPH (FATP) or CPH (SMT) section; if both metrics are present in one table, it uses the populated metric for each row. It matches rows using the selected customer, line, SAP location, and model, and skips rows that belong to the other process. CPH target subheadings may be `Target`, `Installed`, or `Ins`.

For the supplied September 29, 2026 report, the Gigabyte `FA-06 / PVT Build` row is in the FATP section. Run `Database/017_gigabyte_fatp_pvt_build_model.sql` on existing databases so its model is available to the importer. The workbook's `SMT-03 / AIO` row is not a Gigabyte row.

Quality, overtime, and TRC columns are optional in the Daily Performance workbook. The production importer reads Open WO and >7 Days WO quantities, including the `>7 Days WO (Sum of Bal Qty)` header used in some SMT workbooks. Imported UPPH Installed and Actual values are rounded to two decimal places. WIP Lying >3/>7 Days fields are not imported or shown in the report interface. Other fields not present are left unchanged. Standalone WO Closure, Quality, and IE workbooks are not imported through this Daily Report upload because WO Closure is an aggregate report without line/model keys and needs a separate destination.
