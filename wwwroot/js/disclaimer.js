/* The disclaimer popup on the Create user page. Pressing "Create user" opens it instead of sending the form. The text has to be read to
   the end (the accept button stays disabled until it has been scrolled to the bottom) and accepted; only then is the form sent, with
   the hidden field DisclaimerAccepted set. The server refuses a sign-up without it. */
(function () {
    var form = document.getElementById('register-form');
    var dialog = document.getElementById('disclaimer');
    var accepted = document.getElementById('disclaimer-accepted');
    if (!form || !dialog || !accepted || typeof dialog.showModal !== 'function') return;

    var text = document.getElementById('disclaimer-text');
    var hint = document.getElementById('disclaimer-hint');
    var accept = document.getElementById('disclaimer-accept');
    var hintText = hint.textContent;

    // The end has been reached when the text is scrolled to (nearly) the bottom, or when it was never long enough to scroll.
    function atEnd() {
        return text.scrollHeight - text.scrollTop - text.clientHeight <= 8;
    }

    function update() {
        var done = atEnd();
        accept.disabled = !done;
        hint.textContent = done ? '' : hintText;
    }

    text.addEventListener('scroll', update);
    window.addEventListener('resize', update);

    form.addEventListener('submit', function (e) {
        // Already accepted (the page came back with an error and the user had accepted before): send the form.
        if (accepted.value === 'true') return;

        e.preventDefault();
        text.scrollTop = 0;
        dialog.showModal();
        update();
        text.focus();
    });

    accept.addEventListener('click', function () {
        if (!atEnd()) return;
        accepted.value = 'true';
        dialog.close();
        form.requestSubmit();
    });

    document.getElementById('disclaimer-cancel').addEventListener('click', function () { dialog.close(); });
})();
