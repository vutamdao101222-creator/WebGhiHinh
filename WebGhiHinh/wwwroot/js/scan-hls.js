// ======================================================
// AI AUTO SCAN HLS – jsQR (main thread)
// ======================================================

// 1 canvas dùng chung để xử lý ảnh
const sharedCanvas = document.createElement("canvas");
const sharedCtx = sharedCanvas.getContext("2d", { willReadFrequently: true });

// Cấu hình
const SCAN_INTERVAL = 180;      // ms giữa 2 lần quét mỗi cam
const instances = new Map();

function log() {
    console.log("[SCAN]", ...arguments);
}

// ======================================================
// CLASS INSTANCE
// ======================================================
class ScannerInstance {
    constructor(videoEl, canvasEl, dotRef, stationName) {
        this.video = videoEl;
        this.canvas = canvasEl;
        this.ctx = canvasEl.getContext("2d");
        this.station = stationName;
        this.dotRef = dotRef;
        this.hls = null;
        this.enabled = true;

        this.lastScanValue = "";
        this.seenCount = 0;
        this.lastScanTime = 0;
        this.lastDetectTime = 0;
    }

    destroy() {
        this.enabled = false;
        if (this.hls) {
            try { this.hls.destroy(); } catch { }
            this.hls = null;
        }
        if (this.video) {
            try {
                this.video.pause();
                this.video.removeAttribute("src");
                this.video.load();
            } catch { }
        }
    }
}

// ======================================================
// PUBLIC API (Blazor gọi)
// ======================================================
window.startHlsScanByElementId = function (hlsUrl, videoId, canvasId, dotRef, enableScan, stationName) {
    const video = document.getElementById(videoId);
    const canvas = document.getElementById(canvasId);

    if (!video || !canvas) {
        console.error("[SCAN] video/canvas not found:", videoId, canvasId);
        return;
    }

    if (!window.Hls) {
        console.error("[SCAN] Hls.js not loaded!");
        return;
    }

    // Clear instance cũ nếu có
    if (instances.has(video)) {
        instances.get(video).destroy();
        instances.delete(video);
    }

    const inst = new ScannerInstance(video, canvas, dotRef, stationName);
    inst.enabled = enableScan !== false;
    instances.set(video, inst);

    setupVideoStream(inst, hlsUrl);
    requestAnimationFrame(() => instanceLoop(inst));

    log("Start for", stationName, "=>", hlsUrl);
};

window.stopHlsScan = function () {
    instances.forEach(inst => inst.destroy());
    instances.clear();
    log("Stop all scans");
};

// ======================================================
// SETUP HLS VIDEO
// ======================================================
function setupVideoStream(inst, url) {
    const video = inst.video;

    video.crossOrigin = "anonymous";
    video.muted = true;
    video.autoplay = true;
    video.playsInline = true;
    video.style.objectFit = "fill";

    try {
        if (Hls.isSupported()) {
            const h = new Hls({
                enableWorker: true,
                lowLatencyMode: true,
                backBufferLength: 0
            });
            h.loadSource(url);
            h.attachMedia(video);
            h.on(Hls.Events.MANIFEST_PARSED, () => {
                video.play().catch(e => console.warn("Autoplay blocked:", e));
            });
            h.on(Hls.Events.ERROR, (event, data) => {
                if (data.fatal) console.error("[SCAN] Hls fatal error:", data);
            });
            inst.hls = h;
        } else {
            // Fallback – browser support HLS native
            video.src = url;
            video.play().catch(e => console.warn("Native HLS play error:", e));
        }
    } catch (e) {
        console.error("[SCAN] setupVideoStream error:", e);
    }
}

// ======================================================
// LOOP VẼ OVERLAY + XẾP QUEUE QUÉT
// ======================================================
function instanceLoop(inst) {
    if (!instances.has(inst.video)) return;

    const canvas = inst.canvas;
    const ctx = inst.ctx;
    const vid = inst.video;

    // Sync kích thước canvas overlay với kích thước hiển thị
    const w = canvas.clientWidth || vid.clientWidth;
    const h = canvas.clientHeight || vid.clientHeight;
    if (w > 0 && h > 0 && (canvas.width !== w || canvas.height !== h)) {
        canvas.width = w;
        canvas.height = h;
    }

    // Clear overlay mỗi frame
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    // Thử quét
    if (inst.enabled) {
        detectFrame(inst);
    }

    requestAnimationFrame(() => instanceLoop(inst));
}

// ======================================================
// QUÉT 1 FRAME BẰNG jsQR
// ======================================================
function detectFrame(inst) {
    const vid = inst.video;

    if (!vid || !inst.enabled) return;
    if (vid.readyState < 2) return; // chưa có frame video

    const now = Date.now();
    if (now - inst.lastDetectTime < SCAN_INTERVAL) return;
    inst.lastDetectTime = now;

    if (!window.jsQR) {
        console.error("[SCAN] jsQR library not found on window!");
        inst.enabled = false;
        return;
    }

    // Lấy kích thước video thật
    const vw = vid.videoWidth;
    const vh = vid.videoHeight;
    if (!vw || !vh) return;

    // Thu nhỏ về 800px theo chiều ngang để giảm tải
    const procW = 800;
    const procH = Math.round(vh * (procW / vw));

    sharedCanvas.width = procW;
    sharedCanvas.height = procH;

    try {
        // Tăng tương phản + sáng cho dễ đọc QR
        sharedCtx.filter = "contrast(130%) brightness(120%)";
        sharedCtx.drawImage(vid, 0, 0, procW, procH);
    } catch (e) {
        console.error("[SCAN] drawImage error (CORS?):", e);
        return;
    }

    let imageData;
    try {
        imageData = sharedCtx.getImageData(0, 0, procW, procH);
    } catch (e) {
        console.error("[SCAN] getImageData error:", e);
        return;
    }

    const qr = jsQR(imageData.data, imageData.width, imageData.height);
    if (!qr) return;

    // Vẽ khung + text lên overlay
    drawQrOverlay(inst, qr, procW, procH);

    // Xử lý debounce + gọi về Blazor
    processCode(inst, qr.data || "");
}

// ======================================================
// VẼ KHUNG QR
// ======================================================
function drawQrOverlay(inst, qr, procW, procH) {
    const canvas = inst.canvas;
    const ctx = inst.ctx;

    const cw = canvas.width;
    const ch = canvas.height;
    if (!cw || !ch) return;

    const scaleX = cw / procW;
    const scaleY = ch / procH;

    const loc = qr.location;
    const pts = [
        loc.topLeftCorner,
        loc.topRightCorner,
        loc.bottomRightCorner,
        loc.bottomLeftCorner
    ];

    ctx.lineWidth = 4;
    ctx.strokeStyle = "#00ff00";
    ctx.beginPath();
    ctx.moveTo(pts[0].x * scaleX, pts[0].y * scaleY);
    ctx.lineTo(pts[1].x * scaleX, pts[1].y * scaleY);
    ctx.lineTo(pts[2].x * scaleX, pts[2].y * scaleY);
    ctx.lineTo(pts[3].x * scaleX, pts[3].y * scaleY);
    ctx.closePath();
    ctx.stroke();

    const text = qr.data || "";
    if (text) {
        ctx.font = "bold 16px Arial";
        ctx.fillStyle = "#00ff00";
        ctx.fillText(text, 10, 25);
    }
}

// ======================================================
// LOGIC CHỐNG TRÙNG + GỌI VỀ BLAZOR
// ======================================================
function processCode(inst, raw) {
    const value = (raw || "").trim();
    if (!value) return;

    const now = Date.now();

    if (value !== inst.lastScanValue) {
        inst.lastScanValue = value;
        inst.seenCount = 1;
        inst.lastScanTime = now;
    } else {
        inst.seenCount++;
    }

    // Cần thấy ít nhất 2 frame liên tiếp để tránh nhiễu
    if (inst.seenCount < 2) return;

    // Reset để không spam
    inst.seenCount = -10;

    log("jsQR HIT:", inst.station, "=>", value);
    playBeep();

    if (inst.dotRef) {
        inst.dotRef.invokeMethodAsync("ProcessScan", inst.station, value)
            .catch(e => console.error("[SCAN] Blazor invoke error:", e));
    }
}

// Beep nhỏ khi nhận QR
function playBeep() {
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.frequency.value = 1000;
        gain.gain.value = 0.12;
        osc.start();
        setTimeout(() => {
            osc.stop();
            ctx.close();
        }, 80);
    } catch { }
}
