# iVista Client Application

Windows desktop client for iVista VMS. The application targets **.NET Framework 4.8** and must be built on Windows with Visual Studio/MSBuild; `dotnet build` is not the supported build path for this legacy WPF solution.

## Quick start

1. Install **Visual Studio 2022 Community/Professional** (or Build Tools 2022) with the **.NET desktop development** workload and the **.NET Framework 4.8 SDK/Targeting Pack**.
2. Clone this repository and switch to the `VMS-Application` branch:

   ```powershell
   git clone --branch VMS-Application https://github.com/iVista-Dev/iVista-Client-Application.git
   cd iVista-Client-Application
   ```

3. From PowerShell, restore and build the solution:

   ```powershell
   .\scripts\Restore-And-Build.ps1
   ```

   Use `-Configuration Debug` for a Debug build, or `-SkipBuild` to restore packages only.
4. Open `V3S.sln` in Visual Studio. Set `V3SClient` as the startup project and select the `x64` platform before running.

## Runtime requirements

- **GStreamer MSVC x86_64 runtime** is required for Live View. Install a compatible runtime and set `GStreamerRoot_v3` in `V3SClient/App.config` to its root directory. The directory must contain `bin` and `lib\gstreamer-1.0`.
- The configured API, metadata WebSocket, and camera-stream services must be reachable. These values are environment-specific; do not commit production credentials or endpoints.
- Microsoft Edge WebView2 Runtime is required for embedded web views. It is normally installed with current Windows/Edge, but can be installed separately if missing.

## Known setup notes

- The repository intentionally does not commit restored NuGet packages. The build script runs MSBuild restore to download them.
- GitHub warns that `V3SClient/images/gif/smart_network.gif` is 86.66 MB. Install Git LFS before adding/replacing large binary assets.
- `System.Data.SqlClient` 4.8.3 has published security advisories. Upgrade it only after compatibility testing.

See [Documentation/BUILD_AND_RUN.md](Documentation/BUILD_AND_RUN.md) for detailed build, runtime, and troubleshooting instructions.
