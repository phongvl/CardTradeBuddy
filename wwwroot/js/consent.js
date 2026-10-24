/* The cookie dialog: while it is open nothing behind it can be used. The dialog covers the page (CSS); this also makes the page
   inert, so the keyboard cannot reach it and screen readers skip it, and puts the focus on the one button. */
(function () {
    var box = document.getElementById('cookie-consent');
    if (!box) return;

    document.body.classList.add('consent-open');

    Array.prototype.forEach.call(document.body.children, function (el) {
        if (el !== box && el.tagName !== 'SCRIPT' && el.tagName !== 'LINK') el.inert = true;
    });

    var accept = box.querySelector('.consent-accept');
    if (accept) accept.focus();
})();
