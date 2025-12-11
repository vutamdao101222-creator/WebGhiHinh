// ==========================================
// FILE: wwwroot/js/scan-hls.js (v5.1 ULTIMATE - PRE-PROCESSING)
// - Hỗ trợ: 10-20 Camera cùng lúc (Scheduler Round-Robin)
// - Tối ưu RAM: Shared Canvas Memory
// - NÂNG CẤP: Tự động xử lý ảnh (Grayscale + High Contrast) giúp đọc mã mờ/tối cực tốt
// ==========================================

// ====== TÀI NGUYÊN DÙNG CHUNG (GLOBAL SHARED) ======
// Canvas này dùng để xử lý ảnh ngầm, không hiển thị ra ngoài
const _sharedCanvas = new OffscreenCanvas(800, 450); // Tăng size lên chút để nét hơn
const _sharedCtx = _sharedCanvas.getContext("2d", { willReadFrequently: true });

// ====== CẤU HÌNH ======
const SCAN_INTERVAL_MS = 150;     // Tốc độ quét mỗi cam (ms)
const QUEUE_DELAY_MS = 10;        // Độ trễ giữa các lần chuyển cam
const DEBUG_TEXT_MAX = 60;

// ====== DEBUG CONFIG (Hiển thị nhãn trên khung hình) ======
const DEBUG_FONT_RATIO = 0.045;
const DEBUG_MIN_FONT = 28;
const DEBUG_MAX_FONT = 64;
const DEBUG_BG_ALPHA = 0.85;
const USE_FIXED_CORNER_LABEL = false; // False = nhãn chạy theo mã QR

// ====== KHỞI TẠO ENGINE ======
const nativeDetector = ('BarcodeDetector' in window)
    ? new BarcodeDetector({ formats: ['qr_code', 'code_128', 'ean_13', 'ean_8', 'code_39', 'upc_a'] })
    : null;

let zxingReader = null;
try {
    if (window.ZXing) zxingReader = new ZXing.BrowserMultiFormatReader();
} catch (e) { }

// ====== BỘ ĐIỀU PHỐI (SCHEDULER) ======
const scanQueue = [];       // Hàng đợi các camera đang chờ quét
let isProcessing = false;   // Cờ khóa (Lock)

// Vòng lặp xử lý hàng đợi (Core Loop)
async function processQueue() {
    if (scanQueue.length === 0) {
        isProcessing = false;
        return;
    }

    isProcessing = true;

    // Lấy camera đầu tiên ra xử lý
    const instance = scanQueue.shift();

    try {
        if (instance && instance.enabled && instance.videoEl && !instance.videoEl.paused) {
            await detectFrame(instance);
        }
    } catch (e) {
        console.warn("Scan error:", e);
    }

    // Nghỉ 1 chút rồi xử lý tiếp
    setTimeout(processQueue, QUEUE_DELAY_MS);
}

// Đăng ký vào hàng đợi
function enqueue(instance) {
    if (!scanQueue.includes(instance)) {
        scanQueue.push(instance);
        if (!isProcessing) processQueue();
    }
}

// ====== QUẢN LÝ INSTANCE ======
const instances = new Map();

class CameraInstance {
    constructor(videoEl, canvasEl, dotnetRef, enableScan) {
        this.videoEl = videoEl;
        this.canvasEl = canvasEl;
        this.ctx = canvasEl ? canvasEl.getContext("2d", { willReadFrequently: true }) : null;
        this.dotnetRef = dotnetRef;
        this.enabled = enableScan;
        this.hls = null;

        this.lastRegisterTime = 0;
        this.activeCode = "";
        this.stableCount = 0;
        this.lastSeen = 0;
        this.isEmitted = false;
    }

    destroy() {
        if (this.hls) {
            try { this.hls.destroy(); } catch { }
        }
        this.enabled = false;
    }
}

// ====== PUBLIC API ======

window.startHlsScan = (hlsUrl, videoEl, canvasEl, dotnetRef, enableScan) => {
    if (!videoEl) return;

    let instance = instances.get(videoEl);

    if (instance) {
        if (instance.hls && videoEl.src && !videoEl.src.includes(encodeURI(hlsUrl))) {
            window.stopHlsScan(videoEl);
            instance = null;
        } else {
            instance.enabled = enableScan;
            instance.dotnetRef = dotnetRef;
            instance.canvasEl = canvasEl;
            instance.ctx = canvasEl ? canvasEl.getContext("2d") : null;
            return;
        }
    }

    instance = new CameraInstance(videoEl, canvasEl, dotnetRef, enableScan);
    instances.set(videoEl, instance);

    videoEl.muted = true;
    videoEl.playsInline = true;
    videoEl.autoplay = true;
    videoEl.style.objectFit = "contain";
    videoEl.preload = "none";

    try {
        if (window.Hls && Hls.isSupported()) {
            const hls = new Hls({
                enableWorker: true,
                lowLatencyMode: true,
                backBufferLength: 0,
                maxBufferLength: 6,
                liveSyncDuration: 1
            });
            hls.loadSource(hlsUrl);
            hls.attachMedia(videoEl);
            hls.on(Hls.Events.MANIFEST_PARSED, () => videoEl.play().catch(() => { }));
            hls.on(Hls.Events.ERROR, (event, data) => {
                if (!data.fatal) return;
                if (data.type === Hls.ErrorTypes.NETWORK_ERROR) hls.startLoad();
                else hls.recoverMediaError();
            });
            instance.hls = hls;
        } else if (videoEl.canPlayType('application/vnd.apple.mpegurl')) {
            videoEl.src = hlsUrl;
            videoEl.play().catch(() => { });
        }
    } catch (e) { console.error("HLS Init Error:", e); }

    requestAnimationFrame(() => instanceLoop(instance));
};

window.stopHlsScan = (videoEl) => {
    if (!videoEl) {
        instances.forEach(i => i.destroy());
        instances.clear();
        scanQueue.length = 0;
        return;
    }
    const instance = instances.get(videoEl);
    if (instance) {
        instance.destroy();
        instances.delete(videoEl);
    }
};

// ====== VÒNG LẶP RIÊNG CỦA TỪNG CAM ======
function instanceLoop(instance) {
    if (!instances.has(instance.videoEl)) return;

    const now = performance.now();

    // Xóa overlay mỗi frame để video bên dưới hiển thị rõ
    if (instance.ctx && instance.canvasEl) {
        if (instance.canvasEl.width !== instance.videoEl.videoWidth) {
            instance.canvasEl.width = instance.videoEl.videoWidth;
            instance.canvasEl.height = instance.videoEl.videoHeight;
        }
        instance.ctx.clearRect(0, 0, instance.canvasEl.width, instance.canvasEl.height);
    }

    if (instance.enabled && (now - instance.lastRegisterTime > SCAN_INTERVAL_MS)) {
        instance.lastRegisterTime = now;
        enqueue(instance);
    }

    requestAnimationFrame(() => instanceLoop(instance));
}

// ====== XỬ LÝ ẢNH THÔNG MINH (SMART DETECTION) ======
async function detectFrame(instance) {
    const video = instance.videoEl;
    if (!video.videoWidth) return;

    // 1. Chuẩn bị kích thước (Resize về 800px để cân bằng tốc độ/độ nét)
    const w = Math.min(video.videoWidth, 800);
    const h = Math.round(video.videoHeight * (w / video.videoWidth));

    if (_sharedCanvas.width !== w) _sharedCanvas.width = w;
    if (_sharedCanvas.height !== h) _sharedCanvas.height = h;

    // --- CHIẾN THUẬT 2 LỚP (2-PASS STRATEGY) ---

    // Pass 1: Quét ảnh gốc (Nhanh nhất, cho trường hợp mã rõ nét)
    _sharedCtx.filter = "none";
    _sharedCtx.drawImage(video, 0, 0, w, h);
    let result = await tryDetect(_sharedCanvas);

    // Pass 2: Nếu Pass 1 thất bại -> Bật bộ lọc "Xuyên màn đêm"
    if (!result) {
        // grayscale(1): Chuyển đen trắng để loại bỏ nhiễu màu
        // contrast(2.0): Tăng tương phản gấp đôi (đen thành đen kịt, trắng thành trắng toát)
        // brightness(1.1): Tăng sáng nhẹ nếu cam trong góc tối
        _sharedCtx.filter = "grayscale(1) contrast(2.0) brightness(1.1)";
        _sharedCtx.drawImage(video, 0, 0, w, h);
        result = await tryDetect(_sharedCanvas);
    }

    // Xử lý kết quả
    if (result) {
        // Vẽ lại khung lên màn hình (nếu có overlay)
        if (instance.ctx && instance.canvasEl) {
            drawBox(instance.ctx, result.loc, result.type, video.videoWidth, video.videoHeight, w, h);
            if (!USE_FIXED_CORNER_LABEL) {
                drawDebugLabel(instance.ctx, result.loc, result.type, result.data, video.videoWidth, video.videoHeight, w, h);
            }
        }
        handleScanResult(instance, result.data);
    } else {
        handleScanLost(instance);
    }
}

// Hàm phụ trợ: Thử quét bằng cả Native và ZXing
async function tryDetect(canvasSource) {
    // 1. Ưu tiên Native (GPU - Chrome/Android)
    if (nativeDetector) {
        try {
            const barcodes = await nativeDetector.detect(canvasSource);
            if (barcodes.length > 0) {
                return { data: barcodes[0].rawValue, type: 'native', loc: barcodes[0] };
            }
        } catch { }
    }
    // 2. Dự phòng ZXing (CPU - Các trình duyệt khác)
    if (zxingReader) {
        try {
            const zxRes = zxingReader.decodeFromCanvas(canvasSource);
            if (zxRes) {
                return { data: zxRes.getText(), type: 'zxing', loc: null };
            }
        } catch { }
    }
    return null;
}

// ====== LOGIC DEDUPE & EMIT ======
function handleScanResult(instance, code) {
    const now = Date.now();

    if (code !== instance.activeCode) {
        instance.activeCode = code;
        instance.stableCount = 1;
        instance.lastSeen = now;
        instance.isEmitted = false;
    } else {
        instance.stableCount++;
        instance.lastSeen = now;
    }

    // Logic: Thấy 2 lần liên tiếp hoặc tồn tại > 200ms thì chốt đơn
    if (!instance.isEmitted && (instance.stableCount >= 2 || (now - instance.lastSeen < 200))) {
        instance.isEmitted = true;

        if (instance.dotnetRef) {
            instance.dotnetRef.invokeMethodAsync("ProcessScan", code).catch(() => { });
        }

        playBeep();
    }
}

function handleScanLost(instance) {
    // Reset nếu mất dấu quá 1s
    if (instance.activeCode && (Date.now() - instance.lastSeen > 1000)) {
        instance.activeCode = "";
        instance.stableCount = 0;
        instance.isEmitted = false;
    }
}

// ====== UTILS VẼ KHUNG ======
function drawBox(ctx, loc, type, realW, realH, scanW, scanH) {
    if (type !== 'native' || !loc || !loc.cornerPoints) return;

    const scaleX = realW / scanW;
    const scaleY = realH / scanH;
    const p = loc.cornerPoints;

    ctx.beginPath();
    ctx.lineWidth = 4;
    ctx.strokeStyle = "#00FF00"; // Xanh lá cây

    ctx.moveTo(p[0].x * scaleX, p[0].y * scaleY);
    ctx.lineTo(p[1].x * scaleX, p[1].y * scaleY);
    ctx.lineTo(p[2].x * scaleX, p[2].y * scaleY);
    ctx.lineTo(p[3].x * scaleX, p[3].y * scaleY);
    ctx.closePath();
    ctx.stroke();
}

function roundRect(ctx, x, y, width, height, radius) {
    const r = Math.max(0, Math.min(radius, Math.min(width, height) / 2));
    ctx.beginPath();
    ctx.moveTo(x + r, y);
    ctx.lineTo(x + width - r, y);
    ctx.quadraticCurveTo(x + width, y, x + width, y + r);
    ctx.lineTo(x + width, y + height - r);
    ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
    ctx.lineTo(x + r, y + height);
    ctx.quadraticCurveTo(x, y + height, x, y + height - r);
    ctx.lineTo(x, y + r);
    ctx.quadraticCurveTo(x, y, x + r, y);
    ctx.closePath();
}

function drawDebugLabel(ctx, loc, type, text, realW, realH, scanW, scanH) {
    if (type !== 'native' || !loc || !loc.cornerPoints) return;

    const t = (text || "").slice(0, DEBUG_TEXT_MAX);
    if (!t) return;

    const scaleX = realW / scanW;
    const scaleY = realH / scanH;
    const p = loc.cornerPoints;

    const canvasW = ctx.canvas.width;
    let fontSize = Math.round(canvasW * DEBUG_FONT_RATIO);
    fontSize = Math.max(DEBUG_MIN_FONT, Math.min(DEBUG_MAX_FONT, fontSize));

    const textHeight = Math.round(fontSize * 1.15);
    const padding = Math.round(fontSize * 0.35);

    const x = Math.max(0, p[0].x * scaleX);
    const y = Math.max(textHeight + padding + 10, (p[0].y * scaleY) - 10);

    ctx.save();
    ctx.font = `bold ${fontSize}px 'Segoe UI', Roboto, Helvetica, Arial, sans-serif`;
    ctx.textBaseline = "bottom";

    const metrics = ctx.measureText(t);
    const textWidth = metrics.width;

    ctx.fillStyle = `rgba(0, 0, 0, ${DEBUG_BG_ALPHA})`;
    roundRect(ctx, x, y - textHeight - padding, textWidth + padding * 2, textHeight + padding, 10);
    ctx.fill();

    ctx.fillStyle = "#00FF00";
    ctx.shadowColor = "black";
    ctx.shadowBlur = 8;
    ctx.fillText(t, x + padding, y - (padding / 2));

    ctx.restore();
}

window.playBeep = () => {
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.frequency.value = 1200;
        gain.gain.value = 0.05;
        osc.start();
        setTimeout(() => { osc.stop(); ctx.close(); }, 50);
    } catch { }
};

window.getHlsScanState = () => ({
    queueSize: scanQueue.length,
    activeInstances: instances.size,
    isProcessing,
    nativeSupport: !!nativeDetector
});

window.startHlsScanByElementId = (hlsUrl, vidId, canId, dotRef, enable) => {
    const v = document.getElementById(vidId);
    const c = document.getElementById(canId);
    if (v) window.startHlsScan(hlsUrl, v, c, dotRef, enable);
};
