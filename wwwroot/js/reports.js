/**
 * Reports Module — JavaScript
 * Handles: report dropdown auto-submit, date preset toggling, PDF export via jsPDF
 *
 * PDF export fetches the FULL filtered dataset from the server-side
 * ExportPdfData endpoint — no DOM scraping, so all pages are included.
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

    // ── PDF Export ──
    var btnPdf = document.getElementById('btnExportPdf');
    if (btnPdf) {
        btnPdf.addEventListener('click', function () {
            exportPdf();
        });
    }

    /**
     * Initiates the PDF export by:
     * 1. Reading current filter values from the form controls.
     * 2. Fetching the FULL (unpaginated) dataset from the backend ExportPdfData endpoint.
     * 3. Generating the PDF from the returned JSON — never from the DOM.
     */
    function exportPdf() {
        if (typeof window.jspdf === 'undefined' || typeof window.jspdf.jsPDF === 'undefined') {
            alert('PDF library is not available. Please refresh the page and try again.');
            return;
        }

        var btn = document.getElementById('btnExportPdf');
        var baseUrl = btn ? btn.dataset.pdfBase : null;
        if (!baseUrl) {
            alert('PDF export configuration is missing. Please refresh the page.');
            return;
        }

        // Read the current filter state from the visible form controls
        var reportSel = document.getElementById('reportSelector');
        var presetSel = document.getElementById('datePresetSelector');
        var dateFromEl = document.getElementById('dateFromInput');
        var dateToEl = document.getElementById('dateToInput');

        var params = new URLSearchParams({
            report: reportSel ? reportSel.value : 'All',
            datePreset: presetSel ? presetSel.value : 'AllTime',
            dateFrom: (dateFromEl && dateFromEl.value) ? dateFromEl.value : '',
            dateTo: (dateToEl && dateToEl.value) ? dateToEl.value : ''
        });

        // Cache-busting parameter to prevent stale responses
        params.set('_t', Date.now().toString());
        var fetchUrl = baseUrl + '?' + params.toString();

        // Show a loading state on the button
        var originalHtml = btn.innerHTML;
        btn.disabled = true;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> Exporting...';

        fetch(fetchUrl, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (response) {
                if (!response.ok) {
                    throw new Error('Server returned ' + response.status + ' ' + response.statusText);
                }
                return response.json();
            })
            .then(function (data) {
                generatePdf(data);
            })
            .catch(function (err) {
                alert('PDF export failed: ' + err.message);
            })
            .finally(function () {
                btn.disabled = false;
                btn.innerHTML = originalHtml;
            });
    }

    /**
     * Builds and saves the PDF document from the server-provided data structure.
     * @param {Object} data - PdfExportData JSON with a `sections` array, each section
     *   containing { reportTitle, title, headers[], rows[][] }.
     */
    function generatePdf(data) {
        var sections = data.sections;
        if (!sections || sections.length === 0) {
            alert('No report data to export.');
            return;
        }

        var jsPDF = window.jspdf.jsPDF;
        var doc = new jsPDF({ orientation: 'landscape', unit: 'mm', format: 'a4' });
        var now = new Date();
        var pageWidth = doc.internal.pageSize.getWidth();
        var pageHeight = doc.internal.pageSize.getHeight();

        var reportSel = document.getElementById('reportSelector');
        var mainTitle = reportSel
            ? reportSel.options[reportSel.selectedIndex].text
            : 'DMS CPMS Reports';

        var generatedLabel = 'Generated: ' + now.toLocaleDateString('en-US', {
            year: 'numeric', month: 'long', day: 'numeric',
            hour: '2-digit', minute: '2-digit'
        });

        sections.forEach(function (section, index) {
            if (index > 0) {
                doc.addPage();
            }

            // ── Section header ──
            doc.setFontSize(16);
            doc.setTextColor(0, 109, 119);
            doc.text('DMS CPMS \u2014 ' + (section.reportTitle || mainTitle), 14, 15);

            doc.setFontSize(10);
            doc.setTextColor(80, 80, 80);
            doc.text(section.title || 'Report Data', 14, 23);

            doc.setFontSize(8);
            doc.setTextColor(130, 130, 130);
            doc.text(generatedLabel, pageWidth - 14, 15, { align: 'right' });

            // ── Table data ──
            var headers = section.headers || [];
            var bodyRows = (section.rows && section.rows.length > 0)
                ? section.rows
                : [headers.length > 0
                    ? headers.map(function (_, i) { return i === 0 ? 'No data available' : ''; })
                    : ['No data available']];

            doc.autoTable({
                startY: 30,
                head: [headers],
                body: bodyRows,
                styles: {
                    fontSize: 8,
                    cellPadding: 2.5,
                    overflow: 'linebreak',
                    lineColor: [220, 220, 220],
                    lineWidth: 0.1
                },
                headStyles: {
                    fillColor: [0, 109, 119],
                    textColor: 255,
                    fontStyle: 'bold',
                    fontSize: 8
                },
                alternateRowStyles: { fillColor: [245, 248, 250] },
                margin: { left: 14, right: 14 },
                didDrawPage: function () {
                    // Page number footer on every page jsPDF draws for this table
                    doc.setFontSize(8);
                    doc.setTextColor(150);
                    doc.text(
                        'Page ' + doc.internal.getNumberOfPages(),
                        pageWidth - 14,
                        pageHeight - 10,
                        { align: 'right' }
                    );
                }
            });
        });

        var filename = mainTitle.replace(/[^a-zA-Z0-9]/g, '_') + '_' + now.toISOString().slice(0, 10) + '.pdf';
        doc.save(filename);
    }
})();
