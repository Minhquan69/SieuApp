# V3 Preservation Report

## Live View _v3

- Preserved without modification: `UI/Views/VLiveStream` and `UI/Views/ViewCamera`.
- Active WHEP playback is isolated in `WhepPlayer_v3` and wired only into `LivePage_v3`; no existing native player or V3 Live View XAML was replaced.
- `GstRtspPlayer_v3` remains present and compiles as a fallback, but active Live View no longer references it because its relay endpoint was not reachable from this workstation.
- `ApiManager` and `ClientConfig` received only an additive `StreamApiUrl` property. GitNexus reported LOW impact for `ApiManager` (four direct import dependants and no affected execution process); login and existing API base URL behavior are unchanged.
- ShellWindow_v3 now hosts ShellPage_v3 through a Frame; the existing MainWindow is unchanged.
- `LivePage_v3` owns and disposes its isolated GStreamer WHEP player when unloaded.
- No existing Live View page or stream control was renamed, replaced, or overwritten.
- The parity implementation is additive: `LiveTile_v3`, `AiMetadataOverlay_v3`, `MetadataSocketService_v3`, and new `_v3` ViewModel types do not modify `VLiveStream`, `ViewCamera`, their ViewModels, or their navigation path.
- GitNexus impact checks before editing `LivePage_v3`, `LiveViewModel_v3`, `WhepPlayer_v3`, and `LiveStreamService_v3` all reported LOW risk. The graph-indexed base repository itself was not edited.

| Existing component | Type | Preserved | New `_v3` equivalent | Existing file modified | Reason | Risk |
| --- | --- | --- | --- | --- | --- | --- |
| `VVehicleRegisterManagement` | Page | Yes | None in Phase 1 | No | Existing vehicle-registration WebView page remains the rollback path | Low direct graph impact; navigation caller exists |
| `ucWebVehicleRegisterManagement` | UserControl | Yes | None in Phase 1 | No | Existing `_vehicleReg` WebView endpoint is retained unchanged | Low graph-detected impact |
| `MainWindow.SelectdPage` / `btnRegDK` | Navigation | Yes | None in Phase 1 | No | Existing route selection is preserved; no new navigation is added | Existing shell is high-value integration point |
| `QL_BSX_DK` | Page | Yes | None in Phase 1 | No | No evidence permits replacing or reusing it for the removed web history screen | Unknown equivalence |
| `ApiManager` | Shared API singleton | Yes | None in Phase 1 | No | GitNexus reports critical blast radius (107 upstream dependants); no change made | Critical |
| `App.xaml` resource merge | Application resources | Yes | Isolated VMS dictionaries | Yes | Registers only explicitly keyed `_v3` colors, brushes, and styles | Low graph impact; Release build passed; Debug output locked by running V3SClient |
| `LoginWindow` / `LoginControl` | Existing authentication UI | Yes | `LoginWindow_v3` / `LoginPage_v3` | No | `UseLoginV3=false` preserves old login; true explicitly selects the new flow | Low coexistence risk; Debug and Release compile; runtime host selection is reversible |
| `App.OnStartup` | Application startup handoff | Yes | Feature-flag selection only | Yes | Chooses old or new login window, then preserves the existing dialog-result/MainWindow handoff | Low GitNexus impact; default behavior unchanged |
