// module_trajectory.js - UI + trajectory rendering for WebView2 bridge

let trajMarkers = [];
const TRAJ_SOURCE = 'ai-traj-source';
const TRAJ_LAYER = 'ai-traj-layer';
let trajPoints = [];
let trajActiveIndex = -1;
let trajActiveMarker = null;
let trajPlaybackMarker = null;
let trajPlaybackTimer = null;
let trajPlaybackIndex = -1;
let trajPlaybackMetersPerSecond = 100;
let trajPlaybackDistance = -1;
let trajPlaybackLastTick = 0;
let trajPlaybackCumDistances = [];
let trajPlaybackTotalDistance = 0;
let trajPlaybackFocusEnabled = true;
let trajPlaybackNeedInitialFocus = true;
let trajPlaybackFocusAnimUntil = 0;
let trajImagePreviewModal = null;
let trajImagePreviewEl = null;

const trajectoryHtml = `
<div id="trajTimeline" class="traj-panel traj-timeline" style="position:absolute;top:10px;left:10px;height:500px;width:260px;z-index:500;background:rgba(20,20,20,.88);color:#fff;border-radius:8px;display:flex;flex-direction:column;overflow:hidden;">
  <div id="traj-timeline-panel" class=" traj-panel-header-tight">
    <div class="accent-bar"></div>
    <span class="traj-panel-title">Timeline</span>
  </div>
  <div id="trajTimelineBody" class="traj-panel-body" style="padding:6px;overflow:auto;flex:1;"></div>
  <button id="trajTimelineToggle" class="trajTimeline-collapse-btn" type="button">◀</button>
</div>

<div id="trajToolbar" class="traj-toolbar" style="position:absolute;top:10px;z-index:500;">
  <div id="trajToolbarBody" class="traj-toolbar-body">
    <div class="traj-playback-group">
      <label class="traj-playback-label">PLAYBACK</label>
      <div class="traj-playback-controls">
          <div id="trajPlaybackPulse" class="traj-playback-pulse is-off" title="Playback state"></div>
          <button id="btnTrajPlay" class="traj-playback-btn traj-playback-btn-play" type="button">▶ Play</button>
          <button id="btnTrajPause" class="traj-playback-btn traj-playback-btn-pause is-hidden" type="button">⏸ Pause</button>
          <button id="btnTrajStop" class="traj-playback-btn traj-playback-btn-stop" type="button">■ Stop</button>
          <button id="btnTrajFocus" class="traj-playback-btn traj-playback-btn-focus is-on" type="button">Focus: ON</button>
          <input id="trajPlaybackMps" class="traj-playback-speed" type="number" min="10" step="10" value="100" /> m/s
      </div>
    </div>
  </div>
  <div id="trajSummary">ID: — | Points: 0 | Start: — | End: —</div>
  <div id="trajStatus"></div>
  <button id="trajToolbarToggle" class="traj-collapse-btn traj-collapse-btn-bottom" type="button">▲</button>
</div>

<div id="trajImagePreviewModal" class="traj-image-preview-modal is-hidden">
  <div class="traj-image-preview-backdrop" data-role="close"></div>
  <div class="traj-image-preview-dialog" role="dialog" aria-modal="true">
    <button id="trajImagePreviewClose" class="traj-image-preview-close" type="button" aria-label="Close">✕</button>
    <img id="trajImagePreviewImg" class="traj-image-preview-img" alt="Trajectory image preview" />
  </div>
</div>
`;

document.body.insertAdjacentHTML('beforeend', trajectoryHtml);

trajImagePreviewModal = document.getElementById('trajImagePreviewModal');
trajImagePreviewEl = document.getElementById('trajImagePreviewImg');

function closeTrajectoryImagePreview() {
  if (!trajImagePreviewModal) return;
  trajImagePreviewModal.classList.add('is-hidden');
  if (trajImagePreviewEl) {
    trajImagePreviewEl.src = '';
  }
}

function openTrajectoryImagePreview(imageUrl) {
  if (!trajImagePreviewModal || !trajImagePreviewEl || !imageUrl) return;
  trajImagePreviewEl.src = imageUrl;
  trajImagePreviewModal.classList.remove('is-hidden');
}

trajImagePreviewModal?.addEventListener('click', (e) => {
  if (trajImagePreviewModal.classList.contains('is-hidden')) return;

  const clickedImage = e.target?.closest?.('#trajImagePreviewImg');
  if (clickedImage) return;

  closeTrajectoryImagePreview();
});

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    closeTrajectoryImagePreview();
  }
});

function collapseToolbar(force) {
  const panel = document.getElementById('trajToolbar');
  const btn = document.getElementById('trajToolbarToggle');
  if (!panel || !btn) return;

  const collapsed = typeof force === 'boolean'
    ? force
    : !panel.classList.contains('collapsed');

  panel.classList.toggle('collapsed', collapsed);
  btn.textContent = collapsed ? '▼' : '▲';
}

function collapseTimeline(force) {
  const panel = document.getElementById('trajTimeline');
  const btn = document.getElementById('trajTimelineToggle');
  if (!panel || !btn) return;

  const collapsed = typeof force === 'boolean'
    ? force
    : !panel.classList.contains('collapsed');

  panel.classList.toggle('collapsed', collapsed);
  btn.textContent = collapsed ? '▶' : '◀';
  renderTimeline(trajPoints);
}

document.getElementById('trajToolbarToggle')
  .addEventListener('click', function (e) {
    e.stopPropagation();
    collapseToolbar();
  });

document.getElementById('trajTimelineToggle')
  .addEventListener('click', function (e) {
    e.stopPropagation();
    collapseTimeline();
  });

document.querySelector('#trajTimeline')
  .addEventListener('click', () => {
    collapseTimeline();
  });
document.getElementById('trajTimelineBody')?.addEventListener('click', (e) => e.stopPropagation());

function handleUserMapInteraction(e) {
  if (!trajPlaybackFocusEnabled) return;
  if (e && e.originalEvent) {
    setPlaybackFocus(false, false);
  }
}

window.map.on('dragstart', handleUserMapInteraction);
window.map.on('zoomstart', handleUserMapInteraction);
window.map.on('rotatestart', handleUserMapInteraction);
window.map.on('pitchstart', handleUserMapInteraction);

window.map.on('click', () => {
  if (trajActiveMarker && trajActiveMarker.getPopup()) {
    trajActiveMarker.getPopup().remove();
  }
  trajActiveMarker = null;

  closeTrajectoryImagePreview();

  if (!trajPlaybackTimer) {
    trajActiveIndex = -1;
    renderTimeline(trajPoints);
  }
});

function setStatus(text, color) {
  const el = document.getElementById('trajStatus');
  if (!el) return;
  el.textContent = text || '';
  el.style.color = color || '#f8c471';
}

function setSummary(id, total, start, end) {
  const el = document.getElementById('trajSummary');
  if (!el) return;
  el.textContent = `ID: ${id || '—'} | Points: ${total || 0} | Start: ${start || '—'} | End: ${end || '—'}`;
}

function hasTrajectoryPopupData(point) {
  if (!point) return false;
  return !!(point.popup || point.imageUrl || point.time || point.timestamp || point.id || point.desc || point.camera);
}

function stripHtml(value) {
  const text = String(value || '');
  const withBreaks = text.replace(/<br\s*\/?>/gi, ' | ');
  return withBreaks.replace(/<[^>]*>/g, '').trim();
}

function buildTrajectoryPopupContent(point) {
  const wrapper = document.createElement('div');
  wrapper.className = 'traj-popup-card';

  const imageArea = document.createElement('div');
  imageArea.className = 'traj-popup-image';

  if (point.imageUrl) {
    const img = document.createElement('img');
    img.src = point.imageUrl;
    img.alt = point.id || 'Trajectory image';
    img.loading = 'lazy';
    img.addEventListener('click', (e) => {
      e.preventDefault();
      e.stopPropagation();
      openTrajectoryImagePreview(point.imageUrl);
    });
    imageArea.appendChild(img);
  } else {
    const noImage = document.createElement('div');
    noImage.className = 'traj-popup-no-image';
    noImage.textContent = 'No image';
    imageArea.appendChild(noImage);
  }

  const info = document.createElement('div');
  info.className = 'traj-popup-info';

  const fallbackPopupText = point.popup ? stripHtml(point.popup) : '';
  const popupParts = fallbackPopupText ? fallbackPopupText.split('|').map(s => s.trim()).filter(Boolean) : [];
  const popupTime = popupParts.length > 1 ? popupParts[popupParts.length - 1] : '';
  const popupDesc = popupParts.length ? popupParts[0] : '';

  const timeValue = point.time || point.timestamp || popupTime || '—';
  const idValue = point.id || point.person_id || point.plate_number || point.event_id || '—';
  const descValue = point.desc || point.camera || popupDesc || fallbackPopupText || '—';

  const fields = [
    { label: 'Thời gian', value: timeValue },
    { label: 'ID', value: idValue },
    { label: 'Mô tả', value: descValue }
  ];

  fields.forEach((f) => {
    const row = document.createElement('div');
    row.className = 'traj-popup-row';

    const label = document.createElement('span');
    label.className = 'traj-popup-label';
    label.textContent = f.label;

    const value = document.createElement('span');
    value.className = 'traj-popup-value';
    value.textContent = String(f.value || '—');

    row.appendChild(label);
    row.appendChild(value);
    info.appendChild(row);
  });

  wrapper.appendChild(imageArea);
  wrapper.appendChild(info);
  return wrapper;
}

function renderTimeline(points) {
  const list = document.getElementById('trajTimelineBody');
  const timelinePanel = document.getElementById('trajTimeline');
  if (!list) return;

  list.innerHTML = '';
  if (!points || !points.length) return;

  const collapsed = timelinePanel?.classList.contains('collapsed');

  points.forEach((p, idx) => {
    const item = document.createElement('div');
    item.className = 'traj-timeline-item' + (idx === trajActiveIndex ? ' active' : '');
    item.dataset.index = String(idx + 1);

    if (collapsed) {
      item.innerHTML = '';
    } else {
      item.innerHTML = `
        <div style="font-weight:600;color:#fff;">#${idx + 1} ${p.camera || ''}</div>
        <div style="font-size:11px;color:#bbb;">${p.timestamp || ''}</div>
      `;
    }

    item.addEventListener('click', (e) => {
      e.stopPropagation();
      focusPoint(idx);
    });

    list.appendChild(item);
  });
}

function initTrajLayer() {
  if (!window.map.getSource(TRAJ_SOURCE)) {
    window.map.addSource(TRAJ_SOURCE, {
      type: 'geojson',
      data: { type: 'FeatureCollection', features: [] }
    });
    window.map.addLayer({
      id: TRAJ_LAYER,
      type: 'line',
      source: TRAJ_SOURCE,
      layout: { 'line-join': 'round', 'line-cap': 'round' },
      paint: { 'line-color': '#ff7a2f', 'line-width': 4, 'line-dasharray': [2, 2] }
    });
  }
}

function computeDistanceMeters(a, b) {
  const R = 6371000;
  const toRad = Math.PI / 180;
  const dLat = (b.lat - a.lat) * toRad;
  const dLng = (b.lng - a.lng) * toRad;
  const lat1 = a.lat * toRad;
  const lat2 = b.lat * toRad;

  const sinLat = Math.sin(dLat / 2);
  const sinLng = Math.sin(dLng / 2);
  const h = (sinLat * sinLat) + (Math.cos(lat1) * Math.cos(lat2) * sinLng * sinLng);
  const c = 2 * Math.atan2(Math.sqrt(h), Math.sqrt(1 - h));
  return R * c;
}

function rebuildPlaybackDistanceCache(points) {
  trajPlaybackCumDistances = [0];
  trajPlaybackTotalDistance = 0;

  if (!Array.isArray(points) || points.length < 2) return;

  for (let i = 1; i < points.length; i++) {
    const seg = computeDistanceMeters(points[i - 1], points[i]);
    trajPlaybackTotalDistance += seg;
    trajPlaybackCumDistances.push(trajPlaybackTotalDistance);
  }
}

function setPlaybackButtons(isPlaying) {
  const playBtn = document.getElementById('btnTrajPlay');
  const pauseBtn = document.getElementById('btnTrajPause');
  const pulseEl = document.getElementById('trajPlaybackPulse');
  if (!playBtn || !pauseBtn) return;

  playBtn.classList.toggle('is-hidden', !!isPlaying);
  pauseBtn.classList.toggle('is-hidden', !isPlaying);

  if (pulseEl) {
    pulseEl.classList.toggle('is-on', !!isPlaying);
    pulseEl.classList.toggle('is-off', !isPlaying);
  }
}

function getTrajectoryMode() {
  return 'event';
}

function applyPlaybackMarkerVisual(el) {
  if (!el) return;

  const mode = getTrajectoryMode();
  el.classList.remove('traj-marker-playback-event', 'traj-marker-playback-face', 'traj-marker-playback-plate');

  if (mode === 'face') {
    el.textContent = '👤';
    el.classList.add('traj-marker-playback-face');
    return;
  }

  if (mode === 'plate') {
    el.textContent = '🚗';
    el.classList.add('traj-marker-playback-plate');
    return;
  }

  el.textContent = '●';
  el.classList.add('traj-marker-playback-event');
}

function ensurePlaybackMarker(lng, lat) {
  if (!trajPlaybackMarker) {
    const el = document.createElement('div');
    el.className = 'traj-marker-numbered traj-marker-playback';
    applyPlaybackMarkerVisual(el);
    trajPlaybackMarker = new maplibregl.Marker({ element: el, anchor: 'center' })
      .setLngLat([lng, lat])
      .addTo(window.map);
    return;
  }

  const el = trajPlaybackMarker.getElement?.();
  applyPlaybackMarkerVisual(el);
  trajPlaybackMarker.setLngLat([lng, lat]);
}

function refreshPlaybackMarkerVisual() {
  if (!trajPlaybackMarker) return;
  const el = trajPlaybackMarker.getElement?.();
  applyPlaybackMarkerVisual(el);
}

function setPlaybackFocusButton() {
  const btn = document.getElementById('btnTrajFocus');
  if (!btn) return;

  btn.classList.toggle('is-on', trajPlaybackFocusEnabled);
  btn.classList.toggle('is-off', !trajPlaybackFocusEnabled);
  btn.textContent = trajPlaybackFocusEnabled ? 'Focus: ON' : 'Focus: OFF';
}

function stopPlayback(resetIndex = true, keepMarker = false) {
  if (trajPlaybackTimer) {
    clearInterval(trajPlaybackTimer);
    trajPlaybackTimer = null;
  }

  if (!keepMarker && trajPlaybackMarker) {
    trajPlaybackMarker.remove();
    trajPlaybackMarker = null;
  }

  if (resetIndex) {
    trajPlaybackIndex = -1;
    trajPlaybackLastTick = 0;
    trajPlaybackFocusAnimUntil = 0;
    if (!trajPoints.length) {
      trajActiveIndex = -1;
      renderTimeline([]);
    }
  }

  setPlaybackButtons(false);
}

function focusPlaybackNow(forceInitial = true) {
  if (!trajPlaybackFocusEnabled) return;

  let ll = null;
  if (trajPlaybackMarker && typeof trajPlaybackMarker.getLngLat === 'function') {
    ll = trajPlaybackMarker.getLngLat();
  }

  if (!ll && trajPoints.length && trajActiveIndex >= 0 && trajActiveIndex < trajPoints.length) {
    const p = trajPoints[trajActiveIndex];
    ll = { lng: p.lng, lat: p.lat };
  }

  if (!ll) return;

  const now = Date.now();
  if (!forceInitial && now < trajPlaybackFocusAnimUntil) {
    return;
  }

  if (forceInitial || trajPlaybackNeedInitialFocus) {
    const duration = 100;
    window.map.flyTo({ center: [ll.lng, ll.lat], duration: duration });
    trajPlaybackNeedInitialFocus = false;
    trajPlaybackFocusAnimUntil = Date.now() + duration + 30;
    return;
  }

  const isCameraAnimating =
    (typeof window.map.isMoving === 'function' && window.map.isMoving()) ||
    (typeof window.map.isZooming === 'function' && window.map.isZooming()) ||
    (typeof window.map.isRotating === 'function' && window.map.isRotating()) ||
    (typeof window.map.isPitching === 'function' && window.map.isPitching());

  if (isCameraAnimating) {
    return;
  }

  const currentZoom = window.map.getZoom();
  const currentPitch = window.map.getPitch();
  const currentBearing = window.map.getBearing();
  window.map.jumpTo({ center: [ll.lng, ll.lat], zoom: currentZoom, pitch: currentPitch, bearing: currentBearing });
}

function setPlaybackFocus(enabled, shouldRefocus = false) {
  trajPlaybackFocusEnabled = !!enabled;
  if (trajPlaybackFocusEnabled) {
    trajPlaybackNeedInitialFocus = true;
  } else {
    trajPlaybackFocusAnimUntil = 0;
  }
  setPlaybackFocusButton();

  if (trajPlaybackFocusEnabled && shouldRefocus) {
    focusPlaybackNow(true);
  }
}

function movePlaybackTo(index, fly = true) {
  if (!trajPoints.length || index < 0 || index >= trajPoints.length) return;

  const p = trajPoints[index];
  ensurePlaybackMarker(p.lng, p.lat);

  if (fly) {
    window.map.flyTo({ center: [p.lng, p.lat], duration: 450 });
  }

  trajActiveIndex = index;
  renderTimeline(trajPoints);
}

function updatePlaybackByDistance(distanceMeters) {
  if (!trajPoints.length) return;

  const maxDistance = Math.max(0, trajPlaybackTotalDistance);
  const clamped = Math.max(0, Math.min(distanceMeters, maxDistance));

  if (trajPoints.length === 1 || maxDistance <= 0 || trajPlaybackCumDistances.length < 2) {
    const p0 = trajPoints[0];
    if (!p0) return;
    ensurePlaybackMarker(p0.lng, p0.lat);
    trajActiveIndex = 0;
    renderTimeline(trajPoints);
    return;
  }

  let fromIndex = 0;
  let lo = 0;
  let hi = trajPlaybackCumDistances.length - 1;
  while (lo <= hi) {
    const mid = (lo + hi) >> 1;
    if (trajPlaybackCumDistances[mid] <= clamped) {
      fromIndex = mid;
      lo = mid + 1;
    } else {
      hi = mid - 1;
    }
  }

  const toIndex = Math.min(fromIndex + 1, trajPoints.length - 1);
  const fromDist = trajPlaybackCumDistances[fromIndex] || 0;
  const toDist = trajPlaybackCumDistances[toIndex] || fromDist;
  const segDist = Math.max(0.0001, toDist - fromDist);
  const t = Math.max(0, Math.min(1, (clamped - fromDist) / segDist));

  const from = trajPoints[fromIndex];
  const to = trajPoints[toIndex];
  const lng = from.lng + ((to.lng - from.lng) * t);
  const lat = from.lat + ((to.lat - from.lat) * t);

  ensurePlaybackMarker(lng, lat);

  focusPlaybackNow(false);

  const activeIdx = (t >= 0.5) ? toIndex : fromIndex;
  if (trajActiveIndex !== activeIdx) {
    trajActiveIndex = activeIdx;
    renderTimeline(trajPoints);
  }
}

function startPlayback() {
  if (!trajPoints.length) {
    setStatus('Chưa có dữ liệu để playback.', '#f8c471');
    return;
  }

  if (trajPoints.length === 1) {
    movePlaybackTo(0, true);
    setStatus('Playback hoàn tất.', '#7dff9b');
    setPlaybackButtons(false);
    return;
  }

  if (trajPlaybackTimer) return;

  const maxDistance = Math.max(0, trajPlaybackTotalDistance);
  if (trajPlaybackDistance < 0 || trajPlaybackDistance >= maxDistance) {
    trajPlaybackDistance = 0;
  }

  trajPlaybackNeedInitialFocus = true;
  updatePlaybackByDistance(trajPlaybackDistance);
  trajPlaybackLastTick = Date.now();
  setStatus('Đang playback lộ trình...', '#7db7ff');
  setPlaybackButtons(true);

  const speed = Math.max(0.25, trajPlaybackMetersPerSecond || 100);
  const metersPerSecond = speed;

  trajPlaybackTimer = setInterval(() => {
    const now = Date.now();
    const deltaMs = Math.max(0, now - trajPlaybackLastTick);
    trajPlaybackLastTick = now;

    trajPlaybackDistance += (metersPerSecond * (deltaMs / 1000));

    if (trajPlaybackDistance >= maxDistance) {
      trajPlaybackDistance = maxDistance;
      updatePlaybackByDistance(trajPlaybackDistance);
      stopPlayback(true, true);
      setStatus('Playback hoàn tất.', '#7dff9b');
      return;
    }

    updatePlaybackByDistance(trajPlaybackDistance);
  }, 40);
}

function pausePlayback() {
  if (!trajPlaybackTimer) return;
  clearInterval(trajPlaybackTimer);
  trajPlaybackTimer = null;
  setStatus('Đã tạm dừng playback.', '#f8c471');
  setPlaybackButtons(false);
}

function resetPlayback() {
  stopPlayback(true, false);
  trajPlaybackDistance = -1;
  trajActiveIndex = -1;
  renderTimeline(trajPoints);
  setStatus('Đã dừng playback.', '#f8c471');
  setPlaybackButtons(false);
}

function clearAll() {
  stopPlayback(true, false);
  trajPlaybackDistance = -1;
  trajPlaybackCumDistances = [];
  trajPlaybackTotalDistance = 0;

  closeTrajectoryImagePreview();

  trajMarkers.forEach(m => m.remove());
  trajMarkers = [];
  trajPoints = [];
  trajActiveIndex = -1;
  trajActiveMarker = null;

  if (window.map.getSource(TRAJ_SOURCE)) {
    window.map.getSource(TRAJ_SOURCE).setData({ type: 'FeatureCollection', features: [] });
  }

  renderTimeline([]);
  setSummary('—', 0, '—', '—');
  setPlaybackButtons(false);
}

function drawTrajectory(points) {
  clearAll();
  if (!points || points.length === 0) return;

  initTrajLayer();
  trajPoints = points;
  rebuildPlaybackDistanceCache(points);

  const coordinates = points.map(p => [p.lng, p.lat]);
  const geojsonData = {
    type: 'FeatureCollection',
    features: [{
      type: 'Feature',
      geometry: { type: 'LineString', coordinates: coordinates }
    }]
  };

  window.map.getSource(TRAJ_SOURCE).setData(geojsonData);
  const bounds = new maplibregl.LngLatBounds(coordinates[0], coordinates[0]);

  points.forEach((p, idx) => {
    const isFirst = idx === 0;
    const isLast = idx === points.length - 1;

    const el = document.createElement('div');
    el.className = 'traj-marker-numbered ' + (isFirst ? 'traj-marker-first' : (isLast ? 'traj-marker-last' : 'traj-marker-mid'));
    el.textContent = String(idx + 1);

    const marker = new maplibregl.Marker({ element: el, anchor: 'center' }).setLngLat([p.lng, p.lat]);
    if (hasTrajectoryPopupData(p)) {
      const popup = new maplibregl.Popup({ offset: 15, closeButton: false, className: 'trajectory-popup', maxWidth: '560px' })
        .setDOMContent(buildTrajectoryPopupContent(p));
      marker.setPopup(popup);
    }

    el.addEventListener('click', (e) => {
      e.stopPropagation();
      focusPoint(idx);
    });

    marker.addTo(window.map);
    trajMarkers.push(marker);
    bounds.extend([p.lng, p.lat]);
  });

  window.map.fitBounds(bounds, { padding: 50, duration: 600, pitch: 45 });
  renderTimeline(points);
}

function focusPoint(index) {
  const idx = index ?? -1;
  if (idx < 0 || idx >= trajMarkers.length) return;

  if (trajPlaybackFocusEnabled) {
    setPlaybackFocus(false, false);
  }

  const m = trajMarkers[idx];
  const ll = m.getLngLat();

  if (trajActiveMarker && trajActiveIndex === idx && trajActiveMarker.getPopup()) {
    trajActiveMarker.getPopup().remove();
    trajActiveMarker = null;
    trajActiveIndex = -1;
    renderTimeline(trajPoints);
    return;
  }

  if (trajActiveMarker && trajActiveMarker !== m && trajActiveMarker.getPopup()) {
    trajActiveMarker.getPopup().remove();
  }

  trajActiveMarker = m;
  trajActiveIndex = idx;

  window.map.flyTo({ center: ll, duration: 400 });
  if (m.getPopup()) m.getPopup().addTo(window.map);
  renderTimeline(trajPoints);
}

document.getElementById('btnTrajPlay').addEventListener('click', startPlayback);
document.getElementById('btnTrajPause').addEventListener('click', pausePlayback);
document.getElementById('btnTrajStop').addEventListener('click', resetPlayback);
document.getElementById('btnTrajFocus').addEventListener('click', () => {
  setPlaybackFocus(!trajPlaybackFocusEnabled, true);
});
document.getElementById('trajPlaybackMps').addEventListener('change', (e) => {
  const parsed = parseFloat(e.target.value || '100');
  trajPlaybackMetersPerSecond = Number.isFinite(parsed) && parsed > 0 ? parsed : 100;

  if (trajPlaybackTimer) {
    pausePlayback();
    startPlayback();
  }
});

setPlaybackButtons(false);
setPlaybackFocusButton();

document.addEventListener('mapCommand', function(e) {
  const cmd = e.detail || {};

  // old direct commands (backward compatibility)
  if (cmd.type === 'trajectory') drawTrajectory(cmd.points || []);
  if (cmd.type === 'clear') clearAll();
  if (cmd.type === 'focus') focusPoint(cmd.index);

  // bridge responses from C#
  if (cmd.action === 'trajectoryResult') {
    const data = cmd.data || {};
    const points = data.points || [];
    console.log('[Trajectory] Received trajectoryResult: points=' + points.length + ', id=' + (data.id || '—'));
    trajActiveIndex = -1;
    trajActiveMarker = null;
    drawTrajectory(points);
    setSummary(data.id, data.total, data.start, data.end);
    setStatus(points.length ? 'Đã tải lộ trình.' : 'Không có dữ liệu.', points.length ? '#7dff9b' : '#f8c471');
  }

  if (cmd.action === 'clearTrajectory') {
    clearAll();
  }

  if (cmd.action === 'focusPoint') {
    focusPoint(cmd.index);
  }

  if (cmd.action === 'trajectoryEmpty') {
    clearAll();
    setStatus((cmd.data && cmd.data.message) ? cmd.data.message : 'Không có dữ liệu.', '#f8c471');
  }

  if (cmd.action === 'trajectoryError') {
    clearAll();
    setStatus((cmd.data && cmd.data.message) ? cmd.data.message : 'Lỗi tải lộ trình.', '#ff8f8f');
  }
});
