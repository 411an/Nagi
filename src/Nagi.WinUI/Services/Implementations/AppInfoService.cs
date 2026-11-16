using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Nagi.Core.Services.Abstractions;

namespace Nagi.WinUI.Services.Implementations;

public class AppInfoService : IAppInfoService
{
    private readonly ILogger<AppInfoService> _logger;
    private readonly SemaphoreSlim _languageCacheLock = new(1, 1);
    private IReadOnlyList<string>? _cachedAvailableLanguages;

    public AppInfoService(ILogger<AppInfoService> logger)
    {
        _logger = logger;
    }

    public string GetAppName()
    {
        return Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
               ?? "Nagi";
    }

    public string GetAppVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var path = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(path))
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(path).ProductVersion;
            if (!string.IsNullOrEmpty(fileVersion))
            {
                var plus = fileVersion.IndexOf('+');
                return plus >= 0 ? fileVersion[..plus] : fileVersion;
            }
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<string>> GetAvailableLanguagesAsync()
    {
        if (_cachedAvailableLanguages != null)
            return _cachedAvailableLanguages;

        await _languageCacheLock.WaitAsync();
        try
        {
            if (_cachedAvailableLanguages != null)
                return _cachedAvailableLanguages;

            var satelliteName = Assembly.GetEntryAssembly()?.GetName().Name + ".resources.dll";
            var installFolder = AppContext.BaseDirectory;
            _logger.LogDebug("Language scan: installFolder={Path}, satellite={Satellite}", installFolder, satelliteName);

            var results = new List<string>();
            if (!Directory.Exists(installFolder))
            {
                _cachedAvailableLanguages = results;
                return _cachedAvailableLanguages;
            }

            foreach (var directory in Directory.EnumerateDirectories(installFolder))
            {
                var name = Path.GetFileName(directory);
                try
                {
                    _ = new CultureInfo(name);
                }
                catch (CultureNotFoundException)
                {
                    continue;
                }

                var satellitePath = Path.Combine(directory, satelliteName);
                if (!File.Exists(satellitePath)) continue;
                results.Add(name);
            }

            _logger.LogDebug("Language scan: completed with {Count} languages", results.Count);
            _cachedAvailableLanguages = results;
            return _cachedAvailableLanguages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate available languages from the install folder.");
            return Array.Empty<string>();
        }
        finally
        {
            _languageCacheLock.Release();
        }
    }

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        await GetAvailableLanguagesAsync();
    }
}
