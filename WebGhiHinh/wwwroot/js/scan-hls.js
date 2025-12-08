let _hls = null;
let _running = false;
let _lastCode = "";
let _lastTs = 0;

function playBeep() {
    try {
        const ctx = new (window.AudioContext || window.webkitAudioContext)();
        const o = ctx.createOscillator();
        const g = ctx.createGain();
        o.connect(g);
        g.connect(ctx.destination);
        o.type = "sine";
        o.frequency.value = 880;
        g.gain.value = 0.05;
        o.start();
        setTimeout(() => { o.stop(); ctx.close(); }, 80);
    } catch { }
}

function stopCamera() {
    _running = false;
    _lastCode = "";
    _lastTs = 0;

    try {
        if (_hls) {
            _hls.destroy();
            _hls = null;
        }
    } catch { }
}

// hlsUrl, videoElement, canvasElement, dotnetRef
async function startHlsScan(hlsUrl, videoEl, canvasEl, dotnetRef) {
    stopCamera();

    if (!hlsUrl) return;
    if (!videoEl) return;

    // 1) Attach HLS
    try {
        if (videoEl.canPlayType && videoEl.canPlayType('application/vnd.apple.mpegurl')) {
            videoEl.src = hlsUrl;
            await videoEl.play().catch(() => { });
        } else if (window.Hls && Hls.isSupported()) {
            _hls = new Hls({
                lowLatencyMode: true,
                backBufferLength: 30
            });
            _hls.loadSource(hlsUrl);
            _hls.attachMedia(videoEl);
            _hls.on(Hls.Events.MANIFEST_PARSED, () => {
                videoEl.play().catch(() => { });
            });
        } else {
            console.error("HLS not supported on this browser.");
            return;
        }
    } catch (e) {
        console.error("startHlsScan attach error:", e);
        return;
    }

    // 2) QR Scan loop
    const ctx = canvasEl.getContext("2d", { willReadFrequently: true });
    _running = true;

    const loop = () => {
        if (!_running) return;

        const vw = videoEl.videoWidth;
        const vh = videoEl.videoHeight;

        if (!vw || !vh || videoEl.readyState < 2) {
            requestAnimationFrame(loop);
            return;
        }

        if (canvasEl.width !== vw) canvasEl.width = vw;
        if (canvasEl.height !== vh) canvasEl.height = vh;

        ctx.drawImage(videoEl, 0, 0, vw, vh);

        try {
            const img = ctx.getImageData(0, 0, vw, vh);
            const qr = window.jsQR
                ? window.jsQR(img.data, vw, vh, { inversionAttempts: "attemptBoth" })
                : null;

            if (qr && qr.data) {
                drawBox(ctx, qr.location);

                const now = Date.now();
                const data = String(qr.data).trim();

                // ✅ throttle duplicate
                if (data && (data !== _lastCode || now - _lastTs > 1200)) {
                    _lastCode = data;
                    _lastTs = now;

                    if (dotnetRef) {
                        dotnetRef.invokeMethodAsync("ProcessScan", data)
                            .catch(() => { });
                    }
                }
            }
        } catch { }

        requestAnimationFrame(loop);
    };

    requestAnimationFrame(loop);
}

function drawBox(ctx, loc) {
    if (!loc) return;
    ctx.save();
    ctx.lineWidth = 4;
    ctx.strokeStyle = "lime";
    ctx.beginPath();
    ctx.moveTo(loc.topLeftCorner.x, loc.topLeftCorner.y);
    ctx.lineTo(loc.topRightCorner.x, loc.topRightCorner.y);
    ctx.lineTo(loc.bottomRightCorner.x, loc.bottomRightCorner.y);
    ctx.lineTo(loc.bottomLeftCorner.x, loc.bottomLeftCorner.y);
    ctx.closePath();
    ctx.stroke();
    ctx.restore();
}

// export globals
window.startHlsScan = startHlsScan;
window.stopCamera = stopCamera;
window.playBeep = playBeep;
