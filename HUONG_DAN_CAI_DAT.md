# Hướng dẫn build và cài đặt iVMS

## Nền tảng và kiến trúc

- Ứng dụng chính: `V3SClient/V3SClient.csproj`.
- Loại ứng dụng: WPF `WinExe`, .NET Framework 4.8.
- Kiến trúc phát hành: x64 (`Release|x64`).
- File thực thi sau build: `V3SClient/bin/Release/iVMS.exe`.

Vì đây là .NET Framework, bản phát hành không thể self-contained như .NET 6+.
Bộ cài kiểm tra/cài .NET Framework 4.8 và Visual C++ Runtime x64.

## Chuẩn bị máy build

1. Windows x64, Visual Studio Build Tools/MSBuild có .NET Framework 4.8 targeting pack.
2. NuGet và Inno Setup 6.
3. GStreamer 1.0 x64 MSVC runtime. Mặc định script tìm tại
   `C:\Program Files\gstreamer\1.0\msvc_x86_64`; có thể truyền đường dẫn khác
   bằng tham số `-GStreamerRoot`.

## Restore, build và tạo bộ cài

```powershell
nuget restore .\V3S.sln
msbuild .\V3SClient\V3SClient.csproj /t:Rebuild /p:Configuration=Release /p:Platform=x64
.\scripts\Build-Installer.ps1 -OutputDirectory .\installer\output
```

Nếu GStreamer ở vị trí khác:

```powershell
.\scripts\Build-Installer.ps1 -GStreamerRoot 'D:\Runtime\gstreamer\msvc_x86_64' -OutputDirectory .\installer\output
```

Bộ cài được tạo theo tên `iVMS-Setup-<version>-x64.exe` trong thư mục output.
Script in SHA-256 và kích thước sau khi tạo thành công.

## Nội dung đóng gói

- `iVMS.exe`, các DLL NuGet/managed, resource WPF, logo, icon và `login-background.png`.
- `server_config.json`, `NLog.config`, `wwwroot`, FFmpeg trong `tools\ffmpeg`.
- GStreamer x64: `bin`, plugins, GIO modules, `libexec` và `share` khi có.
- Microsoft Visual C++ Redistributable x64 và .NET Framework 4.8 bootstrapper.

Các địa chỉ API/WebSocket/RTSP không được hard-code vào bộ cài. Cấu hình máy chủ
được đặt trong `server_config.json`; metadata WebSocket mặc định nằm trong
`iVMS.exe.config` và cần được quản trị viên môi trường thay đổi khi triển khai.

## Cài đặt, nâng cấp và gỡ cài đặt

Chạy `iVMS-Setup-<version>-x64.exe`. Bộ cài tạo shortcut Desktop và Start Menu,
cài vào `Program Files\iVMS`, đồng thời giữ nguyên cấu hình người dùng nằm ngoài
thư mục cài đặt. Khi nâng cấp, dùng bộ cài phiên bản mới. Gỡ cài đặt qua Apps &
Features hoặc shortcut Uninstall trong Start Menu.

## Kiểm tra máy sạch

Trên Windows Sandbox/VM x64 chưa có Visual Studio, SDK hoặc GStreamer:

1. Cài bộ cài và mở iVMS từ Desktop/Start Menu.
2. Kiểm tra ảnh nền đăng nhập, logo/icon, đăng nhập/đăng xuất.
3. Kiểm tra API/WebSocket, Live View, RTSP/WebRTC và Playback.
4. Xác nhận không có lỗi DLL native/GStreamer và nâng cấp/gỡ cài đặt hoạt động.

## Lưu ý bảo mật và prerequisite còn lại

- WebView2 Evergreen Runtime x64 offline đã được đóng gói và cài tự động bởi installer.
- GStreamer và các DLL native/codec phải được stage từ runtime x64 tương thích.
- Không đóng gói token, mật khẩu hoặc private key triển khai. Mutual TLS dùng
  `RtspTlsClientCertificatePath` và `RtspTlsClientKeyPath` trong `iVMS.exe.config`;
  quản trị viên cấp các file này bên ngoài thư mục bộ cài.
