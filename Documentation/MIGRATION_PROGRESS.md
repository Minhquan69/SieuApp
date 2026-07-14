# Migration Progress

## Live View _v3

- 2026-07-14 stability pass: camera selection now reuses existing `LiveTile_v3`/GStreamer players by slot. Adding or removing a camera no longer reloads unrelated active tiles. Sidebar rows expose `Available`, `Selected`, `Connecting`, `Connected`, `Retrying` and `Error` states, with an explicit tile Connect action.
- Profile selection now first requests the web-compatible `/api/user/profiles` projection and falls back to the legacy authorized endpoint, fixing deployments where the legacy projection returned fewer clients than the web application.
- 2026-07-14 parity pass: replaced the single-player page with an isolated multi-camera workspace matching the React flow: header/bulk actions, grouped searchable sidebar, camera toggle/fill behavior, layout selector, custom 1-100 slots, tile states/actions, stream selection and fullscreen modes.
- Added `LiveTile_v3` and slot/group ViewModels. Layout changes retain assigned cameras; page unload cancels requests and disposes players, metadata subscriptions and ROI work without blocking the UI thread.
- Implemented disconnect/remove/ROI in `LiveStreamService_v3` and added `MetadataSocketService_v3` plus `AiMetadataOverlay_v3` for shared metadata subscription, reconnect, bounding boxes and ROI drawing.
- Debug x64 compiles with 0 errors after this parity pass. Release and final runtime results are tracked in `RUNTIME_VERIFICATION.md`.
- Restored the active `_v3` playback path to `WhepPlayer_v3`: GStreamer receives WHEP from MediaMTX after `POST /streams/connect`. Direct RTSP failed before SDP because the relay endpoint was unreachable from the workstation.
- Configured the `_v3` shell to load GStreamer from `C:\Program Files\gstreamer\1.0\msvc_x86_64` through `GStreamerRoot_v3`, with a validated fallback to the packaged `x64` runtime.
- Verified the installed GStreamer 1.28.5 runtime provides `rtspsrc`, H264/H265 depayloaders, D3D11 decoders, and `d3d11videosink`.
- Preserved `GstRtspPlayer_v3` as an unused direct-RTSP fallback; no backend endpoint was changed.
- Corrected the stream execution path after runtime logs proved that `https://portal.ivistatech.vn/streams/connect` returns HTTP 405. Authentication remains on the portal, while live stream requests now use the separately configured web stream proxy at `http://localhost:3000/streams`.
- Verified `POST http://localhost:3000/streams/connect` returns the unchanged web contract, including `playbackUrl: /mediamtx/{cameraId}/whep`, `protocol: webrtc`, and `playerType: whep`.
- `LiveStreamService_v3` now sends the same relay fields as `buildConnectStreamInput` in the React source and resolves relative playback URLs against the stream proxy origin.
- `WhepPlayer_v3` advertises PCMU, PCMA, and OPUS audio caps so MediaMTX accepts cameras with G.711 audio, logs asynchronous GStreamer bus errors, detects the main-stream H264/H265 codec, and cancels stale camera selections.
- Fixed the recorded Page.OnVisualParentChanged startup crash. ShellPage_v3 is now navigated through a WPF Frame instead of being placed directly in a Window.
- Fixed the nested Live page host: ContentFrame now uses Frame.Navigate for LivePage_v3, rather than assigning a Page to Content.
- Added `LivePage_v3` and `LiveViewModel_v3` as an isolated shell destination.
- The page uses the isolated `WhepPlayer_v3`; the existing `VLiveStream` control and isolated direct RTSP player remain available as fallback implementations.
- With `UseShellV3=true`, the `_v3` shell opens Live View through `LivePage_v3`.
- The React metadata WebSocket behavior is represented by isolated `MetadataSocketService_v3`; it is activated only for an assigned AI stream and disposed with its tile.
- Added Live View parity actions: sidebar selection/check state, layout menu, camera context menu (select, fill, connect, disconnect, remove), and bulk connect with per-camera fallback.

## Phase 1 — Discovery

| Module | Source evidence | Target evidence | Status | Notes |
| --- | --- | --- | --- | --- |
| Repository and project baseline | Current web and WPF repositories indexed | `V3S.sln`, `V3SClient.csproj`, .NET Framework 4.8 | Complete | Existing uncommitted WPF changes preserved |
| Authentication/client selection | Login, profile store, `RequireAuth` | `LoginWindow`, `ApiManager`, `GlobalUserInfo` | Analyzed | No changes |
| Live monitoring | `/live`, live stores, stream service, metadata WebSocket | `VLiveStream`, `ViewCamera`, stream/services | Implemented side-by-side | New `_v3` page/view models/tile/player/services only; existing V3 controls unchanged |
| Map | `/emap`, camera store | `EMap`, `SystemAndMap` | Analyzed | No changes |
| Playback | `/playback`, playback feature modules | `VPlayback`, `VPlaybackHLS` | Analyzed | No changes |
| Vehicle registration/list | Removed from current source; historical route/API evidence recorded | `VVehicleRegisterManagement`, `ucWebVehicleRegisterManagement`, `QL_BSX_DK` | Blocked | Current API/permission/navigation contract required |
| VMS design system | Color source of truth read | `styles\V3Migration` isolated dictionaries | Implemented | Named `_v3` keys only; no implicit styles |
| Login and authentication | `/login`, auth/profile stores, `RequireAuth` | `LoginWindow`, `ApiManager`, `GlobalUserInfo` | Implemented side-by-side | `LoginWindow_v3`, `LoginPage_v3`, `LoginViewModel_v3`, `AuthenticationService_v3`; existing login remains startup path |

## Next task

Run Live View `_v3` interactively against representative main/sub/AI streams and record visible frames, metadata alignment and WPF binding output; then address only evidence-backed defects.
