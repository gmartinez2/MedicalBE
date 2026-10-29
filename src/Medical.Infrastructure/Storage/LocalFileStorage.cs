using System.Text.RegularExpressions;
using Medical.Application.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Medical.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Absolute path, or a path relative to the content root. Defaults to <c>uploads</c>.</summary>
    public string ImagesPath { get; set; } = "uploads";
}

/// <summary>
/// Stores files flat under a single root as <c>{guid}{ext}</c>. Every path coming back from the
/// database is re-validated against a strict pattern and resolved inside the root, so a tampered
/// <c>StoragePath</c> can never escape the storage directory.
/// </summary>
public sealed partial class LocalFileStorage : IFileStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment env, ILogger<LocalFileStorage> logger)
    {
        _logger = logger;
        var configured = string.IsNullOrWhiteSpace(options.Value.ImagesPath) ? "uploads" : options.Value.ImagesPath;
        _root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        var ext = extension.ToLowerInvariant();
        if (!ExtensionPattern().IsMatch(ext)) throw new ArgumentException("Invalid file extension.", nameof(extension));

        var relative = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Resolve(relative);
        await using var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return relative;
    }

    public Stream? OpenRead(string relativePath)
    {
        var fullPath = Resolve(relativePath);
        return File.Exists(fullPath)
            ? new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
    }

    public void Delete(string relativePath)
    {
        try
        {
            var fullPath = Resolve(relativePath);
            if (File.Exists(fullPath)) File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not delete stored file {Path}", relativePath);
        }
    }

    private string Resolve(string relativePath)
    {
        if (!StoredNamePattern().IsMatch(relativePath))
            throw new ArgumentException("Invalid storage path.", nameof(relativePath));

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Storage path escapes the storage root.", nameof(relativePath));
        return fullPath;
    }

    [GeneratedRegex(@"^\.[a-z0-9]{1,5}$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex(@"^[a-f0-9]{32}\.[a-z0-9]{1,5}$")]
    private static partial Regex StoredNamePattern();
}
