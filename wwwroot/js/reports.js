/**
 * Reports Module — JavaScript
 * Handles: report dropdown auto-submit, date preset toggling.
 * PDF and Excel exports are handled server-side via direct download links.
 */
(function () {
    'use strict';

    var reportSelector = document.getElementById('reportSelector');
    var presetSelect = document.getElementById('datePresetSelector');
    var customDateFrom = document.getElementById('customDateFrom');
    var customDateTo = document.getElementById('customDateTo');
    var form = document.getElementById('reportFilterForm');

    // ── Report dropdown change → auto-submit ──
    if (reportSelector) {
        reportSelector.addEventListener('change', function () {
            if (form) form.submit();
        });
    }

    // ── Date preset toggle ──
    if (presetSelect) {
        presetSelect.addEventListener('change', function () {
            var isCustom = this.value === 'Custom';
            if (customDateFrom) customDateFrom.style.display = isCustom ? 'block' : 'none';
            if (customDateTo) customDateTo.style.display = isCustom ? 'block' : 'none';
            // Auto-submit when changing preset (except Custom — user needs to pick dates first)
            if (!isCustom && form) {
                form.submit();
            }
        });
    }
})();
