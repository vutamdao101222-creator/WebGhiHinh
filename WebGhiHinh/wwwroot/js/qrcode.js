// Biến toàn cục để quản lý Stream và Animation
let videoStream = null;
let isScanning = false;
let dotNetHelper = null;

// Hàm khởi động Camera
window.startCamera = async (videoElement, canvasElement, dotNetRef) => {
    dotNetHelper = dotNetRef;
    isScanning = true;

    // 1. Kiểm tra môi trường bảo mật (Secure Context)
    // Camera yêu cầu HTTPS hoặc Localhost. Nếu đang chạy HTTP LAN, phải cấu hình chrome://flags
    if (!window.isSecureContext && location.hostname !== 'localhost' && location.hostname !== '127.0.0.1') {
        alert("⚠️ Lỗi Bảo Mật: Trình duyệt chặn Camera trên đường truyền không an toàn (HTTP).\n\nVui lòng cấu hình 'unsafely-treat-insecure-origin-as-secure' trên điện thoại hoặc sử dụng HTTPS.");
        // Vẫn cố gắng chạy tiếp phòng trường hợp đã cấu hình flags
    }

    // Cấu hình ưu tiên Camera sau (environment)
    const constraints = {
        video: {
            facingMode: "environment",
            width: { ideal: 1280 },
            height: { ideal: 720 }
        }
    };

    try {
        // Yêu cầu quyền truy cập Camera
        // Trình duyệt sẽ TỰ ĐỘNG hiện popup hỏi quyền tại đây nếu chưa cấp.
        // Nếu đã cấp, nó sẽ tự động chạy tiếp.
        const stream = await navigator.mediaDevices.getUserMedia(constraints);

        videoStream = stream;
        videoElement.srcObject = stream;

        // Cần set attribute này để chạy được trên iPhone/Safari
        videoElement.setAttribute("playsinline", true);

        // Chờ video bắt đầu chạy mới quét
        await videoElement.play();

        // Bắt đầu vòng lặp quét
        requestAnimationFrame(() => tick(videoElement, canvasElement));
        return true;
    } catch (err) {
        console.error("Lỗi Camera:", err);

        // Phân loại lỗi để báo cho người dùng
        if (err.name === 'NotAllowedError' || err.name === 'PermissionDeniedError') {
            alert("🚫 Bạn đã CHẶN quyền truy cập Camera.\n\nHãy bấm vào biểu tượng ổ khóa 🔒 trên thanh địa chỉ -> Chọn 'Quyền' -> Bật Camera, sau đó tải lại trang.");
        } else if (err.name === 'NotFoundError') {
            alert("📷 Không tìm thấy thiết bị Camera nào trên máy này.");
        } else if (err.name === 'NotReadableError') {
            alert("⚠️ Camera đang được sử dụng bởi ứng dụng khác hoặc bị lỗi phần cứng.");
        } else {
            alert(`❌ Không thể mở Camera: ${err.name}\nKiểm tra lại kết nối HTTPS.`);
        }

        return false;
    }
};

// Vòng lặp quét ảnh từ Video
function tick(video, canvas) {
    if (!isScanning) return;

    if (video.readyState === video.HAVE_ENOUGH_DATA) {
        const ctx = canvas.getContext("2d", { willReadFrequently: true });

        // Đồng bộ kích thước canvas với video
        canvas.height = video.videoHeight;
        canvas.width = video.videoWidth;

        // Vẽ frame hiện tại lên canvas
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

        // Lấy dữ liệu ảnh để jsQR phân tích
        const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);

        // Gọi thư viện jsQR (phải đảm bảo đã import thư viện này)
        // Code quét mã: tìm mã QR trong hình
        if (typeof jsQR !== 'undefined') {
            const code = jsQR(imageData.data, imageData.width, imageData.height, {
                inversionAttempts: "dontInvert",
            });

            if (code) {
                // Nếu tìm thấy mã -> Gửi về C#
                console.log("Tìm thấy QR:", code.data);

                // Vẽ khung bao quanh mã QR để user biết
                drawQuad(code.location, "#FF3B58", ctx);

                // Gọi hàm C# 'ProcessScan'
                dotNetHelper.invokeMethodAsync('ProcessScan', code.data);

                // Tạm dừng quét để tránh gửi liên tục
                isScanning = false;
                return;
            }
        }
    }

    // Tiếp tục vòng lặp
    requestAnimationFrame(() => tick(video, canvas));
}

// Hàm vẽ khung bao quanh mã QR
function drawQuad(location, color, ctx) {
    drawLine(location.topLeftCorner, location.topRightCorner, color, ctx);
    drawLine(location.topRightCorner, location.bottomRightCorner, color, ctx);
    drawLine(location.bottomRightCorner, location.bottomLeftCorner, color, ctx);
    drawLine(location.bottomLeftCorner, location.topLeftCorner, color, ctx);
}

function drawLine(begin, end, color, ctx) {
    ctx.beginPath();
    ctx.moveTo(begin.x, begin.y);
    ctx.lineTo(end.x, end.y);
    ctx.lineWidth = 4;
    ctx.strokeStyle = color;
    ctx.stroke();
}

// Tắt Camera
window.stopCamera = () => {
    isScanning = false;
    if (videoStream) {
        videoStream.getTracks().forEach(track => track.stop());
        videoStream = null;
    }
};

// Bật/Tắt đèn Flash (Torch)
window.toggleFlash = async (on) => {
    if (videoStream) {
        const track = videoStream.getVideoTracks()[0];
        try {
            await track.applyConstraints({
                advanced: [{ torch: on }]
            });
        } catch (err) {
            console.warn("Thiết bị không hỗ trợ Flash hoặc API Torch:", err);
        }
    }
};

// Âm thanh 'Bíp' khi quét thành công
window.playBeep = () => {
    const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
    const oscillator = audioCtx.createOscillator();
    const gainNode = audioCtx.createGain();

    oscillator.connect(gainNode);
    gainNode.connect(audioCtx.destination);

    oscillator.type = "sine";
    oscillator.frequency.value = 800; // Tần số âm thanh
    gainNode.gain.value = 0.1; // Âm lượng

    oscillator.start();
    setTimeout(() => oscillator.stop(), 150); // Phát trong 150ms
};