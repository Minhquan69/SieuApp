# Build and Run

## GStreamer for Live View `_v3`

- Active configured root: `C:\Program Files\gstreamer\1.0\msvc_x86_64` (`App.config` key `GStreamerRoot_v3`).
- Required plugins: `whepsrc`, `webrtcbin`, H264/H265 RTP depayloaders, D3D11 decoders, and `d3d11videosink`.
- If the configured installation is missing, `ShellWindow_v3` falls back to the packaged `x64` runtime next to `V3SClient.exe`.
- Active Live View `_v3` uses GStreamer as the player and MediaMTX as the WHEP relay. The frontend stream proxy, backend, and MediaMTX must be running.

## Project

- Solution: `V3S.sln`
- Startup project: `V3SClient`
- Target framework: .NET Framework 4.8
- Recommended build tool: Visual Studio MSBuild for full .NET Framework, not `dotnet build` by default.

## Verification

## Live View _v3 verification

- 2026-07-13 Debug x64: succeeded after correcting the independent stream API URL.
- 2026-07-13 Release x64: succeeded after correcting the independent stream API URL.
- Contract probe: `POST http://localhost:3000/streams/connect` succeeded and returned a WHEP playback URL; the diagnostic stream session was subsequently disconnected.
- Runtime prerequisite: `ivista-webclient-frontend` (port 3000) and `ivista-webclient-backend` (port 8000) must be running. `StreamApiUrl` is stored in `server_config.json` and copied to the build output.
- Release x64 build succeeded after compiling LivePage_v3, LiveViewModel_v3, and ShellPage_v3 navigation.
- The recorded Shell startup crash was fixed by navigating ShellPage_v3 through ShellWindow_v3.ShellFrame.
- Debug x64 and Release x64 both succeeded on 2026-07-13 after switching the active Live View back to GStreamer WHEP.
- Interactive visible-frame and Visual Studio WPF binding verification remain pending; the native D3D11 child window is not exposed through UI Automation.

No source, XAML, project, package, or resource change was made in Phase 1. Therefore no build was run for that documentation-only phase.

Phase 2 uses Visual Studio MSBuild 18.7.8. XML validation passed for all six `styles\V3Migration\*_v3.xaml` dictionaries and for `V3SClient.csproj`.

- Debug x64: compilation and WPF markup compilation completed, but final executable copy failed because the user-running `V3SClient` process (PID 18232) locked `bin\Debug\V3SClient.exe`. The process was not stopped.
- Release x64: succeeded. Existing compiler warnings remain (unused members/variables, unawaited calls, obsolete NLog and Redis APIs); none reference Phase-2 resource files.
- Runtime/binding verification: not performed because the running Debug application could not be replaced and was not interrupted.

## Phase-3 authentication verification

- Release x64: succeeded after compiling `LoginPage_v3`, `LoginWindow_v3`, `LoginViewModel_v3`, and `AuthenticationService_v3`.
- Debug x64: succeeded using the isolated output path `bin\Debug_v3verify\`; this avoids the existing running V3SClient lock on `bin\Debug\V3SClient.exe` while compiling the Debug configuration and WPF markup.
- Runtime authentication and binding verification: pending. The new window is selected only when the default-off `UseLoginV3` setting is enabled; valid test credentials are still required.

`App.config` contains `UseLoginV3=false`. Keep this default to open the existing `LoginWindow`; set it to `true` only when intentionally testing `LoginWindow_v3`. Both paths return the same dialog result to the unchanged application-startup handoff.

Before any implementation change, restore packages and build Debug and Release. Validate WPF binding errors, XAML/resource parsing, existing V3 pages, new `_v3` page navigation, and API serialization.
