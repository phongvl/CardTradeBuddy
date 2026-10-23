/* The card outline on the camera screen, and the check that a card sits in it.
   No libraries: it finds the card's four edges near the outline in a small copy of the picture, so it is cheap enough to run
   a few times a second on a phone. It is a hint ("you can take the picture now"), not a guarantee: the server still reads the card.
   The edges do not have to be exactly on the outline, only close (about 12%): nobody holds a card exactly in a template. */
window.CardFrame = (function () {
    var ASPECT = 63 / 88;            // width / height of a Pokémon card
    var SEARCH = 0.12;               // how far from the outline (share of the card's size) an edge is looked for
    var EDGE_MIN = 9;                // the weakest change in brightness (0-255) that can count as an edge
    var OUTER_SHARE = 0.4;           // the outer edge is the first change at least this share of the strongest one on the line
    var SIDE_SHARE = 0.55;           // share of positions along a side whose edge lies where the side's edge is
    var SIZE_TOLERANCE = 0.12;       // how much too big or too small the card may be
    var SHIFT_TOLERANCE = 0.08;      // how far off centre (share of the outline's width)
    var BUSY = 16;                   // a pixel is "busy" when its brightness changes at least this much from its neighbours
    var BUSY_SHARE = 0.12;           // a row or column belongs to the card when at least this share of its pixels are busy
    var MIN_DETAIL = 10;             // average sharpness inside: below this it is blurred or empty

    // Where the outline goes in a picture of the given size: a card-shaped rounded rectangle in the middle.
    function rect(width, height) {
        var h = Math.min(height * 0.82, (width * 0.86) / ASPECT);
        var w = h * ASPECT;
        return { x: (width - w) / 2, y: (height - h) / 2, w: w, h: h, r: w * 0.045 };
    }

    // Path of a rounded rectangle (used for the outline, the green layer and the hole in the dark shade).
    function roundedPath(r) {
        var x = r.x, y = r.y, w = r.w, h = r.h, c = r.r;
        return 'M' + (x + c) + ' ' + y + 'H' + (x + w - c) + 'A' + c + ' ' + c + ' 0 0 1 ' + (x + w) + ' ' + (y + c) +
            'V' + (y + h - c) + 'A' + c + ' ' + c + ' 0 0 1 ' + (x + w - c) + ' ' + (y + h) +
            'H' + (x + c) + 'A' + c + ' ' + c + ' 0 0 1 ' + x + ' ' + (y + h - c) +
            'V' + (y + c) + 'A' + c + ' ' + c + ' 0 0 1 ' + (x + c) + ' ' + y + 'Z';
    }

    function gray(data, width, height) {
        var g = new Float32Array(width * height);
        for (var i = 0, p = 0; i < g.length; i++, p += 4)
            g[i] = 0.299 * data[p] + 0.587 * data[p + 1] + 0.114 * data[p + 2];
        return g;
    }

    function median(values) {
        var a = values.slice().sort(function (p, q) { return p - q; });
        return a.length ? a[a.length >> 1] : NaN;
    }

    /* Looks at a small picture (RGBA pixels, like canvas getImageData) and the outline in the same pixel units.
       Finds where the card's top, right, bottom and left edges are and compares that rectangle with the outline.
       Returns { ok, fit, sides, detail, card }:
         ok     true when all four edges are found and the card is about the size and place of the outline and sharp
         fit    when the edges are found but the card is off: 'small', 'big' or 'off' (shifted or the wrong shape); otherwise null
         sides  how sure each edge is (0-1): top, right, bottom, left
         card   the rectangle found, or null */
    function analyze(data, width, height, r) {
        var g = gray(data, width, height);
        var mx = Math.max(3, Math.round(r.w * SEARCH));   // search distance for the vertical sides
        var my = Math.max(3, Math.round(r.h * SEARCH));   // search distance for the horizontal sides
        var skip = Math.round(r.w * 0.14);                 // the rounded corners are left out
        var tol = Math.max(2, Math.round(r.w * 0.025));

        function idx(x, y) {
            x = Math.min(width - 2, Math.max(1, Math.round(x)));
            y = Math.min(height - 2, Math.max(1, Math.round(y)));
            return y * width + x;
        }

        // Along one side: for each position, the outer edge on the line across it. Returns { at, share }.
        //   horizontal: a top or bottom side (positions run along x, the edge is a y)
        //   fixed:      where the outline is (a y, or an x)
        //   inward:     +1 when the card lies at larger coordinates than the outside (top, left), -1 otherwise
        function side(horizontal, fixed, inward, from, to, reach) {
            var found = [];
            var total = 0;
            for (var t = from; t <= to; t++) {
                total++;
                // gradient across the line, from the outside (fixed - inward * reach) to the inside
                var grads = [], best = 0;
                for (var o = -reach; o <= reach; o++) {
                    var p = fixed + inward * o;
                    var i = horizontal ? idx(t, p) : idx(p, t);
                    var d = horizontal ? Math.abs(g[i + width] - g[i - width]) : Math.abs(g[i + 1] - g[i - 1]);
                    grads.push(d);
                    if (d > best) best = d;
                }
                if (best < EDGE_MIN) continue;
                // the first change on the way in that is strong enough is the card's outer edge
                for (var k = 0; k < grads.length; k++) {
                    if (grads[k] >= best * OUTER_SHARE) { found.push(fixed + inward * (k - reach)); break; }
                }
            }
            if (!found.length) return { at: NaN, share: 0 };
            var at = median(found);
            var near = 0;
            for (var f = 0; f < found.length; f++) if (Math.abs(found[f] - at) <= tol) near++;
            return { at: at, share: near / Math.max(1, total) };
        }

        // Scanning inward means: top/left look at increasing coordinates, bottom/right at decreasing ones.
        var top = side(true, r.y, +1, Math.round(r.x + skip), Math.round(r.x + r.w - skip), my);
        var bottom = side(true, r.y + r.h, -1, Math.round(r.x + skip), Math.round(r.x + r.w - skip), my);
        var left = side(false, r.x, +1, Math.round(r.y + skip), Math.round(r.y + r.h - skip), mx);
        var right = side(false, r.x + r.w, -1, Math.round(r.y + skip), Math.round(r.y + r.h - skip), mx);
        var sides = [top.share, right.share, bottom.share, left.share];

        // Sharpness inside the outline: the average change between neighbouring pixels.
        var sum = 0, n = 0;
        for (var y = Math.round(r.y + r.h * 0.1); y < r.y + r.h * 0.9; y += 2) {
            for (var x = Math.round(r.x + r.w * 0.1); x < r.x + r.w * 0.9; x += 2) {
                var j = idx(x, y);
                sum += Math.abs(g[j + 1] - g[j - 1]) + Math.abs(g[j + width] - g[j - width]);
                n++;
            }
        }
        var detail = n ? sum / n : 0;

        // First estimate: the four edges. Second estimate (for flat, low-contrast surfaces where the border hardly shows): where
        // the busy part of the picture, the card's artwork and print, begins and ends. Either one is enough.
        var edgeCard = Math.min.apply(null, sides) >= SIDE_SHARE
            ? { x: left.at, y: top.at, w: right.at - left.at, h: bottom.at - top.at }
            : null;
        var busyCard = busyBox();

        var edgeFit = edgeCard ? judge(edgeCard) : undefined;
        var busyFit = busyCard ? judge(busyCard) : undefined;
        var sharp = detail >= MIN_DETAIL;

        if (sharp && (edgeFit === null || busyFit === null))
            return { ok: true, fit: null, sides: sides, detail: detail, card: edgeFit === null ? edgeCard : busyCard };

        // Not good yet: tell which way to move when a card was found but is the wrong size.
        var fit = edgeFit || busyFit || null;
        return { ok: false, fit: fit, sides: sides, detail: detail, card: edgeCard || busyCard || null };

        // null when the rectangle is about the size, place and shape of the outline, otherwise 'small', 'big' or 'off'.
        function judge(c) {
            if (c.w <= 0 || c.h <= 0) return 'off';
            var scaleW = c.w / r.w, scaleH = c.h / r.h;
            var shiftX = Math.abs(c.x + c.w / 2 - (r.x + r.w / 2)) / r.w;
            var shiftY = Math.abs(c.y + c.h / 2 - (r.y + r.h / 2)) / r.w;
            var shape = (c.w / c.h) / ASPECT;                 // 1 = card shaped
            if (Math.max(scaleW, scaleH) > 1 + SIZE_TOLERANCE) return 'big';
            if (Math.min(scaleW, scaleH) < 1 - SIZE_TOLERANCE) return 'small';
            if (shiftX > SHIFT_TOLERANCE || shiftY > SHIFT_TOLERANCE || shape < 0.85 || shape > 1.15) return 'off';
            return null;
        }

        // The extent of the busy part of the picture around the outline, from how many pixels in each row and column are busy.
        function busyBox() {
            var x0 = Math.max(1, Math.round(r.x - r.w * 0.2)), x1 = Math.min(width - 2, Math.round(r.x + r.w * 1.2));
            var y0 = Math.max(1, Math.round(r.y - r.h * 0.2)), y1 = Math.min(height - 2, Math.round(r.y + r.h * 1.2));
            var cols = new Float32Array(x1 - x0 + 1), rows = new Float32Array(y1 - y0 + 1);
            for (var y = y0; y <= y1; y++) {
                for (var x = x0; x <= x1; x++) {
                    var q = y * width + x;
                    if (Math.abs(g[q + 1] - g[q - 1]) + Math.abs(g[q + width] - g[q - width]) >= BUSY) { cols[x - x0]++; rows[y - y0]++; }
                }
            }
            var xs = extent(cols, y1 - y0 + 1), ys = extent(rows, x1 - x0 + 1);
            return xs && ys ? { x: x0 + xs[0], y: y0 + ys[0], w: xs[1] - xs[0], h: ys[1] - ys[0] } : null;
        }

        // First and last index whose (smoothed) share of busy pixels reaches BUSY_SHARE.
        function extent(counts, of) {
            var a = -1, b = -1;
            for (var i = 0; i < counts.length; i++) {
                var v = (counts[Math.max(0, i - 1)] + counts[i] + counts[Math.min(counts.length - 1, i + 1)]) / (3 * of);
                if (v >= BUSY_SHARE) { if (a < 0) a = i; b = i; }
            }
            return a < 0 ? null : [a, b];
        }
    }

    return { ASPECT: ASPECT, rect: rect, roundedPath: roundedPath, analyze: analyze };
})();
