// module_live.js - Thiết lập giao diện Sidebar và Tracking lưới Camera

const TRACKING_MODE = {
    NONE: 'none',      // không lưu
    MEMORY: 'memory',  // lưu RAM
    LOCAL: 'local'     // lưu localStorage
};

const LS_PREFIX = 'camera_tracking_path:';

// Marker pin tip offset: negative value moves pin up, positive moves down
// Adjust this value to align marker tail exactly with map coordinate
const CAMERA_MARKER_TIP_OFFSET_Y = 5;

// Khởi tạo HTML tự động
const liveHTML = `
    <!-- Sidebar: Camera List + Search -->
    <div id="legacySidebarRemoved" style="display:none">
        <div id="sidebarHeader" onclick="toggleSidebar()">
            <div class="title-group">
                <div class="accent-bar"></div>
                <span>CAMERAS</span>
            </div>
            <button id="sidebarToggle" title="Thu gọn / Mở rộng">◀</button>
        </div>
        <div id="sidebarBody">
            <div style="display:flex; align-items:center; gap:8px; margin-bottom:5px;">
                <input type="checkbox" id="chkFOV" checked onchange="toggleFOVLayer()" style="cursor:pointer;"/>
                <label for="chkFOV" style="font-size:11px; color:#aaa; cursor:pointer;">Hiển thị góc nhìn (FOV)</label>
            </div>
            <input id="searchBox" type="text" placeholder="Tìm camera..." oninput="filterCameras()" />
            <ul id="cameraList"></ul>
            <button onclick="stopTracking()" id="btnStopTrack" style="display:none;">⏹ Bỏ theo dõi</button>
            <button onclick="clearAllTrackingData()" id="btnClearAllTrackingData">Xóa tất cả dữ liệu tracking</button>
        </div>
    </div>
    <!-- Tracking Bar (top center) -->
    <div id="trackingBar">
        <div class="pulse-icon"></div>
        <span>Đang theo dõi: <strong id="trackingName">-</strong></span>
        <button id="btnTogglePath" onclick="togglePathTracking()" style="background: rgba(232,126,4,0.1); border-color: #E87E04; color: #E87E04;">Hiện vết di chuyển</button>
        <select id="trackingModeSelect" onchange="onTrackingModeChanged(this.value)" style="font-size:11px;background:#222;color:#ddd;border:1px solid #444;border-radius:4px;padding:2px 6px;">
            <option value="none">Không lưu</option>
            <option value="memory">Lưu tạm thời</option>
            <option value="local">Lưu local</option>
        </select>
        <button id="btnClearCurrentTracking" onclick="clearCurrentTrackingData()" style="background: rgba(200,60,60,0.12); border-color: #c44; color: #ffb3b3;">Xóa Data Tracking</button>
        <button onclick="stopTracking()">Bỏ theo dõi</button>
    </div>
`;
document.body.insertAdjacentHTML('beforeend', liveHTML);

const confirmHTML = `
    <div id="appConfirmOverlay" style="
        display:none;
        position:fixed;
        inset:0;
        background:rgba(0,0,0,0.45);
        z-index:99999;
        align-items:center;
        justify-content:center;
    ">
        <div id="appConfirmBox" style="
            width:min(420px, calc(100vw - 32px));
            background:#1f1f1f;
            color:#fff;
            border:1px solid rgba(255,255,255,0.08);
            border-radius:12px;
            box-shadow:0 20px 50px rgba(0,0,0,0.35);
            overflow:hidden;
            font-family:inherit;
        ">
            <div id="appConfirmTitle" style="
                padding:14px 16px 8px 16px;
                font-size:15px;
                font-weight:700;
            ">Xác nhận</div>

            <div id="appConfirmMessage" style="
                padding:0 16px 16px 16px;
                font-size:13px;
                line-height:1.5;
                color:rgba(255,255,255,0.88);
                white-space:pre-wrap;
            "></div>

            <div style="
                display:flex;
                justify-content:flex-end;
                gap:8px;
                padding:12px 16px 16px 16px;
                border-top:1px solid rgba(255,255,255,0.06);
            ">
                <button id="appConfirmCancelBtn" type="button" style="
                    padding:8px 12px;
                    border-radius:8px;
                    border:1px solid rgba(255,255,255,0.15);
                    background:transparent;
                    color:#fff;
                    cursor:pointer;
                ">Hủy</button>

                <button id="appConfirmOkBtn" type="button" style="
                    padding:8px 12px;
                    border-radius:8px;
                    border:1px solid #E87E04;
                    background:#E87E04;
                    color:#fff;
                    cursor:pointer;
                    font-weight:600;
                ">Đồng ý</button>
            </div>
        </div>
    </div>
`;
document.body.insertAdjacentHTML('beforeend', confirmHTML);

let confirmDialogState = null;

function showConfirmDialog(message, title = 'Xác nhận') {
    return new Promise((resolve) => {
        const overlay = document.getElementById('appConfirmOverlay');
        const titleEl = document.getElementById('appConfirmTitle');
        const messageEl = document.getElementById('appConfirmMessage');
        const okBtn = document.getElementById('appConfirmOkBtn');
        const cancelBtn = document.getElementById('appConfirmCancelBtn');

        if (!overlay || !titleEl || !messageEl || !okBtn || !cancelBtn) {
            resolve(false);
            return;
        }

        // Nếu đang có dialog cũ thì đóng luôn
        if (confirmDialogState?.cleanup) {
            confirmDialogState.cleanup(false);
        }

        titleEl.textContent = title;
        messageEl.textContent = message;
        overlay.style.display = 'flex';

        const onOk = () => cleanup(true);
        const onCancel = () => cleanup(false);
        const onOverlayClick = (e) => {
            if (e.target === overlay) cleanup(false);
        };
        const onKeyDown = (e) => {
            if (e.key === 'Escape') cleanup(false);
            if (e.key === 'Enter') cleanup(true);
        };

        function cleanup(result) {
            overlay.style.display = 'none';

            okBtn.removeEventListener('click', onOk);
            cancelBtn.removeEventListener('click', onCancel);
            overlay.removeEventListener('click', onOverlayClick);
            document.removeEventListener('keydown', onKeyDown);

            if (confirmDialogState?.cleanup === cleanup) {
                confirmDialogState = null;
            }

            resolve(result);
        }

        confirmDialogState = { cleanup };

        okBtn.addEventListener('click', onOk);
        cancelBtn.addEventListener('click', onCancel);
        overlay.addEventListener('click', onOverlayClick);
        document.addEventListener('keydown', onKeyDown);

        setTimeout(() => okBtn.focus(), 0);
    });
}

// Trạng thái cục bộ (State)
const markers = {};
const overlapClusterMarkers = {};
const cameraData = {};  // Lưu toàn bộ thông tin camera
const isOverviewMap = window.iVistaMapOverview === true;


let trackingCamId = null;
let pathTrackingEnabled = false; // Mặc định KHÔNG lưu vết khi bám

let activeTrackingPath = [];
const trackingConfigByCam = {};
const memoryTrackingStore = {};

// ===============HELPER================
function normalizeLatLng(lat, lng) {
    let nLat = Number(lat);
    let nLng = Number(lng);

    if (!Number.isFinite(nLat) || !Number.isFinite(nLng)) {
        return { lat: null, lng: null, valid: false };
    }

    // If likely swapped, fix automatically
    // Valid ranges: lat [-90,90], lng [-180,180]
    if (Math.abs(nLat) > 90 && Math.abs(nLng) <= 90) {
        const t = nLat; nLat = nLng; nLng = t;
    }

    const valid = Math.abs(nLat) <= 90 && Math.abs(nLng) <= 180;
    return { lat: nLat, lng: nLng, valid };
}

function toLngLatArray(lat, lng) {
    const n = normalizeLatLng(lat, lng);
    if (!n.valid) return null;
    return [n.lng, n.lat];
}

function getLocalKey(camId) {
    return `${LS_PREFIX}${camId}`;
}

function loadLocalPath(camId) {
    try {
        const raw = localStorage.getItem(getLocalKey(camId));
        if (!raw) return [];
        const data = JSON.parse(raw);
        return Array.isArray(data) ? data : [];
    } catch {
        return [];
    }
}

function saveLocalPath(camId, path) {
    localStorage.setItem(getLocalKey(camId), JSON.stringify(path || []));
}

function clearLocalPath(camId) {
    localStorage.removeItem(getLocalKey(camId));
}

function getCameraTrackingMode(camId) {
    if (!camId) return TRACKING_MODE.MEMORY;

    // First-run initialization per camera:
    // if localStorage already has tracking data => LOCAL, else NONE.
    if (!trackingConfigByCam[camId]) {
        const localPath = loadLocalPath(camId);
        trackingConfigByCam[camId] = {
            mode: (Array.isArray(localPath) && localPath.length > 0) ? TRACKING_MODE.LOCAL : TRACKING_MODE.MEMORY
        };
    }

    return trackingConfigByCam[camId].mode || TRACKING_MODE.MEMORY;
}

async function setCameraTrackingMode(camId, newMode) {
    if (!trackingConfigByCam[camId]) {
        trackingConfigByCam[camId] = { mode: TRACKING_MODE.NONE };
    }

    const oldMode = trackingConfigByCam[camId].mode || TRACKING_MODE.NONE;
    if (oldMode === newMode) return true;

    const activePath = (trackingCamId === camId) ? [...activeTrackingPath] : [];

    if (oldMode === TRACKING_MODE.NONE && newMode === TRACKING_MODE.MEMORY) {
        if (activePath.length) {
            memoryTrackingStore[camId] = mergePaths(memoryTrackingStore[camId], activePath);
        }
    }

    else if (oldMode === TRACKING_MODE.NONE && newMode === TRACKING_MODE.LOCAL) {
        if (activePath.length) {
            saveLocalPath(camId, mergePaths(loadLocalPath(camId), activePath));
        }
    }

    else if (oldMode === TRACKING_MODE.MEMORY && newMode === TRACKING_MODE.LOCAL) {
        const mem = memoryTrackingStore[camId] || [];
        const merged = mergePaths(loadLocalPath(camId), mem.length ? mem : activePath);
        saveLocalPath(camId, merged);
        delete memoryTrackingStore[camId];
    }

    else if (oldMode === TRACKING_MODE.LOCAL && newMode === TRACKING_MODE.MEMORY) {
        const ok = await showConfirmDialog(
            `Camera ${camId}: chuyển từ local sang tạm thời.\nDữ liệu trong local sẽ bị xóa và chuyển sang tạm thời (khởi động lại ứng dụng sẽ mất dữ liệu).`,
            'Chuyển chế độ tracking'
        );
        if (!ok) return false;

        const local = loadLocalPath(camId);
        memoryTrackingStore[camId] = mergePaths(memoryTrackingStore[camId], local);
        clearLocalPath(camId);
    }

    else if (oldMode === TRACKING_MODE.LOCAL && newMode === TRACKING_MODE.NONE) {
        const ok = await showConfirmDialog(
            `Camera ${camId}: chuyển sang không lưu?\nDữ liệu local sẽ bị xóa (ngừng theo dõi camera này sẽ mất dữ liệu).`,
            'Chuyển chế độ tracking'
        );
        if (!ok) return false;

        delete memoryTrackingStore[camId];
        clearLocalPath(camId);
    }

    else if (oldMode === TRACKING_MODE.MEMORY && newMode === TRACKING_MODE.NONE) {
        const ok = await showConfirmDialog(
            `Camera ${camId}: chuyển sang không lưu?\nDữ liệu tạm thời của camera này sẽ bị xóa (ngừng theo dõi camera này sẽ mất dữ liệu).`,
            'Chuyển chế độ tracking'
        );
        if (!ok) return false;

        delete memoryTrackingStore[camId];
        clearLocalPath(camId);
    }

    trackingConfigByCam[camId].mode = newMode;
    return true;
}

function getRenderablePath(camId) {
    if (!camId) return [];

    if (trackingCamId === camId && activeTrackingPath.length) {
        return activeTrackingPath;
    }

    const mode = getCameraTrackingMode(camId);

    if (mode === TRACKING_MODE.MEMORY) {
        return memoryTrackingStore[camId] || [];
    }

    if (mode === TRACKING_MODE.LOCAL) {
        return loadLocalPath(camId);
    }

    return [];
}

function appendTrackingPoint(camId, ll) {
    if (!camId || !ll) return;

    if (trackingCamId === camId) {
        activeTrackingPath = mergePaths(activeTrackingPath, [ll]);
    }

    const mode = getCameraTrackingMode(camId);

    if (mode === TRACKING_MODE.MEMORY) {
        memoryTrackingStore[camId] = mergePaths(memoryTrackingStore[camId], [ll]);
    } else if (mode === TRACKING_MODE.LOCAL) {
        const current = loadLocalPath(camId);
        saveLocalPath(camId, mergePaths(current, [ll]));
    }
}

function mergePaths(oldPath = [], newPath = []) {
    const result = [...oldPath];
    for (const p of newPath) {
        const last = result[result.length - 1];
        if (!last || last[0] !== p[0] || last[1] !== p[1]) {
            result.push(p);
        }
    }
    return result;
}

// Toggle Sidebar Global Function
window.toggleSidebar = function() {
    document.getElementById('sidebar').classList.toggle('collapsed');
}

// Lắng nghe click đóng radial
window.map.on('click', closeRadial);
window.map.on('dragstart', closeRadial);

// Nhận Lệnh WebView2
document.addEventListener('mapCommand', function(e) {
    const cmd = e.detail;
    if (cmd.action === 'updateCameras') updateCameras(cmd.data);
    else if (cmd.action === 'triggerAlarm') triggerAlarm(cmd.camId, cmd.duration);
    else if (cmd.action === 'updatePositions') updatePositions(cmd.data);
    else if (cmd.action === 'focusCamera') {
        const d = cmd.data || {};
        if (Number.isFinite(Number(d.lat)) && Number.isFinite(Number(d.lng))) {
            // A selection in the camera list is an explicit request to inspect
            // that camera: centre it and zoom in to a useful detail level.
            // Never zoom out a user who is already viewing the map closer.
            window.map.flyTo({
                center: [Number(d.lng), Number(d.lat)],
                zoom: Math.min(Math.max(window.map.getZoom(), 17), window.map.getMaxZoom()),
                duration: 800,
                essential: true
            });
        }
    }
});

// Hàm từ index.html map_assets cũ
window.setFovVisible = function(visible) {
    const isVisible = !!visible;
    const checkbox = document.getElementById('chkFOV');
    if (checkbox) checkbox.checked = isVisible;
    if (window.map && window.map.getLayer('fov-layer')) {
        window.map.setLayoutProperty('fov-layer', 'visibility', isVisible ? 'visible' : 'none');
    }
};

window.toggleFOVLayer = function() {
    const checkbox = document.getElementById('chkFOV');
    window.setFovVisible(!checkbox || checkbox.checked);
};

function getFOVWedge(lng, lat, heading, fov, radius = 0.001) {
    const coordinates = [[lng, lat]];
    const step = 2; // degrees
    const startAngle = heading - fov / 2;
    const endAngle = heading + fov / 2;
    for (let i = startAngle; i <= endAngle; i += step) {
        const rad = (90 - i) * Math.PI / 180;
        coordinates.push([
            lng + radius * Math.cos(rad),
            lat + radius * Math.sin(rad)
        ]);
    }
    coordinates.push([lng, lat]);
    return [coordinates];
}

function normalizeHeading(value) {
    const heading = Number(value);
    const safeHeading = Number.isFinite(heading) ? heading : 0;
    return ((safeHeading % 360) + 360) % 360;
}

function updateFOVSource() {
    if (!window.map || !window.map.isStyleLoaded()) return;

    // heading from the API is the physical camera direction.  The map bearing
    // must be applied when the user rotates the map, otherwise the field of
    // view continues to use its initial angle and appears fixed on screen.
    const mapBearing = normalizeHeading(window.map.getBearing());
    const features = Object.values(cameraData).filter(cam => !cam.isCluster).map(cam => {
        const cameraHeading = normalizeHeading(cam.heading);
        const displayedHeading = normalizeHeading(cameraHeading + mapBearing);
        return {
            type: 'Feature',
            properties: { camId: cam.camID, isOnline: cam.isOnline },
            geometry: {
                type: 'Polygon',
                coordinates: getFOVWedge(cam.lng, cam.lat, displayedHeading, cam.fov || 90)
            }
        };
    });
    const source = window.map.getSource('fov-source');
    if (source) {
        source.setData({ type: 'FeatureCollection', features: features });
    } else {
        window.map.addSource('fov-source', { type: 'geojson', data: { type: 'FeatureCollection', features: features } });
        window.map.addLayer({
            id: 'fov-layer', type: 'fill', source: 'fov-source',
            paint: {
                'fill-color': ['case', ['get', 'isOnline'], '#4caf50', '#666'],
                'fill-opacity': 0.2,
                'fill-outline-color': ['case', ['get', 'isOnline'], '#4caf50', '#666']
            }
        });
    }
}

// Rebuild the FOV source while the user rotates the map.  Coalesce rotate
// events to one update per rendered frame, so the camera view direction stays
// synchronized without creating a burst of source updates.
let fovRefreshPending = false;
function refreshFOVForMapBearing() {
    if (fovRefreshPending) return;
    fovRefreshPending = true;
    requestAnimationFrame(() => {
        fovRefreshPending = false;
        if (window.map && window.map.isStyleLoaded()) updateFOVSource();
    });
}

window.map.on('rotate', refreshFOVForMapBearing);
window.map.on('rotateend', refreshFOVForMapBearing);

const ICONS = {
    Live: `<svg viewBox="0 0 24 24"><path d="M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4zM14 13h-3v3H9v-3H6v-2h3V8h2v3h3v2z"/></svg>`,
    Playback: `<svg viewBox="0 0 24 24"><path d="M12 5V1L7 6l5 5V7c3.31 0 6 2.69 6 6s-2.69 6-6 6-6-2.69-6-6H4c0 4.42 3.58 8 8 8s8-3.58 8-8-3.58-8-8-8z"/><path d="M10 16.5l4.5-3.5L10 9.5v7z"/></svg>`,
    Info: `<svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>`,
    Alert: `<svg viewBox="0 0 24 24"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z"/></svg>`
};

function triggerAlarm(camId, duration) {
    const el = document.getElementById('cam-' + camId);
    const li = document.getElementById('li-' + camId);
    if (!el) return;
    el.classList.add('alarm');
    if (li) li.classList.add('alarm');
    window.map.flyTo({ center: markers[camId].getLngLat(), zoom: 15});
    setTimeout(() => {
        el.classList.remove('alarm');
        if (li) li.classList.remove('alarm');
    }, duration * 1000);
    const cam = cameraData[camId];
    showToast(`CẢNH BÁO KHẨN CẤP: ${cam ? cam.name : camId}`, duration);
}

function showToast(message, duration) {
    let container = document.getElementById('toast-container');
    if (!container) {
        container = document.createElement('div');
        container.id = 'toast-container';
        document.body.appendChild(container);
    }
    const toast = document.createElement('div');
    toast.className = 'toast-item';
    toast.innerHTML = `<span style="font-size: 20px;">🚨</span> <div><div style="font-weight:bold; font-size:14px;">BÁO ĐỘNG EMG</div><div style="font-size:12px; opacity: 0.9;">${message}</div></div>`;
    container.appendChild(toast);
    setTimeout(() => {
        toast.style.animation = 'toast-fade-out 0.4s forwards';
        setTimeout(() => toast.remove(), 400);
    }, duration * 1000);
}

function initPathLayers() {
    if (window.map.getSource('path-source')) return;
    window.map.addSource('path-source', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
    window.map.addLayer({ id: 'path-line-layer', type: 'line', source: 'path-source', filter: ['==', ['get', 'type'], 'line'], paint: { 'line-color': '#E87E04', 'line-width': 3, 'line-dasharray': [3, 3] } });
    window.map.addLayer({ id: 'path-points-layer', type: 'circle', source: 'path-source', filter: ['==', ['get', 'type'], 'point'], paint: { 'circle-radius': 3, 'circle-color': '#fff', 'circle-stroke-width': 1, 'circle-stroke-color': '#E87E04' } });
    window.map.addLayer({ id: 'path-start-layer', type: 'circle', source: 'path-source', filter: ['==', ['get', 'type'], 'start'], paint: { 'circle-radius': 5, 'circle-color': '#E87E04', 'circle-stroke-width': 2, 'circle-stroke-color': '#fff' } });
}

function updatePathLayer() {
    if (!window.map.getSource('path-source')) {
        initPathLayers();
    }

    const source = window.map.getSource('path-source');
    if (!source) return;

    if (!pathTrackingEnabled) {
        source.setData({ type: 'FeatureCollection', features: [] });
        return;
    }

    const path = trackingCamId ? getRenderablePath(trackingCamId) : [];

    if (!trackingCamId || path.length < 1) {
        source.setData({ type: 'FeatureCollection', features: [] });
        return;
    }

    const features = [];

    if (path.length >= 2) {
        features.push({
            type: 'Feature',
            properties: { type: 'line' },
            geometry: { type: 'LineString', coordinates: path }
        });
    }

    features.push({
        type: 'Feature',
        properties: { type: 'start' },
        geometry: { type: 'Point', coordinates: path[0] }
    });

    for (let i = 1; i < path.length - 1; i++) {
        features.push({
            type: 'Feature',
            properties: { type: 'point' },
            geometry: { type: 'Point', coordinates: path[i] }
        });
    }

    source.setData({ type: 'FeatureCollection', features });
}

window.togglePathTracking = function() {
    pathTrackingEnabled = !pathTrackingEnabled;
    const btn = document.getElementById('btnTogglePath');
    if (pathTrackingEnabled) {
        btn.textContent = 'Ẩn vết di chuyển';
        btn.style.background = '#E87E04'; btn.style.color = '#fff';
        if (trackingCamId && cameraData[trackingCamId]) {
            const cam = cameraData[trackingCamId];
            if (!activeTrackingPath.length) {
                activeTrackingPath = [[cam.lng, cam.lat]];
            }
        }
    } else {
        btn.textContent = 'Hiện vết di chuyển';
        btn.style.background = 'rgba(232,126,4,0.1)'; btn.style.color = '#E87E04';
    }
    updatePathLayer();
}

function closeRadial() {
    document.querySelectorAll('.radial-menu.open').forEach(r => r.classList.remove('open'));
    Object.values(markers).forEach(marker => {
        const element = marker && marker.getElement();
        if (element) element.style.zIndex = '';
    });
}

function layoutRadialMenu(radial, markerEl) {
    if (!radial || !markerEl) return;

    radial.classList.remove('upward', 'scrollable');
    radial.style.maxHeight = '';
    radial.style.overflowY = '';

    const markerRect = markerEl.getBoundingClientRect();
    const baseHeight = radial.offsetHeight || 80;
    const safeGap = 12;

    const spaceBelow = window.innerHeight - markerRect.bottom - safeGap;
    const spaceAbove = markerRect.top - safeGap;

    if (spaceBelow < baseHeight && spaceAbove > spaceBelow) {
        radial.classList.add('upward');
    }

    const available = radial.classList.contains('upward') ? spaceAbove : spaceBelow;
    const maxHeight = Math.max(56, Math.min(baseHeight, available));

    if (available < baseHeight) {
        radial.classList.add('scrollable');
        radial.style.maxHeight = `${maxHeight}px`;
        radial.style.overflowY = 'auto';
    }
}

window.onRadialAction = function(e, action, camId) {
    e.stopPropagation();
    closeRadial();

    if (action === 'Live') {
        // Open and start the native live preview immediately.
        window.notifyCSharp('markerClicked', camId);
        return;
    }
    window.notifyCSharp('spareCustomAction', { action: action, id: camId });
}

window.startTracking = function(camId) {
    const cam = cameraData[camId];
    if (!cam) return;

    const ll = toLngLatArray(cam.lat, cam.lng);
    if (!ll) return;

    // nếu đang track cam khác và cam cũ ở mode none thì path runtime sẽ mất
    if (trackingCamId && trackingCamId !== camId) {
        const oldMode = getCameraTrackingMode(trackingCamId);
        if (oldMode === TRACKING_MODE.NONE) {
            activeTrackingPath = [];
        }
    }

    trackingCamId = camId;

    document.getElementById('trackingName').textContent = cam.name;
    document.getElementById('trackingBar').classList.add('active');
    document.getElementById('btnStopTrack').style.display = 'block';

    Object.values(markers).forEach(m => m.getElement().classList.remove('tracking'));
    if (markers[camId]) markers[camId].getElement().classList.add('tracking');

    document.querySelectorAll('#cameraList li').forEach(li => li.classList.remove('active'));
    const li = document.getElementById('li-' + camId);
    if (li) li.classList.add('active');

    window.map.flyTo({ center: ll, zoom: 15 });

    const mode = getCameraTrackingMode(camId);

    if (mode === TRACKING_MODE.NONE) {
        activeTrackingPath = [ll];
    } else if (mode === TRACKING_MODE.MEMORY) {
        memoryTrackingStore[camId] = mergePaths(memoryTrackingStore[camId], [ll]);
        activeTrackingPath = [...(memoryTrackingStore[camId] || [])];
    } else if (mode === TRACKING_MODE.LOCAL) {
        const local = mergePaths(loadLocalPath(camId), [ll]);
        saveLocalPath(camId, local);
        activeTrackingPath = [...local];
    }

    updatePathLayer();
    updateTrackingBarControls();
    window.notifyCSharp('trackingChanged', { camId, tracking: true });
};

window.stopTracking = function() {
    if (trackingCamId && markers[trackingCamId]) {
        markers[trackingCamId].getElement().classList.remove('tracking');
    }

    const oldCamId = trackingCamId;
    const oldMode = oldCamId ? getCameraTrackingMode(oldCamId) : TRACKING_MODE.NONE;

    if (oldMode === TRACKING_MODE.NONE) {
        activeTrackingPath = [];
    }

    trackingCamId = null;

    document.getElementById('trackingBar').classList.remove('active');
    const stopButton = document.getElementById('btnStopTrack');
    if (stopButton) stopButton.style.display = 'none';
    document.querySelectorAll('#cameraList li').forEach(li => li.classList.remove('active'));

    updatePathLayer();
    updateTrackingBarControls();
    window.notifyCSharp('trackingChanged', { camId: null, tracking: false });
};

function rebuildSidebar() {
    /* Camera list is rendered by the WPF right sidebar. */
    return;
    /*
    const ul = document.getElementById('cameraList');
    ul.innerHTML = '';
    const keyword = document.getElementById('searchBox').value.toLowerCase();
    Object.values(cameraData).forEach(cam => {
        if (cam.isCluster) return; // Không hiển thị cụm gộp lên danh sách thanh bên
        if (keyword && !cam.name.toLowerCase().includes(keyword)) return;
        const li = document.createElement('li');
        li.id = 'li-' + cam.camID;
        if (trackingCamId === cam.camID) li.classList.add('active');
        li.innerHTML = `<div class="status-dot ${cam.isOnline ? 'online' : 'offline'}"></div><span>${cam.name}</span>`;
        li.onclick = () => window.startTracking(cam.camID);
        ul.appendChild(li);
    });
    */
}
window.filterCameras = rebuildSidebar;

function isCameraChanged(oldCam, newCam) {
    if (!oldCam) return true;

    return (
        oldCam.lat !== newCam.lat ||
        oldCam.lng !== newCam.lng ||
        oldCam.isOnline !== newCam.isOnline ||
        oldCam.heading !== newCam.heading ||
        oldCam.fov !== newCam.fov ||
        oldCam.name !== newCam.name
    );
}

// Keep labels hidden while the map is zoomed out to avoid visual clutter.
// The condition is based on the actual visible map width, not a fixed zoom:
// names appear only when the user is viewing an area no wider than 500 m.
const CAMERA_LABEL_MAX_VIEW_WIDTH_METERS = 500;
const CAMERA_LABEL_FALLBACK_ZOOM = 16;
const CAMERA_LABEL_MAX_METERS_PER_PIXEL = 5;

function getVisibleMapWidthMeters() {
    if (!window.map) return Number.POSITIVE_INFINITY;
    const bounds = window.map.getBounds();
    const west = bounds.getSouthWest();
    const east = bounds.getSouthEast();
    const radians = Math.PI / 180;
    const lat1 = west.lat * radians;
    const lat2 = east.lat * radians;
    const deltaLng = (east.lng - west.lng) * radians;
    const haversine = Math.sin((lat2 - lat1) / 2) ** 2 +
        Math.cos(lat1) * Math.cos(lat2) * Math.sin(deltaLng / 2) ** 2;
    return 2 * 6371000 * Math.asin(Math.min(1, Math.sqrt(haversine)));
}

function shouldShowCameraLabels() {
    const mapWidth = window.map && window.map.getContainer().clientWidth;
    const metersPerPixel = mapWidth > 0 ? getVisibleMapWidthMeters() / mapWidth : Number.POSITIVE_INFINITY;
    return metersPerPixel <= CAMERA_LABEL_MAX_METERS_PER_PIXEL ||
        getVisibleMapWidthMeters() <= CAMERA_LABEL_MAX_VIEW_WIDTH_METERS ||
        (window.map && window.map.getZoom() >= CAMERA_LABEL_FALLBACK_ZOOM);
}

let cameraLabelLayer = null;

function ensureCameraLabelLayer() {
    if (cameraLabelLayer || !window.map) return cameraLabelLayer;
    cameraLabelLayer = document.createElement('div');
    cameraLabelLayer.className = 'camera-label-layer';
    window.map.getContainer().appendChild(cameraLabelLayer);
    return cameraLabelLayer;
}

function updateCameraMarkerLabel(marker, name) {
    const markerEl = marker.getElement();
    const cameraId = markerEl.id.replace(/^cam-/, '');
    const layer = ensureCameraLabelLayer();
    if (!layer || !cameraId) return;

    // Labels used to live inside each marker. A sibling marker could then
    // paint its pin on top of the text. Keep labels in one overlay instead.
    const oldLabel = markerEl.querySelector('.camera-marker-label');
    if (oldLabel) oldLabel.remove();

    let label = layer.querySelector('[data-camera-label="' + CSS.escape(cameraId) + '"]');
    if (!label) {
        label = document.createElement('div');
        label.className = 'camera-marker-label';
        label.dataset.cameraLabel = cameraId;
        layer.appendChild(label);
    }

    label.textContent = name || 'Camera';
    // Marker positions can still change while MapLibre renders this update.
    // Keep the label hidden here; the layout pass enables it only after every
    // marker has reached its final rendered position.
    label.style.display = 'none';
    label.style.zIndex = '10';
    markerEl.classList.remove('zoom-label-visible');
}

function refreshCameraMarkerLabels() {
    Object.keys(markers).forEach(id => {
        const camera = cameraData[id];
        if (camera) updateCameraMarkerLabel(markers[id], camera.name);
    });
    scheduleCameraLabelLayout();
}

let cameraLabelLayoutQueued = false;

function rectanglesOverlap(first, second, padding) {
    return first.left < second.right + padding &&
        first.right > second.left - padding &&
        first.top < second.bottom + padding &&
        first.bottom > second.top - padding;
}

function scheduleCameraLabelLayout() {
    if (cameraLabelLayoutQueued) return;
    cameraLabelLayoutQueued = true;

    const layoutAfterMapRender = () => requestAnimationFrame(() => {
        cameraLabelLayoutQueued = false;
        const show = shouldShowCameraLabels();
        const occupied = [];
        const ids = Object.keys(markers).sort((left, right) => {
            const leftOnline = cameraData[left] && cameraData[left].isOnline ? 1 : 0;
            const rightOnline = cameraData[right] && cameraData[right].isOnline ? 1 : 0;
            return rightOnline - leftOnline || left.localeCompare(right);
        });

        ids.forEach(id => {
            const markerEl = markers[id].getElement();
            const layer = ensureCameraLabelLayer();
            const label = layer && layer.querySelector('[data-camera-label="' + CSS.escape(id) + '"]');
            if (!label) return;

            const markerRect = markerEl.getBoundingClientRect();
            const layerRect = layer.getBoundingClientRect();
            label.style.left = (markerRect.left - layerRect.left + markerRect.width / 2) + 'px';
            label.style.top = (markerRect.top - layerRect.top - 3) + 'px';
            // The legacy in-marker label uses bottom:43px.  In the overlay
            // that would combine with top and stretch into a tall rectangle.
            label.style.bottom = 'auto';
            label.style.transform = 'translate(-50%, -100%)';
            label.style.display = show && markerEl.style.display !== 'none' ? 'block' : 'none';
            if (!show || markerEl.style.display === 'none') return;

            const rect = label.getBoundingClientRect();
            if (occupied.some(other => rectanglesOverlap(rect, other, 5))) {
                label.style.display = 'none';
                return;
            }
            occupied.push(rect);
        });
    });

    // All points/offsets are rendered first, then labels are measured. This
    // prevents labels from colliding because of stale marker positions.
    if (window.map) {
        window.map.once('idle', layoutAfterMapRender);
        window.map.triggerRepaint();
    } else {
        layoutAfterMapRender();
    }
}

function updateCameras(camList) {
    const renderList = camList
        .filter(c => !c.isCluster)
        .map(c => {
            const n = normalizeLatLng(c.lat, c.lng);
            return {
                ...c,
                lat: n.lat,
                lng: n.lng,
                _coordValid: n.valid
            };
        })
        .filter(c => c._coordValid);

    const activeIds = new Set(renderList.map(c => c.camID));

    // 🗑 Xóa camera không còn tồn tại
    for (const id in markers) {
        if (!activeIds.has(id)) {
            markers[id].remove();
            delete markers[id];
            delete cameraData[id];
            const label = cameraLabelLayer && cameraLabelLayer.querySelector('[data-camera-label="' + CSS.escape(id) + '"]');
            if (label) label.remove();
        }
    }

    // ➕ Thêm mới hoặc cập nhật khi có thay đổi
    renderList.forEach(cam => {
        const oldCam = cameraData[cam.camID];
        const ll = [cam.lng, cam.lat];

        // Nếu không thay đổi thì bỏ qua
        if (!isCameraChanged(oldCam, cam)) return;

        cameraData[cam.camID] = cam;

        // ➕ Thêm mới marker
        if (!markers[cam.camID]) {
            const popup = new maplibregl.Popup({
                offset: 25,
                closeButton: false,
                closeOnClick: false
            }).setHTML(`
                <strong>${cam.name}</strong><br/>
                Status: ${cam.isOnline
                    ? '<span style="color:#4caf50">Online</span>'
                    : '<span style="color:#888">Offline</span>'}
            `);

            const marker = new maplibregl.Marker({
                anchor: 'bottom',
                offset: [Number(cam.offsetX) || 0, CAMERA_MARKER_TIP_OFFSET_Y + (Number(cam.offsetY) || 0)],
                pitchAlignment: 'viewport',
                rotationAlignment: 'viewport'
            })
                .setLngLat(ll)
                .setPopup(popup)
                .addTo(window.map);

            const markerEl = marker.getElement();
            markerEl.id = 'cam-' + cam.camID;
            markerEl.classList.add('camera-marker-default');
            if (!cam.isOnline) markerEl.classList.add('offline');
            updateCameraMarkerLabel(marker, cam.name);

            markerEl.addEventListener('click', (e) => {
                e.stopPropagation();
                if (isOverviewMap) return;
                closeRadial();
                // Do not force zoom 15: it made a zoomed-in map jump back out.
                window.map.flyTo({ center: ll });
                // A marker click only focuses the map; it must not start a
                // second native playback pipeline.
            });

            markerEl.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                e.stopPropagation();
                closeRadial();

                // Right-click only opens the camera menu and must preserve zoom too.
                window.map.flyTo({ center: ll });

                let radial = markerEl.querySelector('.radial-menu');
                if (!radial) {
                    radial = createRadialMenu(cam.camID);
                    markerEl.appendChild(radial);
                }

                // A radial menu belongs above all markers and their labels.
                // Without raising the marker container, a nearby pin can paint
                // over an action and make it impossible to click.
                markerEl.style.zIndex = '2001';
                radial.style.zIndex = '2002';

                layoutRadialMenu(radial, markerEl);
                
                // Show with animation
                setTimeout(() => radial.classList.add('open'), 10);
            });

            markerEl.addEventListener('mouseenter', () => marker.togglePopup());
            markerEl.addEventListener('mouseleave', () => marker.togglePopup());

            markers[cam.camID] = marker;
        }
        // 🔄 Cập nhật marker nếu thay đổi
        else {
            const marker = markers[cam.camID];
            marker.setLngLat(ll);
            marker.setOffset([Number(cam.offsetX) || 0, CAMERA_MARKER_TIP_OFFSET_Y + (Number(cam.offsetY) || 0)]);

            const el = marker.getElement();
            el.classList.toggle('offline', !cam.isOnline);
            el.classList.toggle('tracking', trackingCamId === cam.camID);
            updateCameraMarkerLabel(marker, cam.name);

            const popup = marker.getPopup();
            if (popup) {
                popup.setHTML(`
                    <strong>${cam.name}</strong><br/>
                    Status: ${cam.isOnline
                        ? '<span style="color:#4caf50">Online</span>'
                        : '<span style="color:#888">Offline</span>'}
                `);
            }
        }
    });

    function createRadialMenu(camId) {
        const div = document.createElement('div');
        div.className = 'radial-menu';
        div.innerHTML = `
            <div class="radial-btn" onclick="onRadialAction(event, 'Live', '${camId}')" title="Xem trực tiếp (Live)">${ICONS.Live}</div>
            <div class="radial-btn" onclick="onRadialAction(event, 'Info', '${camId}')" title="Thông tin">${ICONS.Info}</div>
            <div class="radial-btn" onclick="onRadialAction(event, 'Alert', '${camId}')" title="Báo động">${ICONS.Alert}</div>
        `;
        return div;
    }

    // Cập nhật UI liên quan
    rebuildSidebar();
    updateFOVSource();
    applyOverlapClusters(renderList);
    scheduleCameraLabelLayout();

    // Cập nhật tracking nếu đang theo dõi
    if (trackingCamId) {
        const tc = cameraData[trackingCamId];
        if (tc) {
            const ll = toLngLatArray(tc.lat, tc.lng);
            const lastPos = activeTrackingPath[activeTrackingPath.length - 1];

            if (!lastPos || lastPos[0] !== ll[0] || lastPos[1] !== ll[1]) {
                appendTrackingPoint(trackingCamId, ll);
                updatePathLayer();
            }
        }
    }
}

function clearOverlapClusters() {
    Object.keys(overlapClusterMarkers).forEach(id => {
        overlapClusterMarkers[id].remove();
        delete overlapClusterMarkers[id];
    });
}

function applyOverlapClusters(camList) {
    if (!window.map) return;

    clearOverlapClusters();

    // Reset visible camera markers first
    camList.forEach(cam => {
        const el = document.getElementById('cam-' + cam.camID);
        if (el) el.style.display = '';
        const marker = markers[cam.camID];
        if (marker) {
            marker.setOffset([
                Number(cam.offsetX) || 0,
                CAMERA_MARKER_TIP_OFFSET_Y + (Number(cam.offsetY) || 0)
            ]);
        }
    });

    if (camList.length <= 1) return;

    // A pin is 36 x 48 px. 28 px only detected exact overlaps, leaving
    // vertically adjacent pins visually stacked. Group before their full
    // hit areas touch so a map never renders one camera on top of another.
    const thresholdPx = 58;
    const points = camList.map(cam => {
        const p = window.map.project([cam.lng, cam.lat]);
        return { cam, x: p.x, y: p.y };
    });

    const used = new Set();

    for (let i = 0; i < points.length; i++) {
        if (used.has(points[i].cam.camID)) continue;

        const group = [points[i]];
        used.add(points[i].cam.camID);

        for (let j = i + 1; j < points.length; j++) {
            if (used.has(points[j].cam.camID)) continue;
            const dx = points[j].x - points[i].x;
            const dy = points[j].y - points[i].y;
            if ((dx * dx + dy * dy) <= (thresholdPx * thresholdPx)) {
                group.push(points[j]);
                used.add(points[j].cam.camID);
            }
        }

        if (group.length > 1) {
            // When looking at 500 m or less, always spread nearby cameras
            // around their shared point. This is local to the browser so it
            // never relies on a delayed native map-data refresh.
            if (getVisibleMapWidthMeters() <= CAMERA_LABEL_MAX_VIEW_WIDTH_METERS) {
                // Keep both the 48px pins and their labels readable when a
                // close zoom expands a cluster into its members.
                const radius = 62 + Math.min(group.length * 4, 28);
                group.forEach((entry, index) => {
                    const angle = (Math.PI * 2 * index) / group.length - Math.PI / 2;
                    const marker = markers[entry.cam.camID];
                    const markerEl = marker && marker.getElement();
                    if (marker) marker.setOffset([
                        Math.cos(angle) * radius,
                        CAMERA_MARKER_TIP_OFFSET_Y + Math.sin(angle) * radius
                    ]);
                    if (markerEl) markerEl.style.display = '';
                });
                continue;
            }

            // Hide member markers
            group.forEach(g => {
                const el = document.getElementById('cam-' + g.cam.camID);
                if (el) el.style.display = 'none';
            });

            // Representative center by average lat/lng
            const avgLng = group.reduce((s, g) => s + g.cam.lng, 0) / group.length;
            const avgLat = group.reduce((s, g) => s + g.cam.lat, 0) / group.length;

            const clusterId = 'overlap_' + group.map(g => g.cam.camID).sort().join('_');
            const el = document.createElement('div');
            const count = group.length;
            const onlineCount = group.filter(g => g.cam.isOnline).length;
            const sizeClass = count >= 10 ? 'size-lg' : (count >= 5 ? 'size-md' : 'size-sm');
            el.className = `overlap-cluster-marker ${sizeClass}${onlineCount === 0 ? ' all-offline' : ''}`;
            el.innerHTML = `<span>${count}</span><small>${onlineCount} online</small>`;
            el.title = `${count} cameras, ${onlineCount} online. Click to zoom in; right-click for the list.`;

            const clusterMarker = new maplibregl.Marker({
                element: el,
                anchor: 'bottom',
                pitchAlignment: 'viewport',
                rotationAlignment: 'viewport'
            })
                .setLngLat([avgLng, avgLat])
                .addTo(window.map);

            el.addEventListener('click', (e) => {
                e.stopPropagation();
                window.map.flyTo({ center: [avgLng, avgLat], zoom: Math.min(window.map.getZoom() + 2, 20), duration: 400 });
            });

           el.addEventListener('contextmenu', (e) => {
                e.preventDefault();
                e.stopPropagation();

                closeRadial();
                closeClusterUI();

                const cams = group.map(g => g.cam);

              
                const panel = createClusterPanel(cams, avgLng, avgLat);
                document.body.appendChild(panel);
                requestAnimationFrame(() => panel.classList.add('open'));
                
            });

            overlapClusterMarkers[clusterId] = clusterMarker;
        }
    }
}

function createClusterPanel(cameras, lng, lat) {
    const wrapper = document.createElement('div');
    wrapper.className = 'cluster-panel-wrapper';

    const point = window.map.project([lng, lat]);
    wrapper.style.left = `${point.x}px`;
    wrapper.style.top = `${point.y}px`;

    const panel = document.createElement('div');
    panel.className = 'cluster-panel';

    panel.innerHTML = `
        <div class="cluster-header">${cameras.length} cameras</div>
        <div class="cluster-grid">
            ${cameras.map(cam => `
                <div class="cluster-item" data-id="${cam.camID}">
                    <div class="cam-name">${cam.name}</div>
                    <div class="cluster-actions">
                        <button title="Xem trực tiếp">▶</button>
                        <button title="Thông tin">i</button>
                        <button title="Cảnh báo">!</button>
                    </div>
                </div>
            `).join('')}
        </div>
    `;

    // Compact list layout used by the current VMS map design.
    panel.querySelector('.cluster-header').textContent = `${cameras.length} Cameras`;
    panel.querySelector('.cluster-grid').innerHTML = cameras.map(cam => `
        <div class="cluster-item" data-id="${cam.camID}">
            <div class="cluster-camera-name"><span class="cluster-status-dot"></span><span>${cam.name}</span></div>
            <div class="cluster-actions">
                <button title="Xem trực tiếp"><span class="cluster-action-label">▶</span><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="6" width="12" height="12" rx="2"/><path d="m15 10 5-3v10l-5-3"/></svg></button>
                <button title="Phát lại"><span class="cluster-action-label">i</span><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8.5"/><path d="m10 8.5 5 3.5-5 3.5z"/></svg></button>
                <button title="Thông tin"><span class="cluster-action-label">!</span><svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="8.5"/><path d="M12 10v5M12 7.2v.2"/></svg></button>
            </div>
        </div>
    `).join('');

    wrapper.appendChild(panel);

    // animation mở panel
    requestAnimationFrame(() => {
        wrapper.classList.add('open');
    });

    // ===== EVENT =====
    panel.querySelectorAll('.cluster-item').forEach(el => {
        const id = el.dataset.id;

        el.addEventListener('mouseenter', () => {
            markers[id]?.getElement().classList.add('highlight');
        });

        el.addEventListener('mouseleave', () => {
            markers[id]?.getElement().classList.remove('highlight');
        });

        el.addEventListener('click', (e) => {
            if (e.target.tagName === 'BUTTON') return;

            e.stopPropagation();
            startTracking(id);
            closeClusterUI();
        });
    });

    // button action
    panel.querySelectorAll('.cluster-actions button').forEach(btn => {
        btn.addEventListener('click', (e) => {
            e.stopPropagation();

            const item = btn.closest('.cluster-item');
            const id = item.dataset.id;

            if (btn.textContent === '▶') {
                onRadialAction(e, 'Live', id);
            } else if (btn.textContent === 'i') {
                onRadialAction(e, 'Info', id);
            } else {
                onRadialAction(e, 'Alert', id);
            }
        });
    });

    return wrapper;
}

function closeClusterPanel() {
    document.querySelectorAll('.cluster-panel-wrapper').forEach(p => p.remove());
}
function closeClusterUI() {
    document.querySelectorAll('.cluster-panel-wrapper').forEach(e => e.remove());
    document.querySelectorAll('.cluster-radial').forEach(e => e.remove());
}

window.map.on('click', closeClusterUI);
window.map.on('dragstart', closeClusterUI);
window.map.on('zoom', closeClusterUI)

function updatePositions(positions) {
    for (const [id, pos] of Object.entries(positions)) {
        const ll = toLngLatArray(pos.lat, pos.lng);
        if (!ll) continue;

        if (markers[id]) {
            const camera = cameraData[id] || {};
            markers[id].setOffset([
                Number(camera.offsetX) || 0,
                CAMERA_MARKER_TIP_OFFSET_Y + (Number(camera.offsetY) || 0)
            ]);
            markers[id].setLngLat(ll);
            if (cameraData[id]) { cameraData[id].lng = ll[0]; cameraData[id].lat = ll[1]; }
        }
    }
    if (trackingCamId && cameraData[trackingCamId]) {
        const tc = cameraData[trackingCamId];
        const ll = toLngLatArray(tc.lat, tc.lng);

        if (ll) {
            const center = window.map.getCenter();
            const dx = Math.abs(center.lng - ll[0]);
            const dy = Math.abs(center.lat - ll[1]);

            if (dx > 0.0003 || dy > 0.0003) {
                window.map.easeTo({ center: ll, duration: 300 });
            }

            appendTrackingPoint(trackingCamId, ll);
            updatePathLayer();
        }
    }
    updateFOVSource();
    applyOverlapClusters(Object.values(cameraData).filter(c => !c.isCluster));
}

window.map.on('style.load', initPathLayers);

window.map.on('zoom', () => {
    // Recompute grouping for the current projected pixel positions. Without
    // this, pins that were safe at the previous zoom stayed stacked after a
    // user zoomed in or out until the next native camera-status refresh.
    applyOverlapClusters(Object.values(cameraData));
    refreshCameraMarkerLabels();
    if(window.notifyCSharp) {
        window.notifyCSharp('zoomChanged', window.map.getZoom());
    }
});
window.map.on('moveend', scheduleCameraLabelLayout);

window.clearTrackingData = async function(camId) {
    const ok = await showConfirmDialog(
            `Xóa toàn bộ dữ liệu tracking của camera ${camId}?`
        );
    if (!ok) return;

    delete memoryTrackingStore[camId];
    clearLocalPath(camId);

    if (trackingCamId === camId) {
        activeTrackingPath = [];
        updatePathLayer();
    }
};

window.clearAllTrackingData = async function() {
    const ok = await showConfirmDialog('Xóa toàn bộ dữ liệu tracking của tất cả camera?');
    if (!ok) return;

    Object.keys(memoryTrackingStore).forEach(k => delete memoryTrackingStore[k]);

    Object.keys(localStorage).forEach(k => {
        if (k.startsWith(LS_PREFIX)) {
            localStorage.removeItem(k);
        }
    });

    activeTrackingPath = [];
    updatePathLayer();
};

function updateTrackingBarControls() {
    const modeSelect = document.getElementById('trackingModeSelect');
    const clearBtn = document.getElementById('btnClearCurrentTracking');
    if (!modeSelect || !clearBtn) return;

    const hasCam = !!trackingCamId;
    modeSelect.disabled = !hasCam;
    clearBtn.disabled = !hasCam;

    if (hasCam) {
        modeSelect.value = getCameraTrackingMode(trackingCamId);
    }
}

window.onTrackingModeChanged = async function(newMode) {
    if (!trackingCamId) return;
    const ok = await setCameraTrackingMode(trackingCamId, newMode);
    if (!ok) {
        updateTrackingBarControls();
        return;
    }
    updatePathLayer();
    updateTrackingBarControls();
}

window.clearCurrentTrackingData = function() {
    if (!trackingCamId) return;
    window.clearTrackingData(trackingCamId);
    updateTrackingBarControls();
}

// initialize tracking bar controls state
updateTrackingBarControls();
