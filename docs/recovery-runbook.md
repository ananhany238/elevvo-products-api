# PostgreSQL recovery runbook

For the Products API database (`productsdb`). Written for this exercise: the numbers and names are examples, and
section 7 explains what a real production setup would do differently.

## 1. Objectives

| Term | Meaning | Target for this exercise |
|---|---|---|
| **RPO** (recovery point objective) | How much recent data we can afford to lose | 24 hours (one backup per day) |
| **RTO** (recovery time objective) | How long recovery may take | 15 minutes |

- **RTO is measured** by the restore step of the drill (`Invoke-RecoveryDrill.ps1` prints it).
- **RPO is estimated** as: time of the failure minus the time the latest backup's snapshot started. Everything written
  in between is lost. With one backup per day, the worst case is just under 24 hours.

## 2. What exists

| Item | Where |
|---|---|
| Backups (`pg_dump -Fc` custom format) | `backups\<database>-<UTC timestamp>.dump` (git-ignored) |
| Manifest per backup (row counts, content checksums, sample records) | `backups\<file>.dump.manifest.json` |
| Logs with UTC timestamps | `backups\backup.log`, `restore.log`, `recovery-drill.log` |
| Scripts | `scripts\backup\` |
| Retention | newest 5 backups are kept, older ones are deleted by the backup script |

Connection settings come from the environment variables `PGUSER`, `PGPASSWORD`, `PGDATABASE`. Credentials and
backups are never committed.

## 3. Taking a backup

```powershell
. .\scripts\backup\env.example.ps1          # sets PGUSER, PGPASSWORD, PGDATABASE (dev values)
.\scripts\backup\Backup-Database.ps1 -Keep 5
```
Exit code 0 means the dump was created, is not empty, and `pg_restore --list` can read it. Any failure exits with 1
and a line starting with `[ERROR]` in `backups\backup.log`.

**Scheduling (Windows):** run the same command daily with Task Scheduler, for example
`powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\path\to\repo\scripts\backup\Backup-Database.ps1`,
with the three environment variables set for that task's user. Check the exit code and the log every day.

## 4. Restoring after a data-loss incident

1. **Stop the damage.** Stop the API so nothing writes to the damaged database. Note the time: this is the failure time.
2. **Pick the backup.** `Get-ChildItem backups\*.dump | Sort-Object LastWriteTime -Descending`. Use the newest one
   unless you know it was taken after the damage.
3. **Restore into a NEW database. Never restore over the damaged one.**
   ```powershell
   .\scripts\backup\Restore-Database.ps1 -BackupFile backups\productsdb-<timestamp>.dump
   ```
   This creates `productsdb_restore_test`, restores into it, compares it with the manifest (row counts, checksums,
   sample records) and runs smoke queries. Exit code 0 means it passed.
4. **Smoke-test the application against the restored data.**
   ```powershell
   $env:ConnectionStrings__Default = "Host=localhost;Port=5433;Database=productsdb_restore_test;Username=$env:PGUSER;Password=$env:PGPASSWORD"
   dotnet run --project src/ProductsApi
   ```
   Then open `GET /api/products` and check that the data looks right.
5. **Switch over** (only after step 3 passed and step 4 looks right): stop the API, then in `psql` run
   `ALTER DATABASE productsdb RENAME TO productsdb_damaged;` and
   `ALTER DATABASE productsdb_restore_test RENAME TO productsdb;`, and start the API normally. Keep
   `productsdb_damaged` until the investigation is finished.
6. **Record the result.** RTO = from the moment you declared the incident until the API served correct data again.
   RPO = failure time minus the backup's snapshot time (`snapshotStartedUtc` in the manifest). Take a fresh backup.

## 5. Testing the process (the drill)

Do this regularly: a backup that was never restored is only a hope.

```powershell
. .\scripts\backup\env.example.ps1
.\scripts\backup\Invoke-RecoveryDrill.ps1 -AlsoInContainer
```
It works only on disposable sample data. It creates a database, backs it up, writes extra rows, drops the tables
(the "data-loss event"), restores, validates, proves the validation can fail (negative control), and repeats the
restore in a brand-new PostgreSQL container. It writes a report to `docs\drill-output\` and exits 1 on any failure.

## 6. Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| `failed to connect to the docker API` | Docker Desktop is not running. Start it and wait for the engine. |
| `Missing environment variables` | Run `. .\scripts\backup\env.example.ps1` in the same window. |
| `No manifest next to the backup` | The backup was not made by `Backup-Database.ps1`. Take a new backup. |
| `VALIDATION FAILED ... checksum differs` | The restored data is not what was backed up. Do not use it. Try an older backup. |
| `Refusing to touch database` | Safety guard: only `*_restore_test` and `*_drill` databases can be overwritten. |
| `pg_restore: error` mentioning a version | Dump and restore tools must be the same or a newer major version. Match the image tag. |

## 7. Limitations of this exercise compared with production disaster recovery

- **Same machine, same disk.** The backups sit next to the database. A disk failure, ransomware or a stolen laptop
  destroys both. Production keeps copies in another location or region (the 3-2-1 rule: 3 copies, 2 media, 1 off-site).
- **RPO equals the backup interval.** A daily dump loses up to a day of data. Production uses continuous WAL
  archiving for point-in-time recovery (PITR), with tools such as pgBackRest or WAL-G, to get an RPO of seconds.
- **Tiny data, tiny RTO.** The sample database restores in seconds. Real databases take minutes to hours, and the
  RTO also includes detection, decisions, DNS or connection-string changes, and warming caches.
- **Logical dump, not a physical backup.** `pg_dump` is consistent but slow for big databases and does not include
  cluster-wide objects such as roles. A full recovery plan also covers those.
- **Manifest taken slightly before the dump.** Row counts are read just before `pg_dump` runs. If the application
  wrote in between, validation would report a difference. The drill uses a database nothing else writes to.
- **No encryption, no access control on backup files.** Backups contain all data, including anything sensitive.
  Production encrypts them and restricts who can read and delete them.
- **No monitoring or alerting.** Here a human reads the exit code and the log. Production alerts when a backup is
  late, fails, or restores badly, and tests restores on a schedule.
- **Credentials in environment variables** are better than files in git, but a production system uses a secret
  manager and least-privilege backup roles.
- **One server, no failover.** There is no replica to promote. Production usually has streaming replication, so
  many failures recover in seconds without restoring from a backup at all.
- **Simulated failure.** Dropping tables is a clean, instant failure. Real incidents (corruption, bad deployments,
  a slow data leak) are messier and often discovered late, so the newest backup may already contain the damage.
- **No people or process.** A real runbook also names who decides, who communicates, and how an incident is reviewed.
