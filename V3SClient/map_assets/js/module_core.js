// module_core.js - Xử lý khởi tạo MapLibre và C# Bridge

window.notifyCSharp = function(action, data) {
    if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage({ action: action, data: data });
    } else {
        console.log('C# ←', action, data);
    }
};

window.onerror = function(msg, url, line, col, error) {
    notifyCSharp('error', `JS GLOBAL ERROR: ${msg} at ${url}:${line}:${col}`);
    return false;
};
window.onunhandledrejection = function(event) {
    notifyCSharp('error', `JS UNHANDLED PROMISE: ${event.reason}`);
};

(function() {
    const oldLog = console.log;
    console.log = function(...args) {
        notifyCSharp('console', args.join(' '));
        oldLog.apply(console, args);
    };
    const oldError = console.error;
    console.error = function(...args) {
        notifyCSharp('error', args.join(' '));
        oldError.apply(console, args);
    };
})();

// Khởi tạo bản đồ 3D offline (Tile giữ chỗ)
window.map = new maplibregl.Map({
    container: 'map',
    style: {
        "version": 8,
        "sources": {
            "osm-tiles": {
                "type": "raster",
                "tiles": ["data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="],
                "tileSize": 256,
                "attribution": "© iVista Maps",
                "maxzoom": 18 // Mức zoom tối đa của ảnh nguồn
            }
        },
        "layers": [
            {
                "id": "background-fallback",
                "type": "background",
                "paint": { "background-color": "#f8f9fa" }
            },
            {
                "id": "osm-tiles-layer", "type": "raster", "source": "osm-tiles",
                "minzoom": 6, "maxzoom": 22 // Cho phép layer hiển thị quá maxzoom của nguồn (Overzooming)
            }
        ]
    },
    zoom: 12, pitch: 45, bearing: 0,
    maxZoom: 18, // Giới hạn nhìn cận cảnh nhất (17 ~ thước đo 50-100m)
    minZoom: 6, // Giới hạn nhìn bao quát tối đa (~ thước đo 1000m)
    failIfMajorPerformanceCaveat: false
});

// Tự động resize map khi cửa sổ thay đổi kích thước
window.addEventListener('resize', () => {
    if (window.map) window.map.resize();
});

// Quan trọng: Báo về C# khi thay đổi mức zoom để tính toán cấu trúc (Clustering)
window.map.on('zoomend', () => {
    notifyCSharp('zoomChanged', window.map.getZoom());
});

window.map.on('error', (e) => {
    console.error('MapLibre Error:', e.error ? e.error.message : e);
});

// Nút điều khiển xoay 3D
window.map.addControl(new maplibregl.NavigationControl({ visualizePitch: true }), 'top-right');

class Toggle3D {
    onAdd(m) {
        this._map = m;
        this._el = document.createElement('div');
        this._el.className = 'maplibregl-ctrl maplibregl-ctrl-group';
        const b = document.createElement('button');
        b.type = 'button'; b.title = '2D / 3D';
        b.innerHTML = '<span style="font-weight:bold;color:#444">2D</span>';
        b.onclick = () => {
            if (m.getPitch() > 10) { m.easeTo({ pitch: 0, bearing: 0 }); b.innerHTML = '<span style="font-weight:bold;color:#444">3D</span>'; }
            else { m.easeTo({ pitch: 60 }); b.innerHTML = '<span style="font-weight:bold;color:#444">2D</span>'; }
        };
        m.on('pitchend', () => { b.innerHTML = m.getPitch() > 10
            ? '<span style="font-weight:bold;color:#444">2D</span>'
            : '<span style="font-weight:bold;color:#444">3D</span>'; });
        this._el.appendChild(b);
        return this._el;
    }
    onRemove() { this._el.remove(); }
}
window.map.addControl(new Toggle3D(), 'top-right');
window.map.addControl(new maplibregl.ScaleControl({ maxWidth: 200, unit: 'metric' }), 'bottom-left');

// Lắng nghe sự kiện từ C# (WebView2)
if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', function(event) {
        const cmd = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;

        // Lệnh mặc định của Core
        if (cmd.action === 'setTileUrl' || cmd.action === 'setTileUrl') {
            const tiles = Array.isArray(cmd.urls) ? cmd.urls : [cmd.urls];
            console.log('[Map] Applying new Tile URLs:', JSON.stringify(tiles));
            const s = window.map.getStyle();
            if (s && s.sources['osm-tiles']) { 
                s.sources['osm-tiles'].tiles = tiles; 
                window.map.setStyle(s); 
            }
        }

        if (cmd.action === 'flyTo' && Number.isFinite(cmd.lng) && Number.isFinite(cmd.lat)) {
            window.map.flyTo({ center: [cmd.lng, cmd.lat], duration: 300 });
        }

        // Bắn tín hiệu để các script module động (Live/Trajectory) thu nhận
        document.dispatchEvent(new CustomEvent('mapCommand', { detail: cmd }));
    });
}

// Map Ready Event
window.map.on('load', () => {
    notifyCSharp('mapLoaded', {});
});
