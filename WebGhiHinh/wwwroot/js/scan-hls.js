// ==========================================
// FILE: wwwroot/js/scan-hls.js (v3.0 ULTRA)
// Tối ưu hiển thị RTSP→HLS + Overlay + jsQR
// - Cực mượt (ưu tiên video render)
// - Nhận diện mạnh trong thiếu sáng / ngược sáng / nhiễu / QR nhỏ
// - Dedupe thông minh: emit 1 lần/1 "presence"
// - 2-pass enhance + ROI center pass
// - Tự phục hồi khi mạng yếu
// ==========================================

// ====== HLS STATE ======
let hlsPlayer = null;
let hlsRunning = false;
let scanningEnabled = true;

// ====== SCAN CONFIG ======
const SCAN_INTERVAL_MS = 180;     // ~5.5 fps
const DEBUG_TEXT_MAX = 60;

// ====== DEDUPE / PRESENCE CONFIG ======
const STABLE_FRAMES = 2;          // số frame liên tiếp thấy cùng code
const STABLE_MIN_MS = 140;        // hoặc tồn tại >= ms
const LOST_RESET_MS = 900;        // mất > ms thì reset presence

// ====== ENHANCED DETECTION CONFIG ======
const ENABLE_ENHANCED_PASS = true;
const ENABLE_ROI_PASS = true;     // quét trung tâm khi full frame fail
const ENHANCE_GAMMA = 0.90;       // <1.0 sáng vùng tối
const SCAN_MAX_WIDTH = 960;       // giới hạn width để nhẹ CPU
const ROI_SCALE = 0.62;           // ROI trung tâm theo tỉ lệ frame (0.5~0.75)

// ====== THROTTLE FRAME ======
let lastScanFrame = 0;

// ====== PRESENCE STATE ======
let activeCode = "";
let activeFirstSeen = 0;
let activeLastSeen = 0;
let activeStableCount = 0;
let activeEmitted = false;

// ====== PROCESSING CANVAS (OFFSCREEN ưu tiên) ======
let _procCanvas = null;
let _procCtx = null;
let _roiCanvas = null;
let _roiCtx = null;

function makeProcCanvas() {
    if (!_procCanvas) {
        if (typeof OffscreenCanvas !== "undefined") {
            _procCanvas = new OffscreenCanvas(1, 1);
            _procCtx = _procCanvas.getContext("2d", { willReadFrequently: true });
        } else {
            _procCanvas = document.createElement("canvas");
            _procCtx = _procCanvas.getContext("2d", { willReadFrequently: true });
        }
    }
}

function makeRoiCanvas() {
    if (!_roiCanvas) {
        if (typeof OffscreenCanvas !== "undefined") {
            _roiCanvas = new OffscreenCanvas(1, 1);
            _roiCtx = _roiCanvas.getContext("2d", { willReadFrequently: true });
        } else {
            _roiCanvas = document.createElement("canvas");
            _roiCtx = _roiCanvas.getContext("2d", { willReadFrequently: true });
        }
    }
}

function ensureCanvasSize(c, w, h) {
    if (!c) return;
    if (c.width !== w) c.width = w;
    if (c.height !== h) c.height = h;
}

// ====== DRAW QR BOX ======
function drawQrBox(ctx, loc, color) {
    if (!loc) return;
    ctx.save();
    ctx.lineWidth = 5;
    ctx.strokeStyle = color;
    ctx.lineJoin = "round";
    ctx.beginPath();
    ctx.moveTo(loc.topLeftCorner.x, loc.topLeftCorner.y);
    ctx.lineTo(loc.topRightCorner.x, loc.topRightCorner.y);
    ctx.lineTo(loc.bottomRightCorner.x, loc.bottomRightCorner.y);
    ctx.lineTo(loc.bottomLeftCorner.x, loc.bottomLeftCorner.y);
    ctx.closePath();
    ctx.stroke();
    ctx.restore();
}

// ====== ROUND RECT helper ======
function roundRect(ctx, x, y, width, height, radius) {
    ctx.beginPath();
    ctx.moveTo(x + radius, y);
    ctx.lineTo(x + width - radius, y);
    ctx.quadraticCurveTo(x + width, y, x + width, y + radius);
    ctx.lineTo(x + width, y + height - radius);
    ctx.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
    ctx.lineTo(x + radius, y + height);
    ctx.quadraticCurveTo(x, y + height, x, y + height - radius);
    ctx.lineTo(x, y + radius);
    ctx.quadraticCurveTo(x, y, x + radius, y);
    ctx.closePath();
}

// ====== DEBUG LABEL (TO RÕ) ======
function drawDebugLabel(ctx, loc, text) {
    try {
        if (!loc) return;

        const x = Math.max(0, loc.topLeftCorner.x);
        const y = Math.max(42, loc.topLeftCorner.y - 10);
        const t = (text || "").slice(0, DEBUG_TEXT_MAX);

        ctx.save();
        ctx.font = "bold 24px 'Segoe UI', Roboto, Helvetica, Arial, sans-serif";
        ctx.textBaseline = "bottom";

        const metrics = ctx.measureText(t);
        const textWidth = metrics.width;
        const textHeight = 30;
        const padding = 12;

        ctx.fillStyle = "rgba(0, 0, 0, 0.80)";
        roundRect(ctx, x, y - textHeight - padding, textWidth + padding * 2, textHeight + padding, 6);
        ctx.fill();

        ctx.fillStyle = "#00FF00";
        ctx.shadowColor = "black";
        ctx.shadowBlur = 4;
        ctx.fillText(t, x + padding, y - (padding / 2));

        ctx.restore();
    } catch { }
}

// ====== PRESENCE RESET ======
function resetPresence() {
    activeCode = "";
    activeFirstSeen = 0;
    activeLastSeen = 0;
    activeStableCount = 0;
    activeEmitted = false;
}

// ====== EMIT LOGIC ======
function handleQrDetected(text, nowMs, dotnetRef) {
    const data = (text || "").trim();
    if (!data) return;

    if (data !== activeCode) {
        activeCode = data;
        activeFirstSeen = nowMs;
        activeLastSeen = nowMs;
        activeStableCount = 1;
        activeEmitted = false;
        return;
    }

    activeLastSeen = nowMs;
    activeStableCount++;

    if (activeEmitted) return;

    const aliveMs = nowMs - activeFirstSeen;

    if (activeStableCount >= STABLE_FRAMES || aliveMs >= STABLE_MIN_MS) {
        activeEmitted = true;
        if (dotnetRef) {
            dotnetRef.invokeMethodAsync("ProcessScan", activeCode).catch(() => { });
        }
    }
}

function handleQrLost(nowMs) {
    if (!activeCode) return;
    if ((nowMs - activeLastSeen) > LOST_RESET_MS) {
        resetPresence();
    }
}

// ====== IMAGE ENHANCE (auto-contrast + gamma) ======
function enhanceImageData(img) {
    const d = img.data;
    const len = d.length;

    let min = 255, max = 0;

    for (let i = 0; i < len; i += 4) {
        const r = d[i], g = d[i + 1], b = d[i + 2];
        const y = (0.299 * r + 0.587 * g + 0.114 * b) | 0;
        if (y < min) min = y;
        if (y > max) max = y;
    }

    const range = Math.max(1, max - min);
    const invGamma = 1 / ENHANCE_GAMMA;

    for (let i = 0; i < len; i += 4) {
        const r = d[i], g = d[i + 1], b = d[i + 2];
        let y = (0.299 * r + 0.587 * g + 0.114 * b);
        y = (y - min) * 255 / range;
        y = Math.pow(y / 255, invGamma) * 255;
        const v = y | 0;
        d[i] = d[i + 1] = d[i + 2] = v;
    }

    return img;
}

// ====== LOCATION SCALE ======
function scaleLocation(loc, scaleX, scaleY) {
    if (!loc) return loc;
    const s = (p) => ({ x: p.x * scaleX, y: p.y * scaleY });
    return {
        topLeftCorner: s(loc.topLeftCorner),
        topRightCorner: s(loc.topRightCorner),
        bottomRightCorner: s(loc.bottomRightCorner),
        bottomLeftCorner: s(loc.bottomLeftCorner)
    };
}

function scaleLocationWithOffset(loc, scaleX, scaleY, offX, offY) {
    if (!loc) return loc;
    const s = (p) => ({ x: (p.x + offX) * scaleX, y: (p.y + offY) * scaleY });
    return {
        topLeftCorner: s(loc.topLeftCorner),
        topRightCorner: s(loc.topRightCorner),
        bottomRightCorner: s(loc.bottomRightCorner),
        bottomLeftCorner: s(loc.bottomLeftCorner)
    };
}

// ====== STOP / CLEANUP ======
window.stopHlsScan = () => {
    hlsRunning = false;

    try {
        if (hlsPlayer) {
            hlsPlayer.destroy();
            hlsPlayer = null;
        }
    } catch { }

    resetPresence();
};

// ====== START HLS + SCAN ======
window.startHlsScan = async (hlsUrl, videoEl, canvasEl, dotnetRef, enableScan) => {

    scanningEnabled = !!enableScan;

    // Nếu đang phát đúng URL, chỉ update scan flag
    if (hlsPlayer && videoEl?.src?.includes(hlsUrl) && hlsRunning) {
        if (!scanningEnabled && canvasEl) {
            const ctx = canvasEl.getContext("2d");
            ctx.clearRect(0, 0, canvasEl.width, canvasEl.height);
            resetPresence();
        }
        return;
    }

    window.stopHlsScan();
    if (!hlsUrl || !videoEl || !canvasEl) return;

    hlsRunning = true;

    // Video style
    videoEl.muted = true;
    videoEl.playsInline = true;
    videoEl.autoplay = true;
    videoEl.style.objectFit = "contain";
    videoEl.style.width = "100%";
    videoEl.style.height = "100%";
    videoEl.style.background = "#000";

    try {
        if (window.Hls && Hls.isSupported()) {
            hlsPlayer = new Hls({
                lowLatencyMode: true,
                enableWorker: true,
                backBufferLength: 10,
                maxBufferLength: 6,
                liveSyncDuration: 1,
                liveMaxLatencyDuration: 2,
                maxLiveSyncPlaybackRate: 1.0
            });

            hlsPlayer.loadSource(hlsUrl);
            hlsPlayer.attachMedia(videoEl);

            hlsPlayer.on(Hls.Events.MANIFEST_PARSED, () => {
                videoEl.play().catch(() => { });
            });

            hlsPlayer.on(Hls.Events.ERROR, (event, data) => {
                if (!data?.fatal) return;

                if (data.type === Hls.ErrorTypes.NETWORK_ERROR) {
                    try { hlsPlayer.startLoad(); } catch { }
                }
                else if (data.type === Hls.ErrorTypes.MEDIA_ERROR) {
                    try { hlsPlayer.recoverMediaError(); } catch { }
                }
                else {
                    try { hlsPlayer.destroy(); } catch { }
                    hlsPlayer = null;
                }
            });
        }
        else if (videoEl.canPlayType("application/vnd.apple.mpegurl")) {
            videoEl.src = hlsUrl;
            videoEl.play().catch(() => { });
        }
        else {
            console.warn("Hls.js chưa được load hoặc browser không hỗ trợ.");
        }
    } catch (err) {
        console.error("❌ Lỗi khởi tạo HLS:", err);
        return;
    }

    requestAnimationFrame(() => scanLoop(videoEl, canvasEl, dotnetRef));
};

// ====== CORE DETECTION (FULL FRAME) ======
function detectOnFullFrame(videoEl, overlayW, overlayH) {
    const vw = videoEl.videoWidth;
    const vh = videoEl.videoHeight;
    if (!vw || !vh) return null;

    makeProcCanvas();

    const scanW = Math.min(vw, SCAN_MAX_WIDTH);
    const scanH = Math.max(1, Math.round(vh * (scanW / vw)));

    ensureCanvasSize(_procCanvas, scanW, scanH);

    _procCtx.drawImage(videoEl, 0, 0, scanW, scanH);
    let frame = _procCtx.getImageData(0, 0, scanW, scanH);

    // Pass 1 raw
    let qr = null;
    try {
        qr = jsQR(frame.data, frame.width, frame.height, { inversionAttempts: "attemptBoth" });
    } catch { qr = null; }

    // Pass 2 enhanced
    if (!qr && ENABLE_ENHANCED_PASS) {
        try {
            frame = enhanceImageData(frame);
            qr = jsQR(frame.data, frame.width, frame.height, { inversionAttempts: "invertFirst" });
        } catch { qr = null; }
    }

    if (!qr || !qr.data) return null;

    const scaleX = overlayW / scanW;
    const scaleY = overlayH / scanH;

    return {
        data: qr.data,
        location: scaleLocation(qr.location, scaleX, scaleY)
    };
}

// ====== ROI DETECTION (CENTER CROP) ======
function detectOnRoiCenter(videoEl, overlayW, overlayH) {
    const vw = videoEl.videoWidth;
    const vh = videoEl.videoHeight;
    if (!vw || !vh) return null;

    makeRoiCanvas();

    const roiW_src = Math.max(1, Math.round(vw * ROI_SCALE));
    const roiH_src = Math.max(1, Math.round(vh * ROI_SCALE));
    const roiX_src = Math.max(0, Math.round((vw - roiW_src) / 2));
    const roiY_src = Math.max(0, Math.round((vh - roiH_src) / 2));

    // Giữ ROI quét ở kích thước tương đối lớn để bắt QR nhỏ
    const roiW_scan = Math.min(roiW_src, SCAN_MAX_WIDTH);
    const roiH_scan = Math.max(1, Math.round(roiH_src * (roiW_scan / roiW_src)));

    ensureCanvasSize(_roiCanvas, roiW_scan, roiH_scan);

    // Draw crop -> roi canvas
    _roiCtx.drawImage(
        videoEl,
        roiX_src, roiY_src, roiW_src, roiH_src,
        0, 0, roiW_scan, roiH_scan
    );

    let frame = _roiCtx.getImageData(0, 0, roiW_scan, roiH_scan);

    let qr = null;
    try {
        qr = jsQR(frame.data, frame.width, frame.height, { inversionAttempts: "attemptBoth" });
    } catch { qr = null; }

    if (!qr && ENABLE_ENHANCED_PASS) {
        try {
            frame = enhanceImageData(frame);
            qr = jsQR(frame.data, frame.width, frame.height, { inversionAttempts: "invertFirst" });
        } catch { qr = null; }
    }

    if (!qr || !qr.data) return null;

    // Map ROI location -> overlay
    // Step:
    // 1) location trong roi-scan space
    // 2) scale về roi-src space
    // 3) cộng offset roiX_src/roiY_src
    // 4) scale về overlay
    const roiScaleBackX = roiW_src / roiW_scan;
    const roiScaleBackY = roiH_src / roiH_scan;

    const toSrc = (p) => ({ x: p.x * roiScaleBackX, y: p.y * roiScaleBackY });

    const locSrc = {
        topLeftCorner: toSrc(qr.location.topLeftCorner),
        topRightCorner: toSrc(qr.location.topRightCorner),
        bottomRightCorner: toSrc(qr.location.bottomRightCorner),
        bottomLeftCorner: toSrc(qr.location.bottomLeftCorner)
    };

    // add offset then scale to overlay
    const overlayScaleX = overlayW / vw;
    const overlayScaleY = overlayH / vh;

    const s = (p) => ({
        x: (p.x + roiX_src) * overlayScaleX,
        y: (p.y + roiY_src) * overlayScaleY
    });

    const locOverlay = {
        topLeftCorner: s(locSrc.topLeftCorner),
        topRightCorner: s(locSrc.topRightCorner),
        bottomRightCorner: s(locSrc.bottomRightCorner),
        bottomLeftCorner: s(locSrc.bottomLeftCorner)
    };

    return {
        data: qr.data,
        location: locOverlay
    };
}

// ====== MAIN LOOP ======
function scanLoop(videoEl, canvasEl, dotnetRef) {
    if (!hlsRunning) return;

    try {
        if (videoEl.readyState >= 2) {
            const ctx = canvasEl.getContext("2d", { willReadFrequently: true });

            // Sync overlay canvas size with real video
            if (videoEl.videoWidth && videoEl.videoHeight) {
                if (canvasEl.width !== videoEl.videoWidth) canvasEl.width = videoEl.videoWidth;
                if (canvasEl.height !== videoEl.videoHeight) canvasEl.height = videoEl.videoHeight;
            }

            // Clear overlay each frame
            ctx.clearRect(0, 0, canvasEl.width, canvasEl.height);

            const nowPerf = performance.now();
            const nowAbs = Date.now();

            if (scanningEnabled && (nowPerf - lastScanFrame > SCAN_INTERVAL_MS)) {
                lastScanFrame = nowPerf;

                if (window.jsQR && videoEl.videoWidth && videoEl.videoHeight) {
                    // 1) Full-frame detection
                    let result = detectOnFullFrame(videoEl, canvasEl.width, canvasEl.height);

                    // 2) ROI center pass nếu cần
                    if (!result && ENABLE_ROI_PASS) {
                        result = detectOnRoiCenter(videoEl, canvasEl.width, canvasEl.height);
                    }

                    if (result && result.data) {
                        drawQrBox(ctx, result.location, "#00FF00");
                        drawDebugLabel(ctx, result.location, result.data);
                        handleQrDetected(result.data, nowAbs, dotnetRef);
                    } else {
                        handleQrLost(nowAbs);
                    }
                }
            } else {
                if (!scanningEnabled) resetPresence();
            }
        }
    } catch (err) {
        console.error("Loop error:", err);
    }

    requestAnimationFrame(() => scanLoop(videoEl, canvasEl, dotnetRef));
}

// ====== BEEP ======
window.playBeep = () => {
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.type = "sine";
        osc.frequency.value = 900;
        gain.gain.value = 0.05;
        osc.start();
        setTimeout(() => { osc.stop(); ctx.close(); }, 80);
    } catch { }
};

// ====== DEBUG STATE ======
window.getHlsScanState = () => ({
    hlsRunning,
    scanningEnabled,
    activeCode,
    activeStableCount,
    activeEmitted,
    activeFirstSeen,
    activeLastSeen,
    SCAN_INTERVAL_MS,
    STABLE_FRAMES,
    STABLE_MIN_MS,
    LOST_RESET_MS,
    ENABLE_ENHANCED_PASS,
    ENABLE_ROI_PASS,
    ENHANCE_GAMMA,
    SCAN_MAX_WIDTH,
    ROI_SCALE
});
