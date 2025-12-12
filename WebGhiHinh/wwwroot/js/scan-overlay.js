// wwwroot/js/scan-overlay.js
(function () {
    if (!window.signalR) {
        console.warn("[scanOverlay] SignalR client is not loaded!");
        return;
    }

    console.log("[scanOverlay] loading...");

    // Sẽ được set từ Blazor
    let dotnetRef = null;

    function init(ref) {
        dotnetRef = ref;
        console.log("[scanOverlay] init from Blazor");
    }

    // Beep nhỏ báo HIT
    function beep() {
        try {
            const Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            const ctx = new Ctx();
            const osc = ctx.createOscillator();
            const gain = ctx.createGain();
            osc.type = "square";
            osc.frequency.value = 1400;
            gain.gain.value = 0.12;
            osc.connect(gain);
            gain.connect(ctx.destination);
            osc.start();
            setTimeout(() => {
                osc.stop();
                ctx.close();
            }, 80);
        } catch { }
    }

    // Kết nối hub
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/scanHub")   // ⚠ trùng với app.MapHub<ScanHub>("/scanHub");
        .withAutomaticReconnect()
        .build();

    function handlePayload(payload) {
        if (!payload) return;

        const station =
            payload.stationName ??
            payload.station ??
            payload.StationName;

        const code =
            payload.code ??
            payload.Code;

        const x = payload.x ?? payload.X ?? 0.3;
        const y = payload.y ?? payload.Y ?? 0.3;
        const w = payload.w ?? payload.W ?? 0.4;
        const h = payload.h ?? payload.H ?? 0.4;

        if (!station || !code) return;

        console.log("[scanOverlay] HIT:", station, code);
        beep();

        if (dotnetRef) {
            dotnetRef.invokeMethodAsync(
                "OnScanResultFromServer",
                station,
                code,
                x, y, w, h
            ).catch(err => console.error("[scanOverlay] invoke error:", err));
        }
    }

    // Nghe cả 2 event name cho chắc
    connection.on("ScanResult", handlePayload);
    connection.on("ScanHit", handlePayload);

    connection
        .start()
        .then(() => console.log("[scanOverlay] SignalR connected"))
        .catch(err => console.error("[scanOverlay] connect error:", err));

    // Expose cho Blazor gọi
    window.scanOverlay = {
        init: init
    };
})();
