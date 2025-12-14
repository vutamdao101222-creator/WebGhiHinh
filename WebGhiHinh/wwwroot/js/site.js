window.showToast = function (message, type = "info") {
    const box = document.createElement("div");
    box.className = `toast-msg toast-${type}`;
    box.innerText = message;

    document.body.appendChild(box);

    setTimeout(() => box.classList.add("show"), 10);
    setTimeout(() => box.classList.remove("show"), 2500);
    setTimeout(() => box.remove(), 3000);
};

window.highlightStationCam = function (stationName, action) {
    try {
        const el = document.querySelector(`[data-station='${stationName}']`);
        if (!el) return;

        el.classList.remove("highlight-start", "highlight-stop");

        if (action === "start") el.classList.add("highlight-start");
        if (action === "stop") el.classList.add("highlight-stop");

        setTimeout(() => {
            el.classList.remove("highlight-start", "highlight-stop");
        }, 1500);
    } catch (e) {
        console.warn("highlight error", e);
    }
};
