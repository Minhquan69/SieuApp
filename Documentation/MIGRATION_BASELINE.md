# Migration Baseline

| Field | Value |
| --- | --- |
| Base project | `C:\iVistatech\SieuDuAn\iVista-Client-Application\iVista-Client-Application` |
| Base branch | `main` |
| Base commit | `063b2f387d26fe092f6170dd13472cd0e5b8f7fd` |
| Base working-tree entries at copy time | 37 (preserved; none discarded) |
| Isolated migration copy | `C:\iVistatech\SieuDuAn\iVista-Client-Application-Migrated` |
| Copy created | 2026-07-13, Asia/Saigon |
| Solution | `V3S.sln` |
| Target project | `V3SClient\V3SClient.csproj` |

## Copy exclusions

The copy excluded generated or environment-specific directories: `.git`, `.vs`, `bin`, `obj`, and `logs`.

## Rules

- The base project is read-only for the migration.
- All future implementation, builds, runtime checks, and documentation updates occur in this isolated copy.
- Recreate generated output by restoring packages and building this copy; do not copy build output from the base.
