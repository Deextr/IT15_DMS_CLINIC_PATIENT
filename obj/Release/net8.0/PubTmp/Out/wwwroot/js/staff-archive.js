/* ═══════════════════════════════════════════════════════════
   Staff Document Archive Module – JavaScript
   ═══════════════════════════════════════════════════════════ */
(function () {
    'use strict';

    // ─── Restore modal: populate hidden fields ───
    var restoreModal = document.getElementById('restoreModal');
    if (restoreModal) {
        restoreModal.addEventListener('show.bs.modal', function (event) {
            var btn = event.relatedTarget;
            document.getElementById('restoreArchiveId').value = btn.getAttribute('data-archive-id');
            document.getElementById('restoreDocTitle').textContent = btn.getAttribute('data-doc-title');
        });
    }

    // ─── Auto-dismiss alerts after 5s ───
    document.querySelectorAll('.alert-dismissible').forEach(function (alert) {
        setTimeout(function () {
            var bsAlert = bootstrap.Alert.getOrCreateInstance(alert);
            if (bsAlert) bsAlert.close();
        }, 5000);
    });
})();
