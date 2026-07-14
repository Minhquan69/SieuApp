# VMS Color Mapping

The source is `iVista-Client-WebApp\vms-color-source-of-truth.md`. Phase 2 implements the dark operational tokens and reserves explicit light-theme values in `V3SClient\styles\V3Migration\VmsColors_v3.xaml`; all resource keys end in `_v3`.

| Source token | Color value | Semantic role | WPF Color key | WPF Brush key | Consumers |
| --- | --- | --- | --- | --- | --- |
| Dark `background.app` | `#06111F` | Application background | `VmsBackgroundColor_v3` | `VmsBackgroundBrush_v3` | `_v3` pages |
| Dark `background.surface` | `#0B1B2B` | Primary surface/card | `VmsSurfaceColor_v3` | `VmsSurfaceBrush_v3` | `_v3` cards/grids |
| Dark `background.surface2` | `#102437` | Secondary surface | `VmsSurface2Color_v3` | `VmsSurface2Brush_v3` | Toolbars/filter panels |
| Dark `background.elevated` | `#152F48` | Elevated surface | `VmsElevatedColor_v3` | `VmsElevatedBrush_v3` | Dialogs/popovers |
| Dark `background.sidebar` | `#071624` | Sidebar | `VmsSidebarColor_v3` | `VmsSidebarBrush_v3` | `_v3` shell only |
| Dark `border.default` | `#1E3A56` | Standard border | `VmsBorderColor_v3` | `VmsBorderBrush_v3` | Inputs/grids |
| Dark `border.focus` | `#3B82F6` | Focus indicator | `VmsFocusColor_v3` | `VmsFocusBrush_v3` | Focus states |
| Dark `text.primary` | `#EAF2FF` | Primary text | `VmsTextPrimaryColor_v3` | `VmsTextPrimaryBrush_v3` | `_v3` text |
| Dark `text.secondary` | `#A9BCD0` | Secondary text | `VmsTextSecondaryColor_v3` | `VmsTextSecondaryBrush_v3` | Labels/help text |
| Dark `text.disabled` | `#52677D` | Disabled text | `VmsTextDisabledColor_v3` | `VmsTextDisabledBrush_v3` | Disabled controls |
| Dark `brand.primary` | `#2563EB` | Brand/selected/active | `VmsPrimaryColor_v3` | `VmsPrimaryBrush_v3` | Primary actions/selection |
| Dark `brand.hover` | `#1D4ED8` | Primary hover | `VmsPrimaryHoverColor_v3` | `VmsPrimaryHoverBrush_v3` | Button hover |
| Dark `brand.active` | `#1E40AF` | Primary pressed | `VmsPrimaryPressedColor_v3` | `VmsPrimaryPressedBrush_v3` | Button pressed |
| Status online | `#22C55E` | Healthy/success | `VmsSuccessColor_v3` | `VmsSuccessBrush_v3` | Status badges |
| Status warning | `#F59E0B` | Warning | `VmsWarningColor_v3` | `VmsWarningBrush_v3` | Status badges |
| Status danger | `#EF4444` | Error/critical | `VmsErrorColor_v3` | `VmsErrorBrush_v3` | Error panels |
| Status offline | `#64748B` | Offline/disabled state | `VmsOfflineColor_v3` | `VmsOfflineBrush_v3` | Status badges |
| Light `background.app` | `#F5F7FB` | Light application background | `VmsLightBackgroundColor_v3` | `VmsLightBackgroundBrush_v3` | Future light `_v3` theme |
| Light `brand.primary` | `#FF7A1A` | Light-theme brand | `VmsLightPrimaryColor_v3` | `VmsLightPrimaryBrush_v3` | Future light `_v3` theme |

Opacity-based source tokens are represented as ARGB brushes in `VmsBrushes_v3.xaml`: primary soft `#242563EB` (14%), primary softer `#142563EB` (8%), primary border `#732563EB` (45%), and status soft brushes at 14%. No toolkit defaults are substituted.

Live View audit (2026-07-14): `LivePage_v3.xaml`, `LiveTile_v3.xaml`, `WhepPlayer_v3.xaml`, and `AiMetadataOverlay_v3.cs` introduce no component-level hexadecimal/RGB colors. Workspace, sidebar, tiles, borders, text, focus, online/offline/loading/error states, ROI and bounding boxes resolve through isolated semantic `Vms*Brush_v3` resources. No new theme token was required.
