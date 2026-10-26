# Contributor reporting setup

The contributor flow uses the existing `DailyReport`, `KPI_Daily` and `KPI_Master` tables. `KPI_Master` contains the monthly plan; each `DailyReport` retains its own target snapshot and date-specific actuals, which are copied into the final `KPI_Daily` snapshot when approved.

## Run order on an existing database

1. Treat `full-schema.sql` as a schema snapshot; the existing database and applied migrations are authoritative.
2. Apply `009_plant_scoped_operational_configuration.sql` before the new report-target migration, if it has not already been applied.
3. Run `010_daily_report_kpi_plan_targets.sql` to add the separate UPPH, OS&D and OT target snapshot columns to `DailyReport` and `KPI_Daily`.
4. Run `015_daily_report_upload_locks.sql` to enable report-section locks.
5. Run `seed.sql` after the base/catalog data exists. It also creates September 2026 sample `KPI_Master` rows for every active model/line combination.
6. Apply `017_gigabyte_fatp_pvt_build_model.sql` so the Gigabyte `PVT Build` row in the supplied daily report can be matched during FATP import.
7. If your database already contains the tables, do not recreate the schema; apply only the required migrations and seed section.

Contributors can deactivate an unused line for a report date from the Daily Report matrix and reactivate it later. The line marker does not change the saved report status or delete draft values; lines with submitted or approved records cannot be deactivated.

## Contributor API flow

- `GET /api/catalog/customers`
- `GET /api/daily-reports/config`
- `GET /api/daily-reports/kpi-plan`
- `GET /api/daily-reports/draft`
- `GET /api/daily-reports/line-status`
- `POST /api/daily-reports/draft`
- `DELETE /api/daily-reports/draft/{dailyReportId}`
- `POST /api/daily-reports/submit`
- `GET /api/daily-reports/upload-locks`
- `POST /api/daily-reports/upload-lock`
- `DELETE /api/daily-reports/upload-lock?customerId=...&categoryId=...&reportDate=...&uploadType=...`

The plan lookup always scopes the plant from the authenticated user and matches the selected customer, category, line, model and report month. It returns no plan rather than falling back to a plan from another context.

The report form prefills plan fields for new entries only. Existing reports keep their saved daily target snapshots; actuals remain on their date-specific report. Contributors can change only the allowlisted monthly KPI target fields while saving a draft or submission. The server validates the plan context and the authenticated contributor's customer/plant scope, and uses `KPI_Master.UpdatedAt` as an optimistic concurrency check. A stale edit returns a conflict instead of replacing a newer plan.

Saved drafts and rejected reports can be deleted from the model header using its trash button. Report-section fields remain editable until their matching section is locked. Use the lock control to lock the selected section and its unlock action to restore editing; unlocking `all` removes every section lock for that report date and category.

Complete Excel import reads the dated daily-report sections from the workbook, maps each row by line number, SAP location and model, and imports the report fields present on those rows. When the Customer cell is blank or contains a placeholder, the selected customer/category plus the line and model catalog mappings are used; rows naming another customer are excluded. Total rows are not imported.

`UPPH Installed`, `Planned OT (Hrs)`, and the daily OS&D reporting values are not monthly targets and are not copied from KPI plans. A plan edit and its daily report save occur in the same SQL transaction. Approval and admin reporting continue to use their existing workflows.
