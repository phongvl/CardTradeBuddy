// The eye button on password fields: press it to see what you typed, press again to hide it.
(function () {
    document.querySelectorAll('.password-wrap').forEach(function (wrap) {
        var input = wrap.querySelector('input');
        var button = wrap.querySelector('.eye-toggle');
        if (!input || !button) return;

        button.hidden = false; // without JavaScript the button stays hidden

        function show(visible) {
            input.type = visible ? 'text' : 'password';
            button.setAttribute('aria-pressed', visible ? 'true' : 'false');
            button.setAttribute('aria-label', visible ? button.dataset.hideLabel : button.dataset.showLabel);
        }

        button.addEventListener('click', function () {
            show(input.type === 'password');
        });

        // Never leave a password readable when the form is sent, or when the browser brings the page back from its cache.
        if (input.form) input.form.addEventListener('submit', function () { show(false); });
        window.addEventListener('pageshow', function () { show(false); });
    });
})();