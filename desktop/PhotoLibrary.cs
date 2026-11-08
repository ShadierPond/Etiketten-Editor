using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace LabelEditor.Desktop;

public sealed class PhotoLibrary
{
    private readonly string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShadierPond",
        "FuniturenEditor",
        "Photos");

    public PhotoLibrary()
    {
        Directory.CreateDirectory(directory);
    }

    public bool NormalizeCatalog(CatalogData data, string? sourceDirectory = null, bool strict = false)
    {
        return NormalizeCatalog(data, sourceDirectory, strict, skipUnsupportedPhotos: false, out _);
    }

    public int NormalizeCatalogForImport(CatalogData data, string? sourceDirectory)
    {
        NormalizeCatalog(data, sourceDirectory, strict: true, skipUnsupportedPhotos: true, out var skippedPhotos);
        return skippedPhotos;
    }

    private bool NormalizeCatalog(
        CatalogData data,
        string? sourceDirectory,
        bool strict,
        bool skipUnsupportedPhotos,
        out int skippedPhotos)
    {
        var changed = false;
        var skippedPhotoCount = 0;
        skippedPhotos = 0;
        var existingPhotos = data.Library.ToList();
        data.PhotoNames ??= [];
        data.PhotoAddedAt ??= [];
        var existingPhotoNames = data.PhotoNames;
        var existingPhotoAddedAt = data.PhotoAddedAt;
        var keyMap = new Dictionary<string, string>(StringComparer.Ordinal);
        data.Library.Clear();
        data.PhotoNames = [];
        data.PhotoAddedAt = [];

        void SetPhotoMetadata(string key, string? name, DateTimeOffset? addedAt = null)
        {
            if (!data.PhotoNames.ContainsKey(key))
            {
                data.PhotoNames[key] = string.IsNullOrWhiteSpace(name) ? key : Path.GetFileName(name);
                changed = true;
            }

            if (!data.PhotoAddedAt.ContainsKey(key))
            {
                var path = Path.Combine(directory, FindPhotoFile(key));
                data.PhotoAddedAt[key] = addedAt ?? new DateTimeOffset(File.GetCreationTimeUtc(path));
                changed = true;
            }
        }

        void NormalizePhotoReference(string? value, Action<string> assign)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (keyMap.TryGetValue(value, out var mappedKey))
            {
                changed |= !string.Equals(value, mappedKey, StringComparison.Ordinal);
                assign(mappedKey);
                return;
            }

            try
            {
                var newKey = ImportPhotoValue(value, sourceDirectory);
                assign(newKey);
                data.Library[newKey] = FindPhotoFile(newKey);
                SetPhotoMetadata(newKey, GetPhotoName(value, newKey));
                changed = true;
            }
            catch (UnsupportedPhotoFormatException) when (skipUnsupportedPhotos)
            {
                assign("");
                skippedPhotoCount++;
                changed = true;
            }
            catch (Exception ex) when (!strict && ex is (IOException or FormatException or InvalidDataException or ArgumentException))
            {
                AppDiagnostics.Exception("photo.normalize_reference", ex);
            }
        }

        foreach (var (oldKey, value) in existingPhotos)
        {
            try
            {
                var newKey = ImportPhotoValue(value, sourceDirectory);
                keyMap[oldKey] = newKey;
                data.Library[newKey] = FindPhotoFile(newKey);
                existingPhotoNames.TryGetValue(oldKey, out var photoName);
                existingPhotoAddedAt.TryGetValue(oldKey, out var addedAt);
                SetPhotoMetadata(newKey, photoName ?? GetPhotoName(value, newKey),
                    existingPhotoAddedAt.ContainsKey(oldKey) ? addedAt : null);
                changed |= !string.Equals(oldKey, newKey, StringComparison.Ordinal) ||
                           !string.Equals(value, data.Library[newKey], StringComparison.Ordinal);
            }
            catch (UnsupportedPhotoFormatException) when (skipUnsupportedPhotos)
            {
                keyMap[oldKey] = "";
                skippedPhotoCount++;
                changed = true;
            }
            catch (Exception ex) when (!strict && ex is (IOException or FormatException or InvalidDataException or ArgumentException))
            {
                data.Library[oldKey] = value;
            }
        }

        foreach (var (catalog, item) in data.Catalogs
                     .SelectMany(catalog => catalog.Lists
                         .SelectMany(page => page.Items.Select(item => (catalog, item)))))
        {
            NormalizePhotoReference(item.Photo, normalized => item.Photo = normalized);
            foreach (var field in catalog.Layout.Fields.Where(field => field.Kind == "Photo"))
            {
                if (item.FieldValues.TryGetValue(field.Id, out var photoValue))
                    NormalizePhotoReference(photoValue, normalized => item.FieldValues[field.Id] = normalized);
            }
        }
        foreach (var definition in data.Catalogs)
            NormalizePhotoReference(definition.Layout.LogoPhotoKey, normalized => definition.Layout.LogoPhotoKey = normalized);
        NormalizePhotoReference(data.LabelLayout.LogoPhotoKey,
            normalized => data.LabelLayout.LogoPhotoKey = normalized);

        skippedPhotos = skippedPhotoCount;
        return changed;
    }

    public string AddPhoto(string sourcePath, CatalogData data)
    {
        var key = ImportPhotoValue(sourcePath, null);
        data.Library[key] = FindPhotoFile(key);
        data.PhotoNames ??= [];
        data.PhotoAddedAt ??= [];
        if (!data.PhotoNames.ContainsKey(key))
            data.PhotoNames[key] = Path.GetFileName(sourcePath);
        if (!data.PhotoAddedAt.ContainsKey(key))
            data.PhotoAddedAt[key] = DateTimeOffset.UtcNow;
        return key;
    }

    public void Clear()
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            File.Delete(file);
    }

    public int RemovePhoto(CatalogData data, string key)
    {
        var path = GetPhotoPath(data, key);
        if (path is not null)
            File.Delete(path);

        var references = CatalogData.GetPhotoReferences(data, key);
        foreach (var item in data.Catalogs.SelectMany(catalog => catalog.Lists).SelectMany(page => page.Items))
        {
            item.FieldValues ??= [];
            if (string.Equals(item.Photo, key, StringComparison.Ordinal))
                item.Photo = "";
            foreach (var fieldId in item.FieldValues
                         .Where(field => string.Equals(field.Value, key, StringComparison.Ordinal))
                         .Select(field => field.Key)
                         .ToList())
                item.FieldValues[fieldId] = "";
        }
        foreach (var layout in data.Catalogs.Select(catalog => catalog.Layout).Append(data.LabelLayout))
        {
            if (string.Equals(layout.LogoPhotoKey, key, StringComparison.Ordinal))
                layout.LogoPhotoKey = "";
        }
        data.Library.Remove(key);
        data.PhotoNames.Remove(key);
        data.PhotoAddedAt.Remove(key);
        return references.Count;
    }

    public string? GetPhotoPath(CatalogData data, string key)
    {
        if (!data.Library.TryGetValue(key, out var fileName) ||
            string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
            return null;

        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        var root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    public CatalogData CreateExportCopy(CatalogData data)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<CatalogData>(
            System.Text.Json.JsonSerializer.Serialize(data)) ?? throw new InvalidDataException("Der Katalog konnte nicht kopiert werden.");
        foreach (var key in copy.Library.Keys.ToList())
        {
            var path = GetPhotoPath(data, key);
            if (path is not null)
                copy.Library[key] = ToDataUri(path);
        }
        return copy;
    }

    public string ToDataUri(string path)
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
                var header when header.StartsWith("data:image/svg+xml", StringComparison.Ordinal) => ".svg",
                _ => throw new UnsupportedPhotoFormatException("Das Bildformat wird nicht unterstützt.")
            };
            var bytes = Convert.FromBase64String(value[(comma + 1)..]);
            return extension == ".svg"
                ? SavePhoto(SvgImageConverter.ConvertToPng(bytes), ".png")
                : SavePhoto(bytes, extension);
        }

        var sourcePath = Path.IsPathRooted(value)
            ? value
            : Path.Combine(sourceDirectory ?? "", value);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Das Foto „{value}“ wurde nicht gefunden.");
        var fileExtension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (fileExtension is not (".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff" or ".svg"))
            throw new UnsupportedPhotoFormatException($"Das Fotoformat „{fileExtension}“ wird nicht unterstützt.");
        var sourceBytes = File.ReadAllBytes(sourcePath);
        return fileExtension == ".svg"
            ? SavePhoto(SvgImageConverter.ConvertToPng(sourceBytes), ".png")
            : SavePhoto(sourceBytes, fileExtension);
    }

    private string SavePhoto(byte[] bytes, string extension)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();

        var key = "photo-" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var existing = Directory.EnumerateFiles(directory, key + ".*").FirstOrDefault();
        if (existing is not null)
            return key;

        var destination = Path.Combine(directory, key + extension);
        File.WriteAllBytes(destination, bytes);
        return key;
    }

    private string FindPhotoFile(string key)
    {
        var file = Directory.EnumerateFiles(directory, key + ".*").FirstOrDefault();
        return file is null ? throw new FileNotFoundException($"Das Foto „{key}“ fehlt in der lokalen Mediathek.") : Path.GetFileName(file);
    }

    private static string GetPhotoName(string value, string key)
    {
        if (value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return key;
        var name = Path.GetFileName(value);
        return string.IsNullOrWhiteSpace(name) ? key : name;
    }
}

internal sealed class UnsupportedPhotoFormatException(string message) : IOException(message);
