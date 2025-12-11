window.stationGuard = (() => {
    let stationId = null;
    let enabled = false;

    async function releaseNow() {
        if (!stationId) return;

        try {
            const token = localStorage.getItem("token");
            if (!token) return;

            const base = window.location.origin;

            await fetch(`${base}/api/station/release`, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "Authorization": "Bearer " + token
                },
                body: JSON.stringify({ stationId }),
                keepalive: true
            });
        } catch { }
    }

    function onBeforeUnload() {
        if (enabled) releaseNow();
    }

    function onVisibilityChange() {
        // Mobile swipe / app background
        if (enabled && document.visibilityState === "hidden") {
            releaseNow();
        }
    }

    return {
        register: (id) => {
            stationId = id;
            enabled = true;
            window.addEventListener("beforeunload", onBeforeUnload);
            document.addEventListener("visibilitychange", onVisibilityChange);
        },
        unregister: () => {
            enabled = false;
            stationId = null;
            window.removeEventListener("beforeunload", onBeforeUnload);
            document.removeEventListener("visibilitychange", onVisibilityChange);
        },
        forceReleaseNow: releaseNow
    };
})();
