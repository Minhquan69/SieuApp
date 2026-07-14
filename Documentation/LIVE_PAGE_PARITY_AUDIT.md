# Live Page Parity Audit

Audit date: 2026-07-14. Source: `front-end/src/pages/LivePage.tsx` and related live components/services. The requested instruction file was not present in the migrated root; the permanent instruction file was read from `iVista-Client-Application/AI_MIGRATION_INSTRUCTIONS.md`.

| Feature | Next.js behavior | Current WPF behavior | Status | Root cause | Required fix | Priority |
| ------- | ---------------- | -------------------- | ------ | ---------- | ------------ | -------- |
| Layout selector | Hover/click selector with 1x1, 2x2, 3x3, 5+1, 16+1, 6x6 and custom 1-100; active option highlighted | `_v3` popup and custom count exist; active styling and persistence are incomplete | Partial | WPF selector is code-behind driven and does not persist state | Bind active layout/custom count and persist through existing settings | High |
| Camera selection | Sidebar click adds/removes camera; selected camera has check/highlight | Sidebar click, check/highlight, hover add/connect buttons | Partial | Inline actions are new and need full interaction verification | Verify click/hover event routing and prevent duplicate selection | High |
| Grid placement | Drag/drop camera tiles and slot updates without losing session state | Tile drag/drop swaps slot cameras and reconnects changed players | Partial | WPF grid has no persisted slot order and no drag ghost/target indicator | Add visual drop target and safe session cleanup on swap | High |
| Stream connect | Abortable connect with 15s timeout, retry controller, player-ready timeout | Abortable service/player connect with retry timer | Partial | No explicit video-frame-ready timeout overlay | Add player-ready timeout and user-facing retry action | Critical |
| Stream errors | Codec-specific error, connection-lost retry, no-frame error | Generic error panel and retry | Partial | Error classification and no-frame timeout are not surfaced | Map codec/no-frame errors to distinct messages | High |
| Loading | Animated Loader2 for connecting and playback preparation | Spinner and indeterminate progress overlay | Complete | Equivalent visual state exists | Keep and verify theme resource keys | Medium |
| Main/sub/AI stream | Stream selector; AI warmup and metadata readiness state | Stream selector and metadata overlay/WebSocket | Partial | AI warmup timeout/readiness indicator is missing | Add AI warmup state and timeout indicator | High |
| AI metadata | Shared metadata socket subscription, reconnect, bbox/ROI overlay | Dedicated metadata service and overlay | Partial | ROI/metadata rendering needs live runtime verification | Verify subscription cleanup and coordinate mapping | High |
| Tile actions | Hover actions: fullscreen, connect/disconnect, remove, stream selection | Hover action bar with equivalent actions | Complete | Existing `_v3` tile implements actions | Runtime verify | Medium |
| Fullscreen | Route-level fullscreen suspends/restarts sessions and floating actions | `_v3` fullscreen mode and toolbar | Partial | Suspension lifecycle is not equivalent to route lifecycle | Verify reopen/close cancellation and player disposal | High |
| Sidebar/search | Grouped camera list, search, active state | Grouped list and search binding | Complete | Existing `_v3` VM maps groups and filters | Runtime verify | Medium |
| Bulk actions | Connect/disconnect/remove bulk endpoints and fallback | Bulk connect/disconnect/remove with fallback | Partial | Bulk response handling requires endpoint/runtime confirmation | Verify exact response and fallback behavior | High |
| Permissions | Protected route and permission-aware navigation | Existing login/profile flow and shell navigation | Partial | Live-specific permission gate is not explicit in `_v3` | Reuse existing permission source before navigation | High |
| Cleanup | Route effect cleanup, abort controllers, retry cancellation, player destroy | Page/tile disposal, cancellation token, retry timer disposal | Partial | Page reopen and all event detach paths need runtime test | Add parity test and inspect logs | High |
| GitNexus | Indexed execution graph and impact analysis | No Git metadata/index available in migrated folder | Not applicable | All three requested folders lack `.git` roots in this workspace | Record limitation; use source/code evidence and re-index when Git root is supplied | Medium |

## Audit conclusion

Critical/high work is concentrated in stream readiness/error states, AI warmup, fullscreen cleanup, and interaction verification. Existing V3 controls remain preserved; fixes must stay in `_v3` files and services.
