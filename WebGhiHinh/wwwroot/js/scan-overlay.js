// wwwroot/js/scan-overlay.js
(function () {
    if (!window.signalR) {
        console.warn("[scanOverlay] SignalR client is not loaded!");
        return;
    }

    console.log("[scanOverlay] Connecting to SignalR...");

    let dotNetRef = null;

    // Blazor gọi hàm này: JS.InvokeVoidAsync("scanOverlay.init", objRef);
    function init(ref) {
        dotNetRef = ref;
        console.log("[scanOverlay] init from Blazor");
    }

    function beep() {
        try {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
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

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/scanhub")
        .withAutomaticReconnect()
        .build();

    function handlePayload(payload) {
        if (!payload || !dotNetRef) return;

        const station = payload.stationName || payload.station || payload.StationName;
        const code = payload.code || payload.Code;
        const x = payload.x ?? payload.X ?? 0;
        const y = payload.y ?? payload.Y ?? 0;
        const w = payload.w ?? payload.W ?? 0;
        const h = payload.h ?? payload.H ?? 0;

        if (!station || !code) return;

        console.log("[scanOverlay] HIT:", station, code);

        // Gọi lại LiveCameraPage.OnScanResultFromServer(...)
        dotNetRef.invokeMethodAsync("OnScanResultFromServer", station, code, x, y, w, h)
            .catch(err => console.error("[scanOverlay] invoke error:", err));

        beep();
    }

    connection.on("ScanResult", handlePayload);
    connection.on("ScanHit", handlePayload);

    connection
        .start()
        .then(() => console.log("[scanOverlay] Connected successfully!!"))
        .catch(err => console.error("[scanOverlay] connect error:", err));

    window.scanOverlay = {
        init: init
    };
})();
