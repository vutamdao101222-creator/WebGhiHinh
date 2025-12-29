const ffmpeg = require("fluent-ffmpeg");
const axios = require("axios");
const { createCanvas, ImageData } = require("canvas");
const jsQR = require("jsqr");

/**
 * Danh sách camera (có thể load từ DB / config)
 */
const CAMERAS = [
    {
        rtspUrl: "rtsp://admin:123456@192.168.1.10:554/stream1",
        stationName: "Gate A",
    },
    {
        rtspUrl: "rtsp://admin:123456@192.168.1.11:554/stream1",
        stationName: "Gate B",
    },
];

const API_ENDPOINT = "http://localhost:5000/api/record/scan";

/**
 * Quét QR từ 1 camera
 */
function scanCamera(camera) {
    console.log(`📷 Start scanning: ${camera.stationName}`);

    const command = ffmpeg(camera.rtspUrl)
        .inputOptions(["-rtsp_transport tcp"])
        .outputOptions([
            "-vf fps=2",       // 2 frame / giây (tối ưu CPU)
            "-f image2pipe",
            "-vcodec png",
        ])
        .on("error", (err) => {
            console.error(`❌ Camera error (${camera.stationName}):`, err.message);
        });

    const stream = command.pipe();

    stream.on("data", async (chunk) => {
        try {
            const canvas = createCanvas(640, 480);
            const ctx = canvas.getContext("2d");

            const img = new ImageData(
                new Uint8ClampedArray(chunk),
                canvas.width,
                canvas.height
            );

            ctx.putImageData(img, 0, 0);

            const imageData = ctx.getImageData(
                0,
                0,
                canvas.width,
                canvas.height
            );

            const qr = jsQR(
                imageData.data,
                imageData.width,
                imageData.height
            );

            if (qr && qr.data) {
                console.log(
                    `✅ QR detected [${camera.stationName}]:`,
                    qr.data
                );

                // Gửi API
                await axios.post(API_ENDPOINT, {
                    qrCode: qr.data,              // VD: CTV0013
                    rtspUrl: camera.rtspUrl,
                    stationName: camera.stationName,
                    mode: 0,                      // 0 = camera
                });
            }
        } catch (err) {
            // bỏ qua frame lỗi
        }
    });
}

/**
 * Chạy song song tất cả camera
 */
function startAllCameras() {
    console.log("🚀 QR Worker started");
    CAMERAS.forEach(scanCamera);
}

startAllCameras();
