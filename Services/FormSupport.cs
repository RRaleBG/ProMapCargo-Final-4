using System.Text.Json;
using ProMapCargo.Api.Data;
using ProMapCargo.Api.Models;

namespace ProMapCargo.Api.Services;

/// <summary>Audit trail entry for a change made through a web form (shown on /audit).</summary>
public sealed class AuditWriter(ProMapCargoDbContext db, ICurrentUserContext current, IHttpContextAccessor http)
{
    /// <summary>Adds the entry to the context; it is saved with the change it describes.</summary>
    public void Add(string action, string entityType, Guid entityId, object? newValue = null, object? oldValue = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            CompanyId = current.CompanyId ?? Guid.Empty,
            UserId = current.UserId,
            CreatedAt = DateTimeOffset.UtcNow,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            OldValueJson = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValueJson = newValue is null ? null : JsonSerializer.Serialize(newValue),
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Source = "Web",
        });
    }
}

/// <summary>
/// Receipts and scanned documents: stored on disk under Storage:UploadsPath (default
/// App_Data/uploads), one folder per company; the database keeps the reference.
/// </summary>
public sealed class FileStorageService(ProMapCargoDbContext db, ICurrentUserContext current, IConfiguration configuration, IWebHostEnvironment environment)
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
    };

    private string Root => configuration["Storage:UploadsPath"] is { Length: > 0 } path
        ? path
        : Path.Combine(environment.ContentRootPath, "App_Data", "uploads");

    /// <summary>Saves the upload and adds its record to the context (saved by the caller).</summary>
    public async Task<StoredFile?> SaveAsync(IFormFile? file, Guid companyId, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedTypes.TryGetValue(extension, out var contentType))
        {
            throw new ArgumentException("Dozvoljeni su samo PDF, JPG i PNG fajlovi.");
        }

        if (file.Length > MaxBytes)
        {
            throw new ArgumentException("Fajl može imati najviše 10 MB.");
        }

        var id = Guid.NewGuid();
        var relative = Path.Combine(companyId.ToString(), $"{id}{extension.ToLowerInvariant()}");
        var fullPath = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        var stored = new StoredFile
        {
            Id = id,
            CompanyId = companyId,
            FileName = Path.GetFileName(file.FileName),
            ContentType = contentType,
            SizeBytes = file.Length,
            StoragePath = relative,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = current.UserId,
        };
        db.StoredFiles.Add(stored);
        return stored;
    }

    public string FullPath(StoredFile file) => Path.Combine(Root, file.StoragePath);
}
