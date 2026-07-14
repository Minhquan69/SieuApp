# Libraries

No NuGet dependency was added, removed, or upgraded. Live View `_v3` now selects the installed native GStreamer runtime through configuration; the packaged runtime remains the fallback. AI metadata uses the .NET Framework `ClientWebSocket` API already available to the project.

| No. | Library | Version | Purpose | Project | Framework compatibility | License | Free or commercial | Existing or newly added | Reason | Notes |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | Newtonsoft.Json | 13.0.3 | JSON | V3SClient | net48 package | Verify before change | Existing | Existing | API serialization | Used by `ApiManager` |
| 2 | System.Net.Http | 4.3.4 | HTTP | V3SClient | net48 package | Verify before change | Existing | Existing | HTTP client support | Existing API infrastructure |
| 3 | NLog | 5.4.0 | Logging | V3SClient | Project reference | Verify before change | Existing | Existing | Logging | Existing logging integration |
| 4 | LiveCharts.Wpf | 0.9.7 | Charts | V3SClient | Project reference | Verify before change | Existing | Existing | Charts | Reuse assessment required per chart |
| 5 | Microsoft.Web.WebView2 | 1.0.3856.49 | Embedded web content | V3SClient | Project reference | Verify before change | Existing | Existing | Existing vehicle-registration page | Must not become a migration replacement |
| 6 | Microsoft.Xaml.Behaviors.Wpf | 1.1.135 | XAML behaviors | V3SClient | Project reference | Verify before change | Existing | Existing | WPF behaviors | Available for isolated MVVM binding behavior |
| 7 | MahApps.Metro.IconPacks | 5.1.0 | Icons | V3SClient | Project reference | Verify before change | Existing | Existing | Icons | Restyle before use in `_v3` UI |
| 8 | Extended.Wpf.Toolkit | 4.6.1 | WPF controls | V3SClient | net472 package | Verify before change | Existing | Existing | Existing controls | Existing AvalonDock references |
| 9 | GMap.NET.Core / WinPresentation | 2.1.7 | Mapping | V3SClient | net48 package | Verify before change | Existing | Existing | Map views | Existing map capability |
| 10 | GstSharp | 1.18.0 | GStreamer interop | V3SClient | net462 package | Verify before change | Existing | Existing | Media playback | Existing dependency |
| 11 | OpenCvSharp4 / runtime.win | 4.10.0.20241108 | Image processing | V3SClient | Project reference | Verify before change | Existing | Existing | Vision features | Existing dependency |
| 12 | StackExchange.Redis | 2.8.31 | Realtime/Redis | V3SClient | Project reference | Verify before change | Existing | Existing | Redis pub/sub | Existing dependency |
| 13 | System.Data.SQLite / EF6 / SQLite | 1.0.119 / 6.4.4 / 3.13.0 | Local data | V3SClient | net462 packages | Verify before change | Existing | Existing | Persistence | Existing dependency set |
| 14 | Dapper | 2.1.66 | Data access | V3SClient | Project reference | Verify before change | Existing | Existing | Data access | Existing dependency |
| 15 | SharpDX family | 4.2.0 | DirectX interop | V3SClient | Project reference | Verify before change | Existing | Existing | Graphics | Existing dependency |
| 16 | SSH.NET | 2025.0.0 | SSH | V3SClient | Project reference | Verify before change | Existing | Existing | Remote operations | Existing dependency |
| 17 | System.Reactive | 6.0.1 | Reactive utilities | V3SClient | Project reference | Verify before change | Existing | Existing | Reactive workflows | Existing dependency |
| 18 | WpfAnimatedGif | 2.0.2 | Animated images | V3SClient | Project reference | Verify before change | Existing | Existing | Image display | Existing dependency |
| 19 | GStreamer native runtime (MSVC x86_64) | 1.28.5 | WHEP/WebRTC playback and D3D11 decode/render | V3SClient `_v3` shell | Native x64 runtime used through existing GstSharp interop | LGPL (plugin inspection) | Free/open source | Existing machine installation, newly selected by configuration | Play MediaMTX WHEP output without adding a NuGet dependency | Root: `C:\Program Files\gstreamer\1.0\msvc_x86_64`; packaged `x64` fallback retained |
| 20 | System.Net.WebSockets.ClientWebSocket | .NET Framework 4.8 BCL | Live AI metadata WebSocket | V3SClient `_v3` Live View | Built into target framework | .NET Framework component | Existing platform API | Existing | Match web metadata subscription lifecycle without a package | Shared socket; cancellation, reconnect and cleanup implemented |

## Libraries considered but not selected

| Library | Reason for rejection |
| --- | --- |
| Any new grid, dialog, chart, or HTTP package | No current Vehicle List contract exists; no package is necessary for Phase 1. |
| DevExpress / SciChart | Commercial licensing has not been confirmed. |
