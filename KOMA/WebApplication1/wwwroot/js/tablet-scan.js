(() => {
    const form = document.querySelector('#scan-form');
    if (!form) return;
    const video = document.querySelector('#camera');
    const button = document.querySelector('#scan');
    const message = document.querySelector('#camera-message');
    let stream;
    function stop() { stream?.getTracks().forEach(track => track.stop()); }
    function submit(code) {
        if (!/^[A-F0-9]{48}$/.test(code)) return false;
        stop();
        document.querySelector('#scan-code').value = code;
        history.replaceState(null, '', location.pathname);
        form.requestSubmit();
        return true;
    }
    if (submit(location.hash.slice(1))) return;
    window.addEventListener('pagehide', stop);
    button.addEventListener('click', async () => {
        button.disabled = true;
        try {
            stream = await navigator.mediaDevices.getUserMedia({video:{facingMode:'environment'},audio:false});
            video.srcObject = stream;
            video.hidden = false;
            await video.play();
            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d', {willReadFrequently:true});
            function frame() {
                if (video.readyState >= 2) {
                    canvas.width = 480;
                    canvas.height = Math.round(480 * video.videoHeight / video.videoWidth);
                    ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
                    const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
                    const found = jsQR(img.data, img.width, img.height);
                    if (found) {
                        try { const link = new URL(found.data);
                            if (link.origin === location.origin && submit(link.hash.slice(1))) return;
                        } catch { /* Keep scanning. */ }
                    }
                }
                if (stream.active) setTimeout(frame, 120);
            }
            frame();
        } catch {
            stop(); video.hidden = true; button.disabled = false;
            message.textContent = 'Allow camera access on HTTPS, or scan the QR with this tablet’s camera app and open the link in this browser.';
        }
    });
})();
