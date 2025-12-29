window.hlsPlayer = {
    start: function (videoElementId, url) {
        var video = document.getElementById(videoElementId);
        if (!video) return;

        // 🔥 ÉP TẮT TIẾNG NGAY TỪ ĐẦU (Quan trọng cho Chrome/Edge)
        video.muted = true;
        video.setAttribute("muted", "true");

        if (Hls.isSupported()) {
            var hls = new Hls({
                debug: false,
                latencyPreference: 'low',
            });

            hls.loadSource(url);
            hls.attachMedia(video);

            hls.on(Hls.Events.MEDIA_ATTACHED, function () {
                video.muted = true; // Đảm bảo tắt tiếng lần nữa

                // 🔥 XỬ LÝ PROMISE ĐỂ CHỐNG LỖI AUTOPLAY BLOCKED
                var playPromise = video.play();
                if (playPromise !== undefined) {
                    playPromise.then(function () {
                        // Tự động phát thành công
                    }).catch(function (error) {
                        console.log("⚠️ Autoplay bị chặn, đang thử ép chạy lại với Mute...", error);
                        video.muted = true;
                        video.play(); // Thử lại lần nữa
                    });
                }
            });

            // Xử lý lỗi fatal để tự hồi phục stream
            hls.on(Hls.Events.ERROR, function (event, data) {
                if (data.fatal) {
                    switch (data.type) {
                        case Hls.ErrorTypes.NETWORK_ERROR:
                            console.log("Mạng lỗi, đang thử kết nối lại...");
                            hls.startLoad();
                            break;
                        case Hls.ErrorTypes.MEDIA_ERROR:
                            console.log("Media lỗi, đang hồi phục...");
                            hls.recoverMediaError();
                            break;
                        default:
                            hls.destroy();
                            break;
                    }
                }
            });

            video.hlsInstance = hls;
        }
        else if (video.canPlayType('application/vnd.apple.mpegurl')) {
            // Dành cho Safari
            video.src = url;
            video.addEventListener('loadedmetadata', function () {
                video.play();
            });
        }
    },

    stop: function (videoElementId) {
        var video = document.getElementById(videoElementId);
        if (video && video.hlsInstance) {
            video.hlsInstance.destroy();
            delete video.hlsInstance;
        }
    }
};