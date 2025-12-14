// ===============================
// Stable HLS + jsQR Scanner (PATCH for your Blazor)
// - Supports enableScan (BarcodeGun vs AutoCam)
// - Adds stopHlsScan API
// - Keeps overlay canvas transparent (clear after capture)
// - Avoids resizing canvas every frame
// ===============================

let hls = null;
let rafId = null;
let scanning = false;
let scanningEnabled = true;

let lastCode = "";
let lastHitAt = 0;

function now() { return Date.now(); }

function resetState() {
    lastCode = "";
    lastHitAt = 0;
}

function hardStop(video) {
    try { if (rafId) cancelAnimationFrame(rafId); } catch { }
    rafId = null;
    scanning = false;

    try {
        if (hls) {
            hls.destroy();
            hls = null;
        }
    } catch { }

    try {
        if (video) {
            video.pause();
            video.removeAttribute("src");
            video.load();
        }
    } catch { }

    resetState();
}

function attachHls(url, video) {
    // Native HLS (Safari)
    if (video.canPlayType("application/vnd.apple.mpegurl")) {
        video.src = url;
        video.play().catch(() => { });
        return;
    }

    if (typeof Hls === "undefined") {
        console.error("Hls.js not loaded!");
        return;
    }

    hls = new Hls({
        lowLatencyMode: true,
        enableWorker: true,
        backBufferLength: 10,
        maxBufferLength: 6,
        liveSyncDuration: 1,
        liveMaxLatencyDuration: 2,
        maxLiveSyncPlaybackRate: 1.0
    });

    hls.loadSource(url);
    hls.attachMedia(video);

    hls.on(Hls.Events.MANIFEST_PARSED, () => {
        video.play().catch(() => { });
    });

    hls.on(Hls.Events.ERROR, (event, data) => {
        if (!data || !data.fatal) return;

        switch (data.type) {
            case Hls.ErrorTypes.NETWORK_ERROR:
                try { hls.startLoad(); } catch { }
                break;
            case Hls.ErrorTypes.MEDIA_ERROR:
                try { hls.recoverMediaError(); } catch { }
                break;
            default:
                try {
                    hls.destroy();
                    hls = null;
                    attachHls(url, video);
                } catch { }
                break;
        }
    });
}

function ensureCanvasSize(canvas, w, h) {
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
}

function scanLoop(video, canvas, dotnetRef) {
    if (!scanning) return;

    const w = video.videoWidth;
    const h = video.videoHeight;

    if (!w || !h) {
        rafId = requestAnimationFrame(() => scanLoop(video, canvas, dotnetRef));
        return;
    }

    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ensureCanvasSize(canvas, w, h);

    // Nếu tắt scan (BarcodeGun) -> clear overlay & reset dedupe
    if (!scanningEnabled) {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        resetState();
        rafId = requestAnimationFrame(() => scanLoop(video, canvas, dotnetRef));
        return;
    }

    // Draw frame only for decoding
    ctx.drawImage(video, 0, 0, w, h);
    const img = ctx.getImageData(0, 0, w, h);

    // Clear immediately to keep overlay transparent
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    let code = null;
    try {
        code = jsQR(img.data, w, h, { inversionAttempts: "attemptBoth" });
    } catch { }

    if (code && code.data) {
        const value = (code.data || "").trim();
        const t = now();

        // debounce: tránh spam cùng mã liên tục
        const sameAsLast = value === lastCode;
        const tooSoon = (t - lastHitAt) < 900;

        if (!(sameAsLast && tooSoon)) {
            lastCode = value;
            lastHitAt = t;

            try {
                dotnetRef?.invokeMethodAsync("ProcessScan", value);
            } catch (e) {
                console.error("Invoke ProcessScan failed:", e);
            }
        }
    }

    rafId = requestAnimationFrame(() => scanLoop(video, canvas, dotnetRef));
}

// ===============================
// Public API used by Blazor
// Signature compatible with:
// startHlsScan(hlsUrl, videoEl, canvasEl, dotnetRef, enableScan)
// ===============================
window.startHlsScan = (hlsUrl, videoElement, canvasElement, dotnetRef, enableScan) => {
    try {
        if (!videoElement || !canvasElement) return;

        // update scan flag
        scanningEnabled = !!enableScan;

        // stop old instance cleanly
        hardStop(videoElement);

        // attach new
        attachHls(hlsUrl, videoElement);

        // start loop
        scanning = true;
        rafId = requestAnimationFrame(() => scanLoop(videoElement, canvasElement, dotnetRef));
    } catch (e) {
        console.error("startHlsScan error:", e);
    }
};

// Your Blazor uses this name
window.stopHlsScan = () => {
    try {
        const videos = document.getElementsByTagName("video");
        const video = videos && videos.length ? videos[0] : null;
        hardStop(video);
    } catch { }
};

// Backward alias if you still call it somewhere
window.stopCamera = window.stopHlsScan;

// Optional beep
window.playBeep = () => {
    try {
        const audio = new Audio("/beep.mp3");
        audio.play().catch(() => { });
    } catch { }
};
