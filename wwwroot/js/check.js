/* The Check a card page: choose a photo (camera, file or drag and drop), shrink it, show a preview, and send it.
   The server never stores the photo. Without script the page cannot pick a photo (see the noscript note). */
(function () {
    var form = document.getElementById('check-form');
    if (!form) return;

    var photo = document.getElementById('photo');
    var pickFile = document.getElementById('pick-file');
    var pickCamera = document.getElementById('pick-camera');
    var choose = document.getElementById('choose');
    var preview = document.getElementById('preview');
    var previewImg = document.getElementById('preview-img');
    var wait = document.getElementById('check-wait');
    var dropzone = document.getElementById('dropzone');
    var previewUrl = null;

    // Phone photos are big; 2400 px on the long side is far more than the card reader needs.
    var MAX_SIDE = 2400;
    var SHRINK_ABOVE_BYTES = 1500000;

    var panel = document.querySelector('.check-panel');

    function show(state) {
        // A chosen photo needs the room: the picture and the heading step aside on small screens.
        panel.classList.toggle('is-previewing', state !== 'choose');
        choose.hidden = state !== 'choose';
        // While the card is being found the photo stays on the page with a scan running over it; only the buttons go.
        preview.hidden = state === 'choose';
        preview.classList.toggle('is-scanning', state === 'wait');
        wait.hidden = state !== 'wait';
    }

    // Returns a (possibly smaller) JPEG file, or the original when it cannot be decoded here (the server then decides).
    async function shrink(file) {
        try {
            var bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
            var scale = Math.min(1, MAX_SIDE / Math.max(bitmap.width, bitmap.height));
            if (scale === 1 && file.size <= SHRINK_ABOVE_BYTES && file.type === 'image/jpeg') return file;

            var canvas = document.createElement('canvas');
            canvas.width = Math.round(bitmap.width * scale);
            canvas.height = Math.round(bitmap.height * scale);
            canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height);
            bitmap.close();

            var blob = await new Promise(function (resolve) { canvas.toBlob(resolve, 'image/jpeg', 0.9); });
            return blob ? new File([blob], 'card.jpg', { type: 'image/jpeg' }) : file;
        } catch (e) {
            return file;
        }
    }

    async function take(file) {
        if (!file) return;
        var ready = await shrink(file);

        var transfer = new DataTransfer();
        transfer.items.add(ready);
        photo.files = transfer.files;

        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = URL.createObjectURL(ready);
        previewImg.src = previewUrl;
        show('preview');
        document.getElementById('btn-check').focus();
    }

    function reset() {
        photo.value = '';
        pickFile.value = '';
        pickCamera.value = '';
        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = null;
        previewImg.removeAttribute('src');
        show('choose');
    }

    pickFile.addEventListener('change', function () { take(pickFile.files[0]); });
    pickCamera.addEventListener('change', function () { take(pickCamera.files[0]); });
    document.getElementById('btn-upload').addEventListener('click', function () { pickFile.click(); });
    document.getElementById('btn-again').addEventListener('click', reset);

    // Drag a photo into the box (computers).
    ['dragenter', 'dragover'].forEach(function (name) {
        dropzone.addEventListener(name, function (e) { e.preventDefault(); dropzone.classList.add('is-over'); });
    });
    ['dragleave', 'drop'].forEach(function (name) {
        dropzone.addEventListener(name, function (e) { e.preventDefault(); dropzone.classList.remove('is-over'); });
    });
    dropzone.addEventListener('drop', function (e) {
        var file = e.dataTransfer && e.dataTransfer.files[0];
        if (file && file.type.indexOf('image/') === 0) take(file);
    });

    // ---- The camera screen ----
    // A live picture with a card-shaped outline. When a card sits in the outline it turns green ("take the picture now"); the
    // photo is cut to the outline before it goes on. If the camera cannot be used, a phone falls back to its own camera app.
    var camera = document.getElementById('camera');
    var stage = document.getElementById('camera-stage');
    var video = document.getElementById('camera-video');
    var overlay = document.getElementById('camera-overlay');
    var hint = document.getElementById('camera-hint');
    var shutter = document.getElementById('camera-shoot');
    var stream = null;
    var timer = null;
    var greenInARow = 0;
    var sampler = document.createElement('canvas');

    // Add ?camdebug=1 to the address to see what the check measures on the camera screen (for tuning on a real phone).
    var debug = /[?&]camdebug\b/.test(location.search);

    // The hint under the picture: good to go, or which way to move, or the plain instruction.
    function setHint(ok, result) {
        var key = ok ? 'data-ready'
            : result && result.fit === 'small' ? 'data-closer'
            : result && result.fit === 'big' ? 'data-further'
            : 'data-hint';
        var text = hint.getAttribute(key);
        if (debug && result) {
            text += ' [' + result.sides.map(function (v) { return v.toFixed(2); }).join('/') + ' detail ' + result.detail.toFixed(0) + ' ' + (result.fit || (ok ? 'ok' : '-')) + ']';
        }
        hint.textContent = text;
    }

    // Puts the outline over the picture. The overlay uses the picture's own pixel size, so it lines up whatever the screen size.
    function layout() {
        var vw = video.videoWidth, vh = video.videoHeight;
        if (!vw || !vh) return;
        overlay.setAttribute('viewBox', '0 0 ' + vw + ' ' + vh);

        var r = CardFrame.rect(vw, vh);
        var rounded = CardFrame.roundedPath(r);
        document.getElementById('camera-shade').setAttribute('d', 'M0 0H' + vw + 'V' + vh + 'H0Z' + rounded);
        document.getElementById('camera-fill').setAttribute('d', rounded);
        document.getElementById('camera-frame').setAttribute('d', rounded);
        overlay.style.setProperty('--line', (vw * 0.009) + 'px');
    }

    // A few times a second: copy a small version of the picture and see whether a card sits in the outline.
    function look() {
        var vw = video.videoWidth, vh = video.videoHeight;
        if (!vw || !vh) return;

        var r = CardFrame.rect(vw, vh);
        var scale = 170 / r.w;
        sampler.width = Math.round(vw * scale);
        sampler.height = Math.round(vh * scale);
        var ctx = sampler.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(video, 0, 0, sampler.width, sampler.height);
        var small = { x: r.x * scale, y: r.y * scale, w: r.w * scale, h: r.h * scale, r: r.r * scale };
        var result = CardFrame.analyze(ctx.getImageData(0, 0, sampler.width, sampler.height).data, sampler.width, sampler.height, small);

        // Green only after a few good looks in a row, so it does not flicker while the card moves.
        greenInARow = result.ok ? greenInARow + 1 : 0;
        var ok = greenInARow >= 3;
        overlay.classList.toggle('ok', ok);
        setHint(ok, result);
    }

    function closeCamera() {
        clearInterval(timer);
        timer = null;
        if (stream) stream.getTracks().forEach(function (t) { t.stop(); });
        stream = null;
        video.srcObject = null;
        camera.hidden = true;
        document.body.classList.remove('camera-open');
    }

    async function openCamera() {
        try {
            stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: { ideal: 'environment' }, width: { ideal: 1920 }, height: { ideal: 1080 } },
                audio: false
            });
        } catch (e) {
            // No camera, or it was refused: a phone can still use its own camera app.
            if (window.matchMedia('(pointer: coarse)').matches) pickCamera.click();
            else alert(form.getAttribute('data-camera-denied'));
            return;
        }

        greenInARow = 0;
        overlay.classList.remove('ok');
        setHint(false);
        video.srcObject = stream;
        camera.hidden = false;
        document.body.classList.add('camera-open');
        shutter.focus();
        await video.play().catch(function () { });
        layout();
        timer = setInterval(look, 250);
    }

    document.getElementById('btn-camera').addEventListener('click', function () {
        if (!window.isSecureContext || !navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
            // The live camera needs https; otherwise a phone opens its camera app, a computer lets the user pick a file.
            if (window.matchMedia('(pointer: coarse)').matches) pickCamera.click(); else pickFile.click();
            return;
        }
        openCamera();
    });

    video.addEventListener('loadedmetadata', layout);
    video.addEventListener('resize', layout);

    // The picture is cut to the outline (with a little margin) at the camera's full resolution.
    shutter.addEventListener('click', function () {
        var vw = video.videoWidth, vh = video.videoHeight;
        if (!vw || !vh) return;

        var r = CardFrame.rect(vw, vh);
        var pad = r.w * 0.03;
        var x = Math.max(0, r.x - pad), y = Math.max(0, r.y - pad);
        var w = Math.min(vw - x, r.w + 2 * pad), h = Math.min(vh - y, r.h + 2 * pad);

        var canvas = document.createElement('canvas');
        canvas.width = Math.round(w);
        canvas.height = Math.round(h);
        canvas.getContext('2d').drawImage(video, x, y, w, h, 0, 0, canvas.width, canvas.height);
        canvas.toBlob(function (blob) {
            closeCamera();
            if (blob) take(new File([blob], 'card.jpg', { type: 'image/jpeg' }));
        }, 'image/jpeg', 0.92);
    });

    document.getElementById('camera-cancel').addEventListener('click', closeCamera);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape' && !camera.hidden) closeCamera(); });

    // Sending takes a few seconds (the card is read by a model): say so, and do not allow a second press.
    form.addEventListener('submit', function (e) {
        if (!photo.files.length) { e.preventDefault(); return; }
        show('wait');
    });

    // Coming back with the browser's back button must not show a stuck "looking" state.
    window.addEventListener('pageshow', function (e) { if (e.persisted && !wait.hidden) reset(); });
})();
