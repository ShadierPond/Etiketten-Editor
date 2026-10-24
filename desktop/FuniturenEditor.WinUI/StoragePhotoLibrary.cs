using System.Security.Cryptography;
using FuniturenEditor.Desktop;

namespace FuniturenEditor_WinUI;

/// <summary>
/// WinUI-compatible counterpart to the WPF photo library. Images are stored
/// once by content hash in the same per-user library folder.
/// </summary>
internal sealed class StoragePhotoLibrary
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShadierPond",
        "FuniturenEditor",
        "Photos");

    public StoragePhotoLibrary() => Directory.CreateDirectory(_directory);

    public string AddPhoto(string sourcePath, CatalogData catalog)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff"))
            throw new InvalidDataException($"Das Fotoformat „{extension}“ wird nicht unterstützt.");
        var key = SavePhoto(File.ReadAllBytes(sourcePath), extension);
        catalog.Library[key] = FindPhotoFile(key);
        return key;
    }

    public IReadOnlyList<PhotoLibraryEntry> GetAvailablePhotos() =>
        Directory.EnumerateFiles(_directory)
            .Where(path => Path.GetExtension(path).ToLowerInvariant() is
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff")
            .Select(path => new PhotoLibraryEntry(
                Path.GetFileNameWithoutExtension(path),
                Path.GetFileName(path),
                path))
            .OrderBy(photo => photo.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public bool NormalizeCatalog(CatalogData catalog, string? sourceDirectory = null, bool strict = false)
    {
        return NormalizeCatalog(catalog, sourceDirectory, strict, skipUnsupportedPhotos: false, out _);
    }

    public int NormalizeCatalogForImport(CatalogData catalog, string? sourceDirectory)
    {
        NormalizeCatalog(catalog, sourceDirectory, strict: true, skipUnsupportedPhotos: true, out var skippedPhotos);
        return skippedPhotos;
    }

    private bool NormalizeCatalog(
        CatalogData catalog,
        string? sourceDirectory,
        bool strict,
        bool skipUnsupportedPhotos,
        out int skippedPhotos)
    {
        var changed = false;
        skippedPhotos = 0;
        var mappings = catalog.Library.ToList();
        var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        catalog.Library.Clear();

        foreach (var (key, value) in mappings)
        {
            try
            {
                var newKey = ImportPhotoValue(value, sourceDirectory);
                keyMap[key] = newKey;
                catalog.Library[newKey] = FindPhotoFile(newKey);
                changed |= !string.Equals(key, newKey, StringComparison.Ordinal) ||
                           !string.Equals(value, catalog.Library[newKey], StringComparison.Ordinal);
            }
            catch (UnsupportedStoragePhotoFormatException) when (skipUnsupportedPhotos)
            {
                keyMap[key] = "";
                skippedPhotos++;
                changed = true;
            }
            catch (Exception ex) when (!strict && IsRecoverableImageError(ex))
            {
                catalog.Library[key] = value;
            }
        }

        foreach (var item in catalog.Pages.SelectMany(page => page.Items))
        {
            if (string.IsNullOrWhiteSpace(item.Photo))
                continue;
            if (keyMap.TryGetValue(item.Photo, out var mapped))
            {
                changed |= !string.Equals(item.Photo, mapped, StringComparison.Ordinal);
                item.Photo = mapped;
                continue;
            }
            try
            {
                item.Photo = ImportPhotoValue(item.Photo, sourceDirectory);
                catalog.Library[item.Photo] = FindPhotoFile(item.Photo);
                changed = true;
            }
            catch (UnsupportedStoragePhotoFormatException) when (skipUnsupportedPhotos)
            {
                item.Photo = "";
                skippedPhotos++;
                changed = true;
            }
            catch (Exception ex) when (!strict && IsRecoverableImageError(ex))
            {
                // Keep the source value so the user can reconnect a missing image later.
            }
        }
        return changed;
    }

    public string? GetPhotoPath(CatalogData catalog, string key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            !catalog.Library.TryGetValue(key, out var fileName) ||
            string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            return null;
        var path = Path.GetFullPath(Path.Combine(_directory, fileName));
        var root = Path.GetFullPath(_directory) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    public int RemovePhoto(CatalogData catalog, string key)
    {
        var references = CatalogData.GetPhotoReferences(catalog, key);
        var path = GetPhotoPath(catalog, key) ??
                   Directory.EnumerateFiles(_directory).FirstOrDefault(candidate =>
                       string.Equals(Path.GetFileNameWithoutExtension(candidate), key, StringComparison.Ordinal));
        if (path is not null)
            File.Delete(path);

        foreach (var item in catalog.Pages.SelectMany(page => page.Items))
        {
            if (string.Equals(item.Photo, key, StringComparison.Ordinal))
                item.Photo = "";
        }
        catalog.Library.Remove(key);
        return references.Count;
    }

    public CatalogData CreateExportCopy(CatalogData catalog)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<CatalogData>(
            System.Text.Json.JsonSerializer.Serialize(catalog))
            ?? throw new InvalidDataException("Der Katalog konnte nicht kopiert werden.");
        foreach (var key in copy.Library.Keys.ToList())
        {
            var path = GetPhotoPath(catalog, key);
            if (path is not null)
                copy.Library[key] = ToDataUri(path);
        }
        return copy;
    }

    private string ImportPhotoValue(string value, string? sourceDirectory)
    {
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = value.IndexOf(',');
            if (comma < 0 || !value[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ein eingebettetes Foto enthält keine gültigen Base64-Daten.");
            var extension = value[..comma].ToLowerInvariant() switch
            {
                var header when header.StartsWith("data:image/jpeg", StringComparison.Ordinal) => ".jpg",
                var header when header.StartsWith("data:image/png", StringComparison.Ordinal) => ".png",
                var header when header.StartsWith("data:image/gif", StringComparison.Ordinal) => ".gif",
                var header when header.StartsWith("data:image/bmp", StringComparison.Ordinal) => ".bmp",
                var header when header.StartsWith("data:image/tiff", StringComparison.Ordinal) => ".tiff",
                _ => throw new UnsupportedStoragePhotoFormatException("Das Bildformat wird nicht unterstützt.")
            };
            return SavePhoto(Convert.FromBase64String(value[(comma + 1)..]), extension);
        }

        var candidates = new List<string>();
        if (Path.IsPathRooted(value))
            candidates.Add(value);
        else
        {
            candidates.Add(Path.Combine(_directory, value));
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
                candidates.Add(Path.Combine(sourceDirectory, value));
        }
        var sourcePath = candidates.FirstOrDefault(File.Exists);
        if (sourcePath is null)
            throw new FileNotFoundException($"Das Foto „{value}“ wurde nicht gefunden.");
        var extensionName = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extensionName is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff"))
            throw new UnsupportedStoragePhotoFormatException($"Das Fotoformat „{extensionName}“ wird nicht unterstützt.");
        return SavePhoto(File.ReadAllBytes(sourcePath), extensionName);
    }

    private string SavePhoto(byte[] bytes, string extension)
    {
        if (bytes.Length == 0)
            throw new InvalidDataException("Die Fotodatei ist leer.");
        var key = "photo-" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (Directory.EnumerateFiles(_directory, key + ".*").Any())
            return key;
        File.WriteAllBytes(Path.Combine(_directory, key + extension), bytes);
        return key;
    }

    private string FindPhotoFile(string key)
    {
        var file = Directory.EnumerateFiles(_directory, key + ".*").FirstOrDefault();
        return file is null
            ? throw new FileNotFoundException($"Das Foto „{key}“ fehlt in der lokalen Mediathek.")
            : Path.GetFileName(file);
    }

    private static bool IsRecoverableImageError(Exception ex) =>
        ex is IOException or FormatException or InvalidDataException or ArgumentException;

    private static string ToDataUri(string path)
    {
        var mimeType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png"
        };
        return $"data:{mimeType};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }
}

internal sealed record PhotoLibraryEntry(string Key, string FileName, string FullPath);
internal sealed class UnsupportedStoragePhotoFormatException(string message) : IOException(message);
