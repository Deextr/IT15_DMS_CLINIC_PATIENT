/* ═══════════════════════════════════════════════════════════
   Archive & Retention Module – JavaScript
   ═══════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    // ─── Initialize tooltips ───
    document.querySelectorAll('[data-bs-toggle="tooltip"]').forEach(function(el) {
        new bootstrap.Tooltip(el);
    });

    // ─── Modal open functions ───
    window.openRestoreModal = function(btn) {
        document.getElementById('restoreArchiveId').value = btn.getAttribute('data-archive-id');
        document.getElementById('restoreDocTitle').textContent = btn.getAttribute('data-doc-title');
        var modal = new bootstrap.Modal(document.getElementById('restoreModal'));
        modal.show();
    };

    window.openPermanentDeleteModal = function(btn) {
        document.getElementById('permDeleteArchiveId').value = btn.getAttribute('data-archive-id');
        document.getElementById('permDeleteDocTitle').textContent = btn.getAttribute('data-doc-title');
        var modal = new bootstrap.Modal(document.getElementById('permanentDeleteModal'));
        modal.show();
    };

    window.openEditPolicyModal = function(btn) {
        var totalMonths = parseInt(btn.getAttribute('data-duration'), 10) || 0;
        document.getElementById('editPolicyId').value = btn.getAttribute('data-policy-id');
        document.getElementById('editPolicyModuleName').value = btn.getAttribute('data-module-name');
        document.getElementById('editPolicyAutoAction').value = btn.getAttribute('data-auto-action');
        document.getElementById('editPolicyEnabled').checked = btn.getAttribute('data-is-enabled') === 'true';

        // Convert total months into value + unit
        if (totalMonths > 0 && totalMonths % 12 === 0) {
            document.getElementById('editPolicyDurationValue').value = totalMonths / 12;
            document.getElementById('editPolicyDurationUnit').value = 'years';
        } else {
            document.getElementById('editPolicyDurationValue').value = totalMonths;
            document.getElementById('editPolicyDurationUnit').value = 'months';
        }
        
        var modal = new bootstrap.Modal(document.getElementById('editPolicyModal'));
        modal.show();
    };

    // ─── Duration computation helpers ───
    function computeDurationMonths(valueId, unitId) {
        var val = parseInt(document.getElementById(valueId).value, 10) || 0;
        var unit = document.getElementById(unitId).value;
        return unit === 'years' ? val * 12 : val;
    }

    // Add Policy form: compute hidden RetentionDurationMonths before submit
    var addPolicyForm = document.getElementById('addPolicyForm');
    if (addPolicyForm) {
        addPolicyForm.addEventListener('submit', function () {
            document.getElementById('addPolicyDurationHidden').value =
                computeDurationMonths('addPolicyDurationValue', 'addPolicyDurationUnit');
            // Sync checkbox state to the hidden IsEnabled field
            var cb = document.getElementById('addPolicyEnabled');
            var hidden = addPolicyForm.querySelector('input[type="hidden"][name="IsEnabled"]');
            if (cb && hidden) hidden.value = cb.checked ? 'true' : 'false';
        });
    }

    // Edit Policy form: compute hidden RetentionDurationMonths before submit
    var editPolicyForm = document.getElementById('editPolicyForm');
    if (editPolicyForm) {
        editPolicyForm.addEventListener('submit', function () {
            document.getElementById('editPolicyDurationHidden').value =
                computeDurationMonths('editPolicyDurationValue', 'editPolicyDurationUnit');
            // Sync checkbox state to the hidden IsEnabled field
            var cb = document.getElementById('editPolicyEnabled');
            var hidden = editPolicyForm.querySelector('input[type="hidden"][name="IsEnabled"]');
            if (cb && hidden) hidden.value = cb.checked ? 'true' : 'false';
        });
    }

    // ─── Policy pagination helper ───
    window.goToPolicyPage = function (page) {
        var params = new URLSearchParams(window.location.search);
        params.set('policyPage', page);
        // Preserve the active tab
        window.location.href = window.location.pathname + '?' + params.toString() + '#retentionPolicyPane';
    };

    // Activate retention tab if hash is present
    if (window.location.hash === '#retentionPolicyPane') {
        var tabTrigger = document.querySelector('[data-bs-target="#retentionPolicyPane"]');
        if (tabTrigger) {
            var tab = new bootstrap.Tab(tabTrigger);
            tab.show();
        }
    }

    // ─── Auto-dismiss alerts after 5s ───
    document.querySelectorAll('.alert-dismissible').forEach(function (alert) {
        setTimeout(function () {
            var bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
            if (bsAlert) bsAlert.close();
        }, 5000);
    });

    // ─── Helper: escape HTML ───
    function escapeHtml(text) {
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }
})();
