// wwwroot/js/camera.js

let videoStream = null;
let videoElement = null;
let canvasElement = null;
let canvasContext = null;
let dotnetHelper = null;
let isScanning = false;

// Khởi động Camera
window.startCamera = async (videoElem, canvasElem, helper) => {
    videoElement = videoElem;
    canvasElement = canvasElem;
    canvasContext = canvasElement.getContext('2d', { willReadFrequently: true });
    dotnetHelper = helper;

    try {
        videoStream = await navigator.mediaDevices.getUserMedia({
            video: { facingMode: "environment" } // Ưu tiên camera sau
        });

        videoElement.srcObject = videoStream;
        videoElement.setAttribute("playsinline", true); // Để chạy được trên iOS
        videoElement.play();

        isScanning = true;
        requestAnimationFrame(tick);
        return true;
    } catch (err) {
        console.error("Lỗi camera:", err);
        return false;
    }
};

// Vòng lặp quét QR
function tick() {
    if (!isScanning) return;

    if (videoElement.readyState === videoElement.HAVE_ENOUGH_DATA) {
        canvasElement.height = videoElement.videoHeight;
        canvasElement.width = videoElement.videoWidth;

        // Vẽ frame video lên canvas
        canvasContext.drawImage(videoElement, 0, 0, canvasElement.width, canvasElement.height);

        // Lấy dữ liệu ảnh
        const imageData = canvasContext.getImageData(0, 0, canvasElement.width, canvasElement.height);

        // Dùng thư viện jsQR để quét (đảm bảo đã load thư viện này ở App.razor)
        if (window.jsQR) {
            const code = jsQR(imageData.data, imageData.width, imageData.height, {
                inversionAttempts: "dontInvert",
            });

            if (code) {
                // Tìm thấy mã QR!
                // Vẽ khung bao quanh
                drawLine(code.location.topLeftCorner, code.location.topRightCorner, "#FF3B58");
                drawLine(code.location.topRightCorner, code.location.bottomRightCorner, "#FF3B58");
                drawLine(code.location.bottomRightCorner, code.location.bottomLeftCorner, "#FF3B58");
                drawLine(code.location.bottomLeftCorner, code.location.topLeftCorner, "#FF3B58");

                // Gửi mã về cho C# Blazor xử lý
                // Tạm dừng quét để tránh gửi liên tục
                isScanning = false;
                dotnetHelper.invokeMethodAsync('ProcessScan', code.data);
            }
        }
    }

    if (isScanning) {
        requestAnimationFrame(tick);
    }
}

// Hàm vẽ đường bao quanh QR
function drawLine(begin, end, color) {
    canvasContext.beginPath();
    canvasContext.moveTo(begin.x, begin.y);
    canvasContext.lineTo(end.x, end.y);
    canvasContext.lineWidth = 4;
    canvasContext.strokeStyle = color;
    canvasContext.stroke();
}

// Tắt Camera
window.stopCamera = () => {
    isScanning = false;
    if (videoStream) {
        videoStream.getTracks().forEach(track => track.stop());
        videoStream = null;
    }
};