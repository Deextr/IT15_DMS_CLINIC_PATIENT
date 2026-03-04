using System;
using System.Linq;
using System.Threading.Tasks;
using DMS_CPMS.Data;
using DMS_CPMS.Data.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace DMS_CPMS.Services
{
    public interface IAuditLogService
    {
        /// <summary>
        /// Log an audit entry using the current authenticated user context.
        /// </summary>
        Task LogAsync(string action, int? documentId = null);

        /// <summary>
        /// Log an audit entry with an explicit user ID (for login/logout).
        /// </summary>
        Task LogAsync(string action, string userId, int? documentId = null);
    }

    public class AuditLogService : IAuditLogService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AuditLogService> _logger;

        public AuditLogService(
            ApplicationDbContext context,
            IHttpContextAccessor httpContextAccessor,
            UserManager<ApplicationUser> userManager,
            ILogger<AuditLogService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task LogAsync(string action, int? documentId = null)
        {
            try
            {
                string? userId = null;
                string? role = null;

                var httpContext = _httpContextAccessor.HttpContext;
                if (httpContext?.User?.Identity?.IsAuthenticated == true)
                {
                    var user = await _userManager.GetUserAsync(httpContext.User);
                    userId = user?.Id;
                    if (user != null)
                    {
                        var roles = await _userManager.GetRolesAsync(user);
                        role = roles.FirstOrDefault();
                    }
                }

                var auditLog = new AuditLog
                {
                    Action = TruncateString(action, 100),
                    DocumentID = documentId,
                    UserID = userId,
                    Role = role,
                    Timestamp = DateTime.Now
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log: {Action}", action);
            }
        }

        public async Task LogAsync(string action, string userId, int? documentId = null)
        {
            try
            {
                string? role = null;
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await _userManager.FindByIdAsync(userId);
                    if (user != null)
                    {
                        var roles = await _userManager.GetRolesAsync(user);
                        role = roles.FirstOrDefault();
                    }
                }

                var auditLog = new AuditLog
                {
                    Action = TruncateString(action, 100),
                    DocumentID = documentId,
                    UserID = string.IsNullOrEmpty(userId) ? null : userId,
                    Role = role,
                    Timestamp = DateTime.Now
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write audit log: {Action}", action);
            }
        }

        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
