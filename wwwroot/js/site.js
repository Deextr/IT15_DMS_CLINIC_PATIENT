// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// ═══════════ Infield Labels – filled-state detection ═══════════
(function () {
    function refreshInfields(root) {
        (root || document).querySelectorAll('.infield').forEach(function (el) {
            var field = el.querySelector('.form-control, .form-select');
            if (field) {
                el.classList.toggle('filled', field.value.trim() !== '');
            }
        });
    }

    function ensurePlaceholders(root) {
        // Inputs inside .infield need a placeholder (even a space) so the
        // CSS :not(:placeholder-shown) selector can detect a filled state
        // without relying solely on JS class toggling.
        (root || document).querySelectorAll('.infield > .form-control').forEach(function (input) {
            if (!input.getAttribute('placeholder')) {
                input.setAttribute('placeholder', ' ');
            }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        // Inject placeholder attributes for CSS detection
        ensurePlaceholders();

        // Event delegation for user interaction
        document.addEventListener('input', function (e) {
            var infield = e.target.closest('.infield');
            if (infield) infield.classList.toggle('filled', e.target.value.trim() !== '');
        });
        document.addEventListener('change', function (e) {
            var infield = e.target.closest('.infield');
            if (infield) infield.classList.toggle('filled', e.target.value.trim() !== '');
        });
        // Also toggle on blur so label stays up after tabbing away
        document.addEventListener('focusout', function (e) {
            var infield = e.target.closest('.infield');
            if (infield) infield.classList.toggle('filled', e.target.value.trim() !== '');
        });

        // Initial check (for server-rendered values on edit pages)
        refreshInfields();

        // Re-check when Bootstrap modals finish showing (fields may be populated by JS)
        document.addEventListener('shown.bs.modal', function (e) {
            ensurePlaceholders(e.target);
            refreshInfields(e.target);
        });
    });

    // Expose globally so other scripts can call it after programmatic value changes
    window.refreshInfields = refreshInfields;
})();

// ═══════════ Password hint – show only when requirements aren't met ═══════════
(function () {
    var passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[\W_]).{8,}$/;

    function checkPasswordHint(input) {
        var hint = input.closest('.infield').querySelector('.password-hint');
        if (!hint) return;
        var val = input.value;
        // Show hint only when there's text that doesn't satisfy requirements
        if (val.length > 0 && !passwordRegex.test(val)) {
            hint.classList.add('visible');
        } else {
            hint.classList.remove('visible');
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.addEventListener('input', function (e) {
            if (e.target.hasAttribute('data-password-hint')) {
                checkPasswordHint(e.target);
            }
        });
        document.addEventListener('focusout', function (e) {
            if (e.target.hasAttribute('data-password-hint')) {
                checkPasswordHint(e.target);
            }
        });
    });
})();
