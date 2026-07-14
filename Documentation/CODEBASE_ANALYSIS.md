# Codebase Analysis

## Scope and evidence

- Source repository: `iVista-Client-WebApp` (`main`).
- WPF repository: `iVista-Client-Application\iVista-Client-Application` (`main`).
- GitNexus indexes: source 2,145 nodes / 5,287 edges; WPF 10,020 nodes / 15,700 edges.
- GitNexus graph queries work, but full-text/BM25 search is unavailable because the LadybugDB FTS extension is not installed.

## Current web application

The web client is a Vite React/TypeScript application, not a Next.js application. `App.tsx` defines the current routes: `/login`, `/select-client`, `/live`, `/emap`, `/playback`, and a catch-all not-found route. `ProtectedLayout` provides the shell; `RequireAuth` requires an access token and selected client profile.

Authentication uses `POST /api/auth/login`, persists `username`, `authToken`, `userId`, profiles, and selected profile through Zustand persistence, and clears all camera/live/stream state on logout or an `ivista-auth-error` event. Camera/profile loading uses `GET /api/user/profiles` and `GET /api/user/cameras?profile_id={id}` with a Bearer access token.

Live streaming uses the configured `/streams` base for connect/disconnect/ROI APIs and a WebSocket metadata manager. Playback and map are separate route modules. No current vehicle-list, vehicle-registration, or registration-history route exists.

## Historical vehicle module

Commit `afc3e02` removed registration features. Its parent contains two distinct routes: `/vehicle-registration` (an iframe to an external management UI) and `/registration-history` (external-submission history). The latter has filters, grouped device rows, server pagination, a detail/photo dialog, and SSE updates. These are historical contracts only; they must not be treated as current backend behavior until confirmed.

## Existing WPF application

`V3S.sln` contains `V3SClient` and `UnitTest`. `V3SClient` is a .NET Framework 4.8 WPF executable, root namespace `V3SClient`. `App` opens `LoginWindow`, then `MainWindow`. The shell is largely code-behind driven and uses `MainWindow.SelectdPage` for navigation.

Key extension infrastructure: `viewModels\VMBase`, `libs\RelayCommand`, `libs\AsyncRelayCommand`, `libs\ApiManager`, `libs\LoggerManager`, `libs\ToastManager`, `Services`, `styles`, and `UI\Converters`. `ApiManager` is a high-impact singleton (GitNexus: 107 upstream dependants; critical risk), so it must not be changed without a dedicated impact review.

The existing vehicle-registration page is `UI\Views\VVehicleRegisterManagement`, which hosts `ucs\ucWebVehicleRegisterManagement`. The control opens the discovered `_vehicleReg` endpoint in WebView2. It is selected by `btnRegDK` and must remain unchanged. `QL_BSX_DK` is another legacy plate-related page, but discovery has not established it as a replacement for the removed web registration-history module.

## Safe Phase-1 conclusion

No current native vehicle-list API contract or navigation target is available in the supplied source. Future implementation requires an approved current source/backend contract and an explicit side-by-side navigation decision.
