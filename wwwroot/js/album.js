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
            slot.innerHTML = '<span class="album-thumb empty"></span><span class="album-name">&nbsp;</span>';
            grid.appendChild(slot);
        }
    }

    fill();
    if ('ResizeObserver' in window) new ResizeObserver(fill).observe(grid);
    else window.addEventListener('resize', fill);
})();
