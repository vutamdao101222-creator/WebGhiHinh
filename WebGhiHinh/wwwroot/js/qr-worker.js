// wwwroot/js/qr-worker.js
// Worker chỉ nhận ImageData và decode QR bằng jsQR

// Nếu anh đang dùng CDN jsQR:
self.importScripts("https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.min.js");

let last = "";

self.onmessage = function (e) {
    const { id, width, height, buffer } = e.data;
    const data = new Uint8ClampedArray(buffer);

    const res = jsQR(data, width, height);

    if (!res) {
        self.postMessage({ id, ok: false });
        return;
    }

    // text QR
    const text = (res.data || "").trim();

    self.postMessage({
        id,
        ok: true,
        text,
        corners: res.location ? res.location.cornerPoints : null
    });
};
