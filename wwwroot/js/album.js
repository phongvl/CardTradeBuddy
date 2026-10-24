/* The album page: fills the last row of the thumbnail grid with empty card slots, so an album with few cards does not look bare.
   The number of columns depends on the screen (the grid fits as many thumbnails as there is room for), so the slots are added here,
   where the real number of columns can be read, and redone when the window changes size. Without script the grid just ends. */
(function () {
    var grid = document.querySelector('.album-grid');
    if (!grid) return;

    var lastColumns = 0;

    function columns() {
        return getComputedStyle(grid).gridTemplateColumns.split(' ').filter(Boolean).length;
    }

    function fill() {
        var cols = columns();
        if (cols === lastColumns) return;
        lastColumns = cols;

        grid.querySelectorAll('.album-slot').forEach(function (slot) { slot.remove(); });

        var missing = (cols - (grid.children.length % cols)) % cols;
        for (var i = 0; i < missing; i++) {
            var slot = document.createElement('li');
            slot.className = 'album-card album-slot';
            slot.setAttribute('aria-hidden', 'true');
            slot.innerHTML = '<span class="album-thumb empty"></span><span class="album-name">&nbsp;</span><span class="album-price">&nbsp;</span>';
            grid.appendChild(slot);
        }
    }

    fill();
    if ('ResizeObserver' in window) new ResizeObserver(fill).observe(grid);
    else window.addEventListener('resize', fill);
})();

/* The album page: the card popup (everything saved about a card, with a button that looks the card up again) and "update all".
   The popup is loaded from the server when a thumbnail is clicked; the updates are posts with the anti-forgery token of the hidden form. */
(function () {
    var form = document.getElementById('album-form');
    var dialog = document.getElementById('card-dialog');
    if (!form || !dialog) return;

    var body = document.getElementById('card-dialog-body');
    var changed = false;
    var opener = null;

    function token() {
        var field = form.querySelector('input[name="__RequestVerificationToken"]');
        return field ? field.value : '';
    }

    function post(url, extra) {
        var data = new URLSearchParams(extra || {});
        data.set('__RequestVerificationToken', token());
        return fetch(url, { method: 'POST', body: data, headers: { 'Content-Type': 'application/x-www-form-urlencoded' } });
    }

    // ---- The popup of one card ----
    async function open(id, button) {
        opener = button;
        changed = false;
        body.innerHTML = '<p class="detail-loading">' + form.getAttribute('data-loading') + '</p>';
        dialog.showModal();
        try {
            var response = await fetch(form.getAttribute('data-card-url') + '&id=' + encodeURIComponent(id));
            if (!response.ok) throw new Error(response.status);
            body.innerHTML = await response.text();
            var title = body.querySelector('#card-dialog-title');
            if (title) { title.setAttribute('tabindex', '-1'); title.focus(); }
        } catch (e) {
            body.innerHTML = '<p class="message error">' + form.getAttribute('data-failed') + '</p><div class="detail-buttons"><button type="button" class="btn btn-secondary" data-close-card>X</button></div>';
        }
    }

    async function refresh(button) {
        var id = button.getAttribute('data-refresh-card');
        var label = button.textContent;
        button.disabled = true;
        button.textContent = form.getAttribute('data-updating');
        try {
            var response = await post(form.getAttribute('data-refresh-card-url') + '&id=' + encodeURIComponent(id));
            if (!response.ok) throw new Error(response.status);
            body.innerHTML = await response.text();
            changed = true;                      // the list behind is out of date now: it is reloaded when the popup closes
        } catch (e) {
            button.disabled = false;
            button.textContent = label;
            var note = document.createElement('p');
            note.className = 'message error';
            note.textContent = form.getAttribute('data-failed');
            button.closest('.detail').insertBefore(note, button.closest('.detail-buttons'));
        }
    }

    document.addEventListener('click', function (e) {
        var card = e.target.closest('[data-card]');
        if (card) { open(card.getAttribute('data-card'), card); return; }

        var update = e.target.closest('[data-refresh-card]');
        if (update) { refresh(update); return; }

        if (e.target.closest('[data-close-card]')) dialog.close();
    });

    // A click on the dark backdrop closes it too.
    dialog.addEventListener('click', function (e) { if (e.target === dialog) dialog.close(); });

    dialog.addEventListener('close', function () {
        if (changed) { location.reload(); return; }
        if (opener) opener.focus();
    });

    // ---- Update all: the album is looked up again a few cards at a time, with the progress shown ----
    var all = document.getElementById('update-all');
    if (!all) return;

    var status = document.getElementById('update-status');
    var text = document.getElementById('update-text');
    var fill = document.getElementById('update-fill');
    var BATCH = 6;

    function fmt(template) {
        var args = Array.prototype.slice.call(arguments, 1);
        return template.replace(/\{(\d+)\}/g, function (m, i) { return args[i]; });
    }

    all.addEventListener('click', async function () {
        var total = parseInt(all.getAttribute('data-total'), 10) || 0;
        if (!total) return;

        all.disabled = true;
        status.hidden = false;
        status.classList.remove('is-error');

        var skip = 0, updated = 0, notFound = 0, failed = 0;
        try {
            while (skip < total) {
                text.textContent = fmt(all.getAttribute('data-progress'), Math.min(skip + 1, total), total);
                fill.style.width = Math.round(skip / total * 100) + '%';

                var extra = { skip: skip, take: BATCH };
                if (form.getAttribute('data-album')) extra.album = form.getAttribute('data-album');
                var response = await post(form.getAttribute('data-refresh-all-url'), extra);
                if (!response.ok) throw new Error(response.status);

                var batch = await response.json();
                updated += batch.updated; notFound += batch.notFound; failed += batch.failed;
                if (!batch.processed) break;
                skip += batch.processed;
                total = batch.total;
            }

            fill.style.width = '100%';
            text.textContent = fmt(all.getAttribute('data-done'), skip) + ' ' + fmt(all.getAttribute('data-summary'), updated, notFound, failed);
            if (failed || notFound) status.classList.add('is-error');
            // Show the new prices and totals.
            setTimeout(function () { location.reload(); }, failed || notFound ? 3500 : 1200);
        } catch (e) {
            status.classList.add('is-error');
            text.textContent = all.getAttribute('data-error');
            all.disabled = false;
        }
    });
})();
