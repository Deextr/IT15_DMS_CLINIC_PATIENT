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
        });
    }

    // Edit Policy form: compute hidden RetentionDurationMonths before submit
    var editPolicyForm = document.getElementById('editPolicyForm');
    if (editPolicyForm) {
        editPolicyForm.addEventListener('submit', function () {
            document.getElementById('editPolicyDurationHidden').value =
                computeDurationMonths('editPolicyDurationValue', 'editPolicyDurationUnit');
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

    // ─── Archive Document: live-search for active documents ───
    var searchInput = document.getElementById('archiveDocSearch');
    var resultsDiv = document.getElementById('archiveDocResults');
    var hiddenId = document.getElementById('archiveDocId');
    var selectedDiv = document.getElementById('archiveDocSelected');
    var selectedText = document.getElementById('archiveDocSelectedText');
    var clearBtn = document.getElementById('archiveDocClear');
    var submitBtn = document.getElementById('archiveDocSubmit');
    var debounceTimer = null;

    if (searchInput) {
        searchInput.addEventListener('input', function () {
            clearTimeout(debounceTimer);
            var term = searchInput.value.trim();
            if (term.length < 2) {
                resultsDiv.classList.remove('show');
                resultsDiv.innerHTML = '';
                return;
            }
            debounceTimer = setTimeout(function () {
                fetch('/ArchiveRetention/GetActiveDocuments?term=' + encodeURIComponent(term))
                    .then(function (res) { return res.json(); })
                    .then(function (data) {
                        if (data.length === 0) {
                            resultsDiv.innerHTML = '<div class="archive-doc-item text-muted">No documents found</div>';
                            resultsDiv.classList.add('show');
                            return;
                        }
                        var html = '';
                        data.forEach(function (d) {
                            html += '<div class="archive-doc-item" data-id="' + d.documentID + '" data-title="' + escapeHtml(d.documentTitle) + '">';
                            html += '<div class="doc-title">' + escapeHtml(d.documentTitle) + '</div>';
                            html += '<div class="doc-meta">' + escapeHtml(d.documentType) + ' &middot; Patient: ' + escapeHtml(d.patientName) + '</div>';
                            html += '</div>';
                        });
                        resultsDiv.innerHTML = html;
                        resultsDiv.classList.add('show');

                        // Attach click handlers
                        resultsDiv.querySelectorAll('.archive-doc-item[data-id]').forEach(function (item) {
                            item.addEventListener('click', function () {
                                selectDocument(item.getAttribute('data-id'), item.getAttribute('data-title'));
                            });
                        });
                    })
                    .catch(function () {
                        resultsDiv.innerHTML = '<div class="archive-doc-item text-danger">Error loading documents</div>';
                        resultsDiv.classList.add('show');
                    });
            }, 300);
        });

        // Hide dropdown when clicking outside
        document.addEventListener('click', function (e) {
            if (!searchInput.contains(e.target) && !resultsDiv.contains(e.target)) {
                resultsDiv.classList.remove('show');
            }
        });
    }

    function selectDocument(id, title) {
        hiddenId.value = id;
        selectedText.textContent = title;
        selectedDiv.style.display = 'flex';
        searchInput.value = '';
        resultsDiv.classList.remove('show');
        resultsDiv.innerHTML = '';
        submitBtn.disabled = false;
    }

    if (clearBtn) {
        clearBtn.addEventListener('click', function () {
            hiddenId.value = '';
            selectedDiv.style.display = 'none';
            selectedText.textContent = '';
            submitBtn.disabled = true;
        });
    }

    // Reset archive modal on close
    var archiveModal = document.getElementById('archiveDocumentModal');
    if (archiveModal) {
        archiveModal.addEventListener('hidden.bs.modal', function () {
            hiddenId.value = '';
            selectedDiv.style.display = 'none';
            selectedText.textContent = '';
            submitBtn.disabled = true;
            searchInput.value = '';
            resultsDiv.classList.remove('show');
            resultsDiv.innerHTML = '';
            var textarea = archiveModal.querySelector('textarea[name="ArchiveReason"]');
            if (textarea) textarea.value = '';
        });
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
