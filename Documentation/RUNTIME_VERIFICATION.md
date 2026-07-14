# Runtime Verification

## 2026-07-14 - Live View `_v3` parity pass

- Final Debug x64 and Release x64 solution builds after multi-camera, metadata/heartbeat and ROI changes: succeeded with 0 errors; existing unrelated warnings remain.
- XAML compile succeeded for the new page and tile; no static binding/XAML parser error was emitted.
- Runtime PID 21956 remained responsive at the login window. The interactive run then logged successful authentication, profile `DK_HN`, 59 cameras, system GStreamer initialization, and four concurrent WHEP pipelines (`2901S-01`, `2901S`, `2902S`, `2904V-01`) before the application was closed. No new exception or GStreamer error was logged during this observation.
- A visible-frame assertion and dynamic WPF binding trace cannot be extracted from the native D3D11 surface without an attached interactive debugger; these remain manual visual checks.
- The existing `VLiveStream` and `ViewCamera` files were not modified. Interactive opening of the legacy page remains a manual navigation check.

## 2026-07-14 - Selection/profile stability pass

- Debug and Release x64 builds after tile reuse, sidebar state indicators and web-profile fallback: succeeded with 0 errors.
- Debug executable started successfully and remained responsive at the login window (PID 2748).
- Full profile-count verification requires completing login in the interactive window; the new log entry will report the web projection count and fallback count separately.

## 2026-07-13 - Live View `_v3` GStreamer WHEP correction

- Direct RTSP diagnosis: GStreamer reported `gst_rtspsrc_retrieve_sdp ... Failed to connect`, proving the failure occurred before SDP/codec/rendering.
- Stream broker probe: `POST http://localhost:3000/streams/connect` returned `/mediamtx/live/2901S-01/main/whep`.
- GStreamer WHEP probe: the system 1.28.5 pipeline ran against that URL for the full diagnostic interval without an error after advertising PCMU, PCMA, and OPUS audio caps.
- Active control: `LivePage_v3` now hosts `WhepPlayer_v3`; `GstRtspPlayer_v3` remains an inactive fallback.
- Rebuilt application run: login and profile `DK_HN` succeeded, 59 cameras loaded, and WHEP pipelines started for `2901S` and `2901S-01` with HTTP 200 responses from `/streams/connect`; no subsequent GStreamer/WHEP error was logged during the observed interval.
- Debug x64 build: succeeded with 0 errors; existing unrelated warnings remain.
- Release x64 build: succeeded with 0 errors; existing unrelated warnings remain.
- Visible frame and WPF binding output require an interactive authenticated run and remain pending.

## Earlier direct RTSP diagnostic

- Installed runtime check: `C:\Program Files\gstreamer\1.0\msvc_x86_64\bin\gst-launch-1.0.exe --version` returned GStreamer 1.28.5.
- Plugin check: `rtspsrc`, `rtph264depay`, `rtph265depay`, `d3d11h264dec`, `d3d11h265dec`, and `d3d11videosink` were found.
- Debug x64 build: succeeded with 0 errors; existing unrelated warnings remain.
- Release x64 build: succeeded with 0 errors; existing unrelated warnings remain.
- Startup: Debug executable opened the existing login window and remained responsive (PID 7488 at 17:10 local time).
- Runtime selection after login: confirmed at 17:10:35; log records `Live View _v3 GStreamer runtime: C:\Program Files\gstreamer\1.0\msvc_x86_64`.
- Camera pipeline: multiple direct RTSP H264 pipelines started between 17:10:36 and 17:11:18 for cameras selected in profile `DEV`; no new GStreamer error, WHEP error, or `/streams/connect` call was logged.
- Final rebuilt run: profile `DEV` loaded at 17:15:54, system GStreamer initialized at 17:15:54, and direct RTSP pipelines started for `ptz_001` and `CamTestViolence`; PID 8576 remained responsive. The first camera was started once, confirming duplicate `Loaded`/selection initialization was removed.
- Visible frame verification: requires user confirmation because the WPF video surface is a native D3D11 child window and is not exposed through UI Automation.
- Binding errors: no XAML compile error; Visual Studio Output binding inspection still requires an interactive debugger session.
- Existing V3 Live View: source and navigation were not modified; interactive open verification remains pending.

## Remaining environment checks

- The selected camera relay endpoint must be reachable from this workstation over RTSP/TCP.
- Camera metadata must supply `RtspUrlRaw`, `RtspUrlMainRaw`, or the existing `rtps` fallback.
