using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using DMS_CPMS.Models.Reports;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DMS_CPMS.Services
{
    public interface IReportService
    {
        // ── Three structured report categories ──
        Task<PatientSummaryReport> GetPatientSummaryAsync(DateTime? from, DateTime? to);
        Task<DocumentActivityReport> GetDocumentActivityAsync(DateTime? from, DateTime? to, int page);
        Task<AuditLogsReportData> GetAuditLogsAsync(DateTime? from, DateTime? to, int page);

        // ── Export helpers (all rows, no pagination) ──
        Task<PatientSummaryReport> GetAllPatientSummaryDataAsync(DateTime? from, DateTime? to);
        Task<DocumentActivityReport> GetAllDocumentActivityDataAsync(DateTime? from, DateTime? to);
        Task<AuditLogsReportData> GetAllAuditLogsDataAsync(DateTime? from, DateTime? to);
    }

    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private const int PageSize = 8;

        public ReportService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // ═══════════════════════════════════════════════════════════
        //  1. PATIENT SUMMARY REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<PatientSummaryReport> GetPatientSummaryAsync(DateTime? from, DateTime? to)
        {
            return await BuildPatientSummaryReport(from, to);
        }

        public async Task<PatientSummaryReport> GetAllPatientSummaryDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildPatientSummaryReport(from, to, allRows: true);
        }

        private async Task<PatientSummaryReport> BuildPatientSummaryReport(DateTime? from, DateTime? to, bool allRows = false)
        {
            var today    = DateTime.Today;
            var tomorrow = today.AddDays(1);

            // Push scalar statistics to the DB
            var totalRegistered = await _context.Patients.CountAsync();
            var newDaily   = await _context.Patients.CountAsync(p => p.VisitedAt >= today   && p.VisitedAt < tomorrow);
            var newWeekly  = await _context.Patients.CountAsync(p => p.VisitedAt >= today.AddDays(-6));
            var newMonthly = await _context.Patients.CountAsync(p => p.VisitedAt >= today.AddMonths(-1).AddDays(1));

            // Age Distribution — load only BirthDate column; age maths stay in memory
            var birthDates = await _context.Patients.Select(p => p.BirthDate).ToListAsync();
            var ageGroups = birthDates
                .Select(bd =>
                {
                    var age = today.Year - bd.Year;
                    if (bd.Date > today.AddYears(-age)) age--;
                    return age;
                })
                .GroupBy(age => age switch
                {
                    < 1  => "Infant (< 1)",
                    < 13 => "Child (1-12)",
                    < 18 => "Adolescent (13-17)",
                    < 30 => "Young Adult (18-29)",
                    < 45 => "Adult (30-44)",
                    < 60 => "Middle-Aged (45-59)",
                    _    => "Senior (60+)"
                })
                .Select(g => new AgeDistributionRow
                {
                    AgeGroup = g.Key,
                    Count = g.Count(),
                    Percentage = totalRegistered > 0
                        ? $"{(g.Count() * 100.0 / totalRegistered):F1}%"
                        : "0.0%"
                })
                .OrderBy(r => r.AgeGroup switch
                {
                    "Infant (< 1)"        => 0,
                    "Child (1-12)"        => 1,
                    "Adolescent (13-17)"  => 2,
                    "Young Adult (18-29)" => 3,
                    "Adult (30-44)"       => 4,
                    "Middle-Aged (45-59)" => 5,
                    _                     => 6
                })
                .ToList();

            // Gender Distribution — push GroupBy/Count to DB; format percentages in memory
            var genderRaw = await _context.Patients
                .GroupBy(p => p.Gender)
                .Select(g => new { Gender = g.Key, Count = g.Count() })
                .ToListAsync();

            var genderGroups = genderRaw
                .GroupBy(g => string.IsNullOrWhiteSpace(g.Gender) ? "Not Specified" : g.Gender)
                .Select(grp => new GenderDistributionRow
                {
                    Gender = grp.Key,
                    Count = grp.Sum(x => x.Count),
                    Percentage = totalRegistered > 0
                        ? $"{(grp.Sum(x => x.Count) * 100.0 / totalRegistered):F1}%"
                        : "0.0%"
                })
                .OrderByDescending(r => r.Count)
                .ToList();

            // Recently Active — push filter / ordering / take to DB;
            // document count resolved as a correlated COUNT subquery in the projection
            var recentQuery = _context.Patients.AsQueryable();
            if (from.HasValue) recentQuery = recentQuery.Where(p => p.VisitedAt >= from.Value);
            if (to.HasValue)   recentQuery = recentQuery.Where(p => p.VisitedAt < to.Value.AddDays(1));
            var orderedRecentQuery = recentQuery.OrderByDescending(p => p.VisitedAt);

            var recentRaw = await (allRows ? orderedRecentQuery : orderedRecentQuery.Take(10))
                .Select(p => new
                {
                    p.PatientID,
                    p.FirstName,
                    p.LastName,
                    p.Gender,
                    p.VisitedAt,
                    DocumentCount = p.Documents.Count(d => !d.IsArchived)
                })
                .ToListAsync();

            var recentlyActiveList = recentRaw
                .Select(p => new RecentlyActivePatientRow
                {
                    PatientID      = p.PatientID,
                    PatientName    = p.FirstName + " " + p.LastName,
                    Gender         = p.Gender,
                    LastVisitDate  = p.VisitedAt.ToString("MMM dd, yyyy"),
                    TotalDocuments = p.DocumentCount
                })
                .ToList();

            return new PatientSummaryReport
            {
                TotalRegisteredPatients = totalRegistered,
                NewPatientsDaily        = newDaily,
                NewPatientsWeekly       = newWeekly,
                NewPatientsMonthly      = newMonthly,
                AgeDistribution         = ageGroups,
                GenderDistribution      = genderGroups,
                RecentlyActivePatients  = recentlyActiveList
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  2. DOCUMENT ACTIVITY REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<DocumentActivityReport> GetDocumentActivityAsync(DateTime? from, DateTime? to, int page)
        {
            return await BuildDocumentActivityReport(from, to, page);
        }

        public async Task<DocumentActivityReport> GetAllDocumentActivityDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildDocumentActivityReport(from, to, 1, allRows: true);
        }

        private async Task<DocumentActivityReport> BuildDocumentActivityReport(DateTime? from, DateTime? to, int page, bool allRows = false)
        {
            var today    = DateTime.Today;
            var tomorrow = today.AddDays(1);

            // Push scalar statistics to the DB
            var totalDocsUploaded = await _context.Documents.CountAsync(d => !d.IsArchived);
            var docsDaily   = await _context.Documents.CountAsync(d => !d.IsArchived && d.UploadDate >= today   && d.UploadDate < tomorrow);
            var docsWeekly  = await _context.Documents.CountAsync(d => !d.IsArchived && d.UploadDate >= today.AddDays(-6));
            var docsMonthly = await _context.Documents.CountAsync(d => !d.IsArchived && d.UploadDate >= today.AddMonths(-1).AddDays(1));

            // Base query for date-filtered non-archived documents (reused below)
            var filteredDocsBase = _context.Documents.Where(d => !d.IsArchived);
            if (from.HasValue) filteredDocsBase = filteredDocsBase.Where(d => d.UploadDate >= from.Value);
            if (to.HasValue)   filteredDocsBase = filteredDocsBase.Where(d => d.UploadDate < to.Value.AddDays(1));

            // Documents per Patient — count pushed as a correlated subquery in the projection
            var docsPerPatientQueryable = _context.Patients
                .Select(p => new
                {
                    p.PatientID,
                    p.FirstName,
                    p.LastName,
                    TotalDocuments = p.Documents.Count(d => !d.IsArchived
                        && (!from.HasValue || d.UploadDate >= from.Value)
                        && (!to.HasValue   || d.UploadDate < to.Value.AddDays(1)))
                });

            if (!allRows)
                docsPerPatientQueryable = docsPerPatientQueryable.Where(x => x.TotalDocuments > 0);

            var docsPerPatientRaw = await docsPerPatientQueryable
                .OrderByDescending(x => x.TotalDocuments)
                .ThenBy(x => x.LastName)
                .ToListAsync();

            // Distinct document types per patient — lightweight separate query
            var docTypesByPatient = (await filteredDocsBase
                .Select(d => new { d.PatientID, d.DocumentType })
                .Distinct()
                .ToListAsync())
                .GroupBy(x => x.PatientID)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(", ", g.Select(x => x.DocumentType)
                                           .Where(t => !string.IsNullOrEmpty(t))
                                           .OrderBy(t => t)));

            var totalPatientCount = docsPerPatientRaw.Count;
            var totalPages = (int)Math.Ceiling(totalPatientCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var docsPerPatientPage = (allRows
                ? docsPerPatientRaw
                : docsPerPatientRaw.Skip((page - 1) * PageSize).Take(PageSize))
                .Select(x => new DocumentsPerPatientRow
                {
                    PatientID      = x.PatientID,
                    PatientName    = x.FirstName + " " + x.LastName,
                    TotalDocuments = x.TotalDocuments,
                    DocumentTypes  = docTypesByPatient.TryGetValue(x.PatientID, out var types) && !string.IsNullOrEmpty(types)
                        ? types
                        : "None"
                })
                .ToList();

            // Documents per Category — GroupBy pushed to DB; percentages computed in memory
            var categoryRaw = await filteredDocsBase
                .GroupBy(d => d.DocumentType)
                .Select(g => new { DocumentType = g.Key, Count = g.Count() })
                .ToListAsync();

            var filteredTotal = categoryRaw.Sum(g => g.Count);
            var docsPerCategory = categoryRaw
                .GroupBy(g => string.IsNullOrWhiteSpace(g.DocumentType) ? "Uncategorized" : g.DocumentType)
                .Select(grp => new DocumentsPerCategoryRow
                {
                    DocumentType  = grp.Key,
                    DocumentCount = grp.Sum(g => g.Count),
                    Percentage    = filteredTotal > 0
                        ? $"{(grp.Sum(g => g.Count) * 100.0 / filteredTotal):F1}%"
                        : "0.0%"
                })
                .OrderByDescending(r => r.DocumentCount)
                .ToList();

            // Most Frequently Updated — ordering and take pushed to DB
            var frequentlyUpdatedQueryable = filteredDocsBase
                .Select(d => new
                {
                    d.DocumentID,
                    d.DocumentTitle,
                    d.DocumentType,
                    d.UploadDate,
                    PatientFirstName = d.Patient != null ? d.Patient.FirstName : null,
                    PatientLastName  = d.Patient != null ? d.Patient.LastName  : null,
                    VersionCount     = d.Versions.Count,
                    LastVersionDate  = d.Versions.Max(v => (DateTime?)v.CreatedDate)
                })
                .Where(d => d.VersionCount > 0)
                .OrderByDescending(d => d.VersionCount);

            var frequentlyUpdated = (allRows
                ? await frequentlyUpdatedQueryable.ToListAsync()
                : await frequentlyUpdatedQueryable.Take(10).ToListAsync())
                .Select(d => new FrequentlyUpdatedDocumentRow
                {
                    DocumentID    = d.DocumentID,
                    DocumentTitle = d.DocumentTitle,
                    DocumentType  = d.DocumentType,
                    PatientName   = d.PatientFirstName != null
                        ? d.PatientFirstName + " " + d.PatientLastName
                        : "—",
                    VersionCount  = d.VersionCount,
                    LastUpdated   = (d.LastVersionDate ?? d.UploadDate).ToString("MMM dd, yyyy")
                })
                .ToList();

            // Version Count per Document — ordering and take pushed to DB
            var versionCountQueryable = filteredDocsBase
                .Select(d => new
                {
                    d.DocumentID,
                    d.DocumentTitle,
                    d.DocumentType,
                    PatientFirstName = d.Patient != null ? d.Patient.FirstName : null,
                    PatientLastName  = d.Patient != null ? d.Patient.LastName  : null,
                    VersionCount     = d.Versions.Count
                })
                .OrderByDescending(d => d.VersionCount);

            var versionCounts = (allRows
                ? await versionCountQueryable.ToListAsync()
                : await versionCountQueryable.Take(10).ToListAsync())
                .Select(d => new VersionCountRow
                {
                    DocumentID    = d.DocumentID,
                    DocumentTitle = d.DocumentTitle,
                    DocumentType  = d.DocumentType,
                    PatientName   = d.PatientFirstName != null
                        ? d.PatientFirstName + " " + d.PatientLastName
                        : "—",
                    VersionCount  = d.VersionCount
                })
                .ToList();

            return new DocumentActivityReport
            {
                TotalDocumentsUploaded     = totalDocsUploaded,
                DocumentsUploadedDaily     = docsDaily,
                DocumentsUploadedWeekly    = docsWeekly,
                DocumentsUploadedMonthly   = docsMonthly,
                DocumentsPerPatient        = docsPerPatientPage,
                DocumentsPerCategory       = docsPerCategory,
                FrequentlyUpdatedDocuments = frequentlyUpdated,
                VersionCounts              = versionCounts,
                PageNumber                 = page,
                PageSize                   = PageSize,
                TotalPages                 = totalPages,
                TotalCount                 = totalPatientCount
            };
        }

        // ═══════════════════════════════════════════════════════════
        //  3. AUDIT LOGS REPORT
        // ═══════════════════════════════════════════════════════════

        public async Task<AuditLogsReportData> GetAuditLogsAsync(DateTime? from, DateTime? to, int page)
        {
            return await BuildAuditLogsReport(from, to, page);
        }

        public async Task<AuditLogsReportData> GetAllAuditLogsDataAsync(DateTime? from, DateTime? to)
        {
            return await BuildAuditLogsReport(from, to, 1, allRows: true);
        }

        private async Task<AuditLogsReportData> BuildAuditLogsReport(DateTime? from, DateTime? to, int page, bool allRows = false)
        {
            var query = _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Document)
                .AsQueryable();

            if (from.HasValue) query = query.Where(a => a.Timestamp >= from.Value);
            if (to.HasValue) query = query.Where(a => a.Timestamp < to.Value.AddDays(1));

            var totalCount = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)PageSize);
            page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

            var orderedQuery = query.OrderByDescending(a => a.Timestamp);

            var logs = allRows
                ? await orderedQuery.Select(a => new AuditLogReportRow
                {
                    LogID = a.LogID,
                    UserFullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                    Role = a.Role ?? "Unknown",
                    Action = a.Action,
                    DocumentTitle = a.Document != null ? a.Document.DocumentTitle : "—",
                    Timestamp = a.Timestamp.ToString("MMM dd, yyyy hh:mm tt")
                }).ToListAsync()
                : await orderedQuery
                    .Skip((page - 1) * PageSize).Take(PageSize)
                    .Select(a => new AuditLogReportRow
                    {
                        LogID = a.LogID,
                        UserFullName = a.User != null ? (a.User.FirstName + " " + a.User.LastName) : "Unknown",
                        Role = a.Role ?? "Unknown",
                        Action = a.Action,
                        DocumentTitle = a.Document != null ? a.Document.DocumentTitle : "—",
                        Timestamp = a.Timestamp.ToString("MMM dd, yyyy hh:mm tt")
                    }).ToListAsync();

            return new AuditLogsReportData
            {
                Rows = logs,
                TotalEntries = totalCount,
                PageNumber = page,
                PageSize = PageSize,
                TotalPages = totalPages,
                TotalCount = totalCount
            };
        }
    }
}
