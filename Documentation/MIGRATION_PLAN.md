# Migration Plan

## Guardrails

- Preserve all V3SClient pages, controls, namespaces, and navigation.
- Keep .NET Framework 4.8, WPF, XAML, and MVVM.
- Use isolated `_v3` views, view models, resources, and keys.
- Do not infer backend URLs, contracts, permissions, or external service credentials.

## Phase 1 — Discovery and documentation

Status: complete for the repositories currently supplied.

Completed: Git roots/status review, solution/project analysis, GitNexus indexing, current route/API/authentication analysis, V3SClient architecture review, vehicle-module preservation analysis, and VMS semantic-token mapping.

Known constraint: the current web source has no Vehicle List route. Registration routes and their APIs were removed in historical commit `afc3e02`; they are recorded as historical evidence only.

## Phase 2 — Isolated `_v3` theme

Pending. Create no resources until a concrete migrated screen is approved. Resources will use only `*_v3` keys and the semantic mapping in `VMS_COLOR_MAPPING.md`.

## Phase 3 — Shared infrastructure

In progress. `AuthenticationService_v3` reuses `ApiManager`, `GlobalUserInfo`, `VMBase`, `AsyncRelayCommand`, and logging without modifying them. GitNexus reports `ApiManager` as critical (107 upstream dependants) and `VMBase` as high impact, so both remain unchanged.

`LoginPage_v3`, `LoginWindow_v3`, and `LoginViewModel_v3` implement the isolated login/profile-selection flow. `UseLoginV3=false` preserves the existing `LoginWindow`; switching the setting to `true` selects the isolated window for controlled side-by-side runtime testing.

## Phase 4+ — Screen migration

Start only with a current, confirmed route/API/permission contract. For the vehicle module, obtain the current service base URL, token mechanism, endpoints, response schemas, realtime protocol, and approved navigation entry point. Then create a side-by-side `VehicleListPage_v3` and `VehicleListViewModel_v3`; do not replace `VVehicleRegisterManagement` or `ucWebVehicleRegisterManagement`.
