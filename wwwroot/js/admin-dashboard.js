/* ═══════════════════════════════════════════════════════════════════════════
   Admin / SuperAdmin Dashboard  —  Interactive Charts & Date Filtering
   Uses Chart.js + AJAX calls to /{area}/Dashboard/DashboardData
   ═══════════════════════════════════════════════════════════════════════════ */

(function () {
    'use strict';

    // ── Color palette (Ocean Breeze) ────────────────────────────────────
    const COLORS = {
        primary: '#006d77',
        primaryDark: '#005660',
        accent: '#83c5be',
        accentLight: '#b8e0d8',
        success: '#15803d',
        muted: '#6b7280',
        bg: '#edf6f9',
        surface: '#ffffff',
        genderPalette: ['#006d77', '#83c5be', '#b8e0d8', '#005660'],
        agePalette: ['#b8e0d8', '#83c5be', '#006d77', '#005660', '#003840'],
        docTypePalette: ['#006d77', '#005660', '#83c5be', '#b8e0d8', '#48a9a6', '#004e57', '#6bb5ad']
    };

    // ── Chart Defaults ──────────────────────────────────────────────────
    Chart.defaults.font.family = "system-ui, -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif";
    Chart.defaults.font.size = 12;
    Chart.defaults.color = COLORS.muted;

    const commonScaleOpts = {
        y: {
            beginAtZero: true,
            grid: { color: 'rgba(0,0,0,0.05)', drawBorder: false },
            ticks: { precision: 0 }
        },
        x: {
            grid: { display: false }
        }
    };

    // ── Chart instances (for later update/destroy) ──────────────────────
    let chartGender, chartAgeGroup, chartNewPatients, chartDocTrend, chartDocTypes;

    // ── Active date range tracker ────────────────────────────────────────
    let currentRange = 'all';

    // ── Range → human-readable label map ────────────────────────────────
    function getRangeDisplayText(range) {
        switch (range) {
            case 'all':     return { archive: 'Archived All Time',          patients: 'New Patients All Time' };
            case 'today':   return { archive: 'Archived Today',              patients: 'New Patients Today' };
            case 'weekly':  return { archive: 'Archived Weekly',             patients: 'New Patients Weekly' };
            case 'monthly': return { archive: 'Archived Monthly',            patients: 'New Patients Monthly' };
            case 'yearly':  return { archive: 'Archived Yearly',             patients: 'New Patients Yearly' };
            case 'custom':  return { archive: 'Archived (Custom Range)',     patients: 'New Patients (Custom Range)' };
            default:        return { archive: 'Archived',                    patients: 'New Patients' };
        }
    }

    function updateDynamicLabels(range) {
        const labels = getRangeDisplayText(range);
        setText('#kpiTotalArchiveDocsLabel', labels.archive);
        setText('#newPatientsChartTitle', labels.patients);
    }

    // ── Utility: number formatting ──────────────────────────────────────
    function fmt(n) { return (n || 0).toLocaleString(); }

    // ═══════════ INITIALIZATION ═══════════════════════════════════════════
    function init() {
        updateDynamicLabels('all');
        renderCharts(initialData);
        updateTitles(initialData);
        bindFilters();
    }

    // ═══════════ RENDER ALL CHARTS ══════════════════════════════════════
    function renderCharts(data) {
        renderGenderChart(data.patientsByGender || data.PatientsByGender || []);
        renderAgeGroupChart(data.patientsByAgeGroup || data.PatientsByAgeGroup || []);
        renderNewPatientsChart(data.newPatientsPerMonth || data.NewPatientsPerMonth || []);
        renderDocTrendChart(data.documentUploadTrend || data.DocumentUploadTrend || []);
        renderDocTypesChart(data.documentsByType || data.DocumentsByType || []);
    }

    // ── 1. Gender Pie Chart ─────────────────────────────────────────────
    function renderGenderChart(data) {
        const ctx = document.getElementById('chartGender');
        if (!ctx) return;
        if (chartGender) chartGender.destroy();

        chartGender = new Chart(ctx, {
            type: 'doughnut',
            data: {
                labels: data.map(d => d.label || d.Label),
                datasets: [{
                    data: data.map(d => d.count ?? d.Count),
                    backgroundColor: COLORS.genderPalette.slice(0, data.length),
                    borderWidth: 0,
                    hoverOffset: 6
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                cutout: '60%',
                plugins: {
                    legend: {
                        position: 'bottom',
                        labels: { padding: 14, usePointStyle: true, pointStyleWidth: 10, font: { size: 11.5 } }
                    }
                }
            }
        });
    }

    // ── 2. Age Group Bar Chart ──────────────────────────────────────────
    function renderAgeGroupChart(data) {
        const ctx = document.getElementById('chartAgeGroup');
        if (!ctx) return;
        if (chartAgeGroup) chartAgeGroup.destroy();

        chartAgeGroup = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: data.map(d => d.label || d.Label),
                datasets: [{
                    label: 'Patients',
                    data: data.map(d => d.count ?? d.Count),
                    backgroundColor: COLORS.agePalette.slice(0, data.length),
                    borderRadius: 6,
                    maxBarThickness: 36
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: commonScaleOpts
            }
        });
    }

    // ── 3. New Patients Line Chart ──────────────────────────────────────
    function renderNewPatientsChart(data) {
        const ctx = document.getElementById('chartNewPatients');
        if (!ctx) return;
        if (chartNewPatients) chartNewPatients.destroy();

        chartNewPatients = new Chart(ctx, {
            type: 'line',
            data: {
                labels: data.map(d => d.label || d.Label),
                datasets: [{
                    label: 'New Patients',
                    data: data.map(d => d.count ?? d.Count),
                    borderColor: COLORS.primary,
                    backgroundColor: 'rgba(0, 109, 119, 0.08)',
                    tension: 0.4,
                    fill: true,
                    pointRadius: 3,
                    pointBackgroundColor: COLORS.primary,
                    pointHoverRadius: 5
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: commonScaleOpts
            }
        });
    }

    // ── 4. Document Upload Trend ────────────────────────────────────────
    function renderDocTrendChart(data) {
        const ctx = document.getElementById('chartDocTrend');
        if (!ctx) return;
        if (chartDocTrend) chartDocTrend.destroy();

        chartDocTrend = new Chart(ctx, {
            type: 'line',
            data: {
                labels: data.map(d => d.label || d.Label),
                datasets: [{
                    label: 'Documents',
                    data: data.map(d => d.count ?? d.Count),
                    borderColor: COLORS.primary,
                    backgroundColor: 'rgba(0, 109, 119, 0.06)',
                    tension: 0.35,
                    fill: true,
                    pointRadius: 3,
                    pointBackgroundColor: COLORS.primary,
                    pointHoverRadius: 5,
                    borderWidth: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: { legend: { display: false } },
                scales: commonScaleOpts
            }
        });
    }

    // ── 5. Documents by Type ────────────────────────────────────────────
    function renderDocTypesChart(data) {
        const ctx = document.getElementById('chartDocTypes');
        if (!ctx) return;
        if (chartDocTypes) chartDocTypes.destroy();

        chartDocTypes = new Chart(ctx, {
            type: 'bar',
            data: {
                labels: data.map(d => d.label || d.Label),
                datasets: [{
                    label: 'Documents',
                    data: data.map(d => d.count ?? d.Count),
                    backgroundColor: COLORS.docTypePalette.slice(0, data.length),
                    borderRadius: 6,
                    maxBarThickness: 36
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                indexAxis: data.length > 5 ? 'y' : 'x',
                plugins: { legend: { display: false } },
                scales: data.length > 5
                    ? { x: { beginAtZero: true, grid: { color: 'rgba(0,0,0,0.05)' }, ticks: { precision: 0 } }, y: { grid: { display: false } } }
                    : commonScaleOpts
            }
        });
    }

    // ═══════════ DATE FILTER BINDING ════════════════════════════════════
    function bindFilters() {
        const filterBtns = document.querySelectorAll('.dash-filter-btn');
        const customFields = document.getElementById('customDateFields');
        const btnCustom = document.getElementById('btnCustomRange');
        const applyBtn = document.getElementById('applyCustomDate');

        filterBtns.forEach(btn => {
            btn.addEventListener('click', function () {
                const range = this.dataset.range;

                if (range === 'custom') {
                    // Toggle custom date fields inline
                    const isVisible = customFields.style.display !== 'none';
                    customFields.style.display = isVisible ? 'none' : 'flex';
                    return;
                }

                // Deactivate all, activate clicked
                filterBtns.forEach(b => b.classList.remove('active'));
                this.classList.add('active');
                customFields.style.display = 'none';

                currentRange = range;
                updateDynamicLabels(range);
                fetchDashboardData(range);
            });
        });

        if (applyBtn) {
            applyBtn.addEventListener('click', function () {
                const startDate = document.getElementById('customStartDate').value;
                const endDate = document.getElementById('customEndDate').value;
                if (!startDate || !endDate) return;

                // Validate: start date must not be after end date
                if (new Date(startDate) > new Date(endDate)) return;

                filterBtns.forEach(b => b.classList.remove('active'));
                if (btnCustom) btnCustom.classList.add('active');
                customFields.style.display = 'none';

                currentRange = 'custom';
                updateDynamicLabels('custom');
                fetchDashboardData('custom', startDate, endDate);
            });
        }
    }

    // ═══════════ FETCH DATA VIA AJAX ═══════════════════════════════════
    function fetchDashboardData(range, startDate, endDate) {
        // Show loading state on KPIs
        document.querySelectorAll('.dash-kpi-number').forEach(el => el.classList.add('loading'));

        let url = `/${dashArea}/Dashboard/DashboardData?range=${encodeURIComponent(range)}`;
        if (range === 'custom' && startDate && endDate) {
            url += `&startDate=${encodeURIComponent(startDate)}&endDate=${encodeURIComponent(endDate)}`;
        }

        fetch(url)
            .then(res => {
                if (!res.ok) throw new Error('Network error');
                return res.json();
            })
            .then(data => {
                updateKPIs(data);
                updateCharts(data);
                updateRecentActivity(data.recentActivities || data.RecentActivities || []);
                updateMostUpdated(data.mostUpdatedDocuments || data.MostUpdatedDocuments || []);
                updateTitles(data);
            })
            .catch(err => {
                console.error('Dashboard fetch error:', err);
            })
            .finally(() => {
                document.querySelectorAll('.dash-kpi-number').forEach(el => el.classList.remove('loading'));
            });
    }

    // ═══════════ UPDATE FUNCTIONS ══════════════════════════════════════

    function updateKPIs(data) {
        setText('#kpiTotalPatients', fmt(data.totalPatients ?? data.TotalPatients));
        setText('#kpiTotalDocs', fmt(data.totalDocuments ?? data.TotalDocuments));
        setText('#kpiTotalArchiveDocs', fmt(data.totalArchiveDocuments ?? data.TotalArchiveDocuments));
        setText('#kpiActiveStaff', fmt(data.activeStaff ?? data.ActiveStaff));
        setText('#kpiDocVersions', fmt(data.totalDocumentVersions ?? data.TotalDocumentVersions));
    }

    function updateCharts(data) {
        renderCharts(data);
    }

    function updateTitles(data) {
        setText('#docTrendTitle', data.documentTrendTitle || data.DocumentTrendTitle || 'Documents Uploaded');
    }

    function updateRecentActivity(activities) {
        const container = document.getElementById('recentActivityContainer');
        if (!container) return;

        if (!activities || activities.length === 0) {
            container.innerHTML = `
                <div class="dash-empty-state">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" style="width:40px;height:40px;color:var(--color-text-muted);opacity:0.4;">
                        <circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/>
                    </svg>
                    <p>No recent activity for this period</p>
                </div>`;
            return;
        }

        let html = '<ul class="dash-activity-list">';
        activities.forEach(a => {
            const desc = a.description || a.Description || '';
            const timeAgo = a.timeAgo || a.TimeAgo || '';
            const actionType = a.actionType || a.ActionType || 'default';
            html += `
                <li class="dash-activity-item">
                    <div class="dash-activity-dot dash-dot-${escapeHtml(actionType)}"></div>
                    <div class="dash-activity-content">
                        <p class="dash-activity-text">${escapeHtml(desc)}</p>
                        <span class="dash-activity-time">${escapeHtml(timeAgo)}</span>
                    </div>
                </li>`;
        });
        html += '</ul>';
        container.innerHTML = html;
    }

    function updateMostUpdated(documents) {
        const container = document.getElementById('mostUpdatedContainer');
        if (!container) return;

        if (!documents || documents.length === 0) {
            container.innerHTML = '<div class="dash-empty-state"><p>No document versions found for this period</p></div>';
            return;
        }

        let html = '<div class="table-responsive"><table class="table dash-table mb-0"><thead><tr>';
        html += '<th>Document</th><th>Patient</th><th class="text-center">Versions</th><th>Last Updated</th>';
        html += '</tr></thead><tbody>';

        documents.forEach(doc => {
            const title = doc.documentTitle || doc.DocumentTitle || '';
            const patient = doc.patientName || doc.PatientName || '';
            const versions = doc.versionCount ?? doc.VersionCount ?? 0;
            const lastUpdated = doc.lastUpdated || doc.LastUpdated || '';
            const dateStr = lastUpdated ? new Date(lastUpdated).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) : '';

            html += `<tr>
                <td class="fw-medium">${escapeHtml(title)}</td>
                <td class="text-muted">${escapeHtml(patient)}</td>
                <td class="text-center"><span class="dash-version-badge">${versions}</span></td>
                <td class="text-muted">${escapeHtml(dateStr)}</td>
            </tr>`;
        });

        html += '</tbody></table></div>';
        container.innerHTML = html;
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    function setText(selector, value) {
        const el = document.querySelector(selector);
        if (el) el.textContent = value;
    }

    function escapeHtml(str) {
        if (!str) return '';
        const div = document.createElement('div');
        div.appendChild(document.createTextNode(str));
        return div.innerHTML;
    }

    // ── Start ───────────────────────────────────────────────────────────
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
