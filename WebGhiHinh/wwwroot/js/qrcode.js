// ===============================
// Stable HLS + jsQR Scanner
// ===============================
let hls = null;
let rafId = null;
let scanning = false;
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
        backBufferLength: 30,
        maxLiveSyncPlaybackRate: 1.0
    });

    hls.loadSource(url);
    hls.attachMedia(video);

    hls.on(Hls.Events.MANIFEST_PARSED, () => {
        video.play().catch(() => { });
    });

    hls.on(Hls.Events.ERROR, (event, data) => {
        // Auto recovery
        if (!data || !data.fatal) return;

        console.warn("HLS fatal error:", data.type);

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

function scanLoop(video, canvas, dotnetRef) {
    if (!scanning) return;

    const w = video.videoWidth;
    const h = video.videoHeight;

    if (!w || !h) {
        rafId = requestAnimationFrame(() => scanLoop(video, canvas, dotnetRef));
        return;
    }

    // Set canvas real size
    canvas.width = w;
    canvas.height = h;

    const ctx = canvas.getContext("2d", { willReadFrequently: true });
    ctx.drawImage(video, 0, 0, w, h);

    const img = ctx.getImageData(0, 0, w, h);
    const code = jsQR(img.data, w, h, { inversionAttempts: "attemptBoth" });

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
                dotnetRef.invokeMethodAsync("ProcessScan", value);
            } catch (e) {
                console.error("Invoke ProcessScan failed:", e);
            }
        }
    }

    rafId = requestAnimationFrame(() => scanLoop(video, canvas, dotnetRef));
}

// ===============================
// Public API used by Blazor
// ===============================
window.startHlsScan = (hlsUrl, videoElement, canvasElement, dotnetRef) => {
    try {
        if (!videoElement || !canvasElement) return;

        // stop old instance cleanly
        hardStop(videoElement);
        resetState();

        // attach new
        attachHls(hlsUrl, videoElement);

        // start scanning
        scanning = true;
        rafId = requestAnimationFrame(() => scanLoop(videoElement, canvasElement, dotnetRef));
    } catch (e) {
        console.error("startHlsScan error:", e);
    }
};

window.stopCamera = () => {
    try {
        const videos = document.getElementsByTagName("video");
        const video = videos && videos.length ? videos[0] : null;
        hardStop(video);
    } catch { }
};

// Optional beep (nếu bạn cần)
window.playBeep = () => {
    try {
        const audio = new Audio("/beep.mp3");
        audio.play().catch(() => { });
    } catch { }
};
