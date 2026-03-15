// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// ═══════════ Sidebar Toggle – Desktop collapse + Mobile off-canvas ═══════════
// The header hamburger (#mobileMenuToggle) serves as the single toggle for
// all breakpoints. On desktop it collapses/expands; on mobile it opens the
// off-canvas drawer.
(function () {
    var MOBILE_BP = 992; // matches CSS @media (max-width: 991px)

    function isMobile() { return window.innerWidth < MOBILE_BP; }

    document.addEventListener('DOMContentLoaded', function () {
        var sidebar     = document.getElementById('sidebar');
        var toggleBtn   = document.getElementById('mobileMenuToggle');
        var sidebarToggleBtn = document.getElementById('sidebarToggle');

        if (!sidebar) return;

        // Create overlay element for mobile backdrop
        var overlay = document.createElement('div');
        overlay.className = 'sidebar-overlay';
        overlay.id = 'sidebarOverlay';
        document.body.appendChild(overlay);

        // Restore desktop collapsed state
        if (!isMobile() && localStorage.getItem('sidebarCollapsed') === 'true') {
            sidebar.classList.add('no-transition', 'collapsed');
            requestAnimationFrame(function () {
                sidebar.classList.remove('no-transition');
            });
        }

        // Desktop collapse helper
        function toggleDesktopSidebar() {
            var willCollapse = !sidebar.classList.contains('collapsed');
            sidebar.classList.toggle('collapsed', willCollapse);
            localStorage.setItem('sidebarCollapsed', willCollapse);

            // Animate menu text fade
            var texts = sidebar.querySelectorAll('.sidebar-link-text, .sidebar-brand-text');
            if (willCollapse) {
                texts.forEach(function(el) { el.style.opacity = '0'; });
            } else {
                setTimeout(function() {
                    texts.forEach(function(el) { el.style.opacity = '1'; });
                }, 120);
            }
        }

        // Header hamburger – desktop collapse OR mobile off-canvas
        if (toggleBtn) {
            toggleBtn.addEventListener('click', function () {
                if (isMobile()) {
                    // Mobile: open / close off-canvas drawer
                    if (sidebar.classList.contains('mobile-open')) {
                        closeMobileSidebar();
                    } else {
                        sidebar.classList.add('mobile-open');
                        overlay.classList.add('active');
                        toggleBtn.setAttribute('aria-expanded', 'true');
                        document.body.style.overflow = 'hidden';
                    }
                } else {
                    toggleDesktopSidebar();
                }
            });
        }

        // In-sidebar toggle button (if visible)
        if (sidebarToggleBtn) {
            sidebarToggleBtn.addEventListener('click', function () {
                if (!isMobile()) {
                    toggleDesktopSidebar();
                }
            });
        }

        // Tooltip on hover when collapsed
        sidebar.querySelectorAll('.sidebar-link').forEach(function(link) {
            link.addEventListener('mouseenter', function() {
                if (sidebar.classList.contains('collapsed')) {
                    var text = link.querySelector('.sidebar-link-text');
                    if (text) link.setAttribute('title', text.textContent.trim());
                } else {
                    link.removeAttribute('title');
                }
            });
        });

        // Close sidebar when clicking overlay
        overlay.addEventListener('click', closeMobileSidebar);

        // Close sidebar when pressing Escape
        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && sidebar.classList.contains('mobile-open')) {
                closeMobileSidebar();
            }
        });

        // Close sidebar when a nav link is clicked (mobile)
        sidebar.addEventListener('click', function (e) {
            if (isMobile() && e.target.closest('.sidebar-link')) {
                closeMobileSidebar();
            }
        });

        function closeMobileSidebar() {
            sidebar.classList.remove('mobile-open');
            overlay.classList.remove('active');
            if (toggleBtn) toggleBtn.setAttribute('aria-expanded', 'false');
            document.body.style.overflow = '';
        }

        // On resize: clean up states (e.g. going from mobile → desktop)
        var resizeTimer;
        window.addEventListener('resize', function () {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(function () {
                if (!isMobile()) {
                    closeMobileSidebar();
                }
            }, 150);
        });
    });
})();

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
