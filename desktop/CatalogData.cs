using System.Text.Json;
using System.Text.Json.Serialization;

namespace FuniturenEditor.Desktop;

public sealed class CatalogData
{
    public int SchemaVersion { get; set; } = 3;
    public List<CatalogPage> Pages { get; set; } = [];
    public Dictionary<string, string> Library { get; set; } = [];
    public List<EmployeePage> EmployeePages { get; set; } = [];
    public EmployeeLayout EmployeeLayout { get; set; } = new();
    public FurniturePrintLayout FurniturePrintLayout { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }

    public static CatalogData CreateDefault() => new()
    {
        Pages = [CatalogPage.Create("Kategorie 1")],
        EmployeePages = [new EmployeePage { Name = "Filiale 1" }]
    };

    public static IReadOnlyList<PhotoReference> GetPhotoReferences(CatalogData catalog, string key) =>
        catalog.Pages
            .SelectMany(page => page.Items.Select((item, index) =>
            {
                var photoKeys = (item.FieldValues ?? [])
                    .Where(fieldValue => catalog.FurniturePrintLayout.Fields.Any(field =>
                        field.Kind == "Photo" && field.Id == fieldValue.Key))
                    .Select(fieldValue => fieldValue.Value)
                    .Append(item.Photo)
                    .Where(photoKey => !string.IsNullOrWhiteSpace(photoKey))
                    .Distinct(StringComparer.Ordinal);
                return photoKeys.Contains(key, StringComparer.Ordinal)
                    ? new PhotoReference(page.Name, item.Title, index + 1, key)
                    : null;
            }))
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .ToList();

    public void Normalize()
    {
        SchemaVersion = Math.Max(SchemaVersion, 3);
        Pages ??= [];
        EmployeePages ??= [];
        FurniturePrintLayout ??= new FurniturePrintLayout();
        FurniturePrintLayout.Normalize();
        for (var index = Pages.Count - 1; index >= 0; index--)
        {
            if (Pages[index] is null)
                Pages.RemoveAt(index);
        }
        if (Pages.Count == 0)
            Pages.Add(CatalogPage.Create("Kategorie 1"));
        foreach (var page in Pages)
        {
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "Kategorie" : page.Name;
            page.Items ??= [];
            for (var index = page.Items.Count - 1; index >= 0; index--)
            {
                if (page.Items[index] is null)
                    page.Items.RemoveAt(index);
            }
            while (page.Items.Count < FurniturePrintLayout.TileCount)
                page.Items.Add(new CatalogItem());
            if (page.Items.Count > FurniturePrintLayout.TileCount)
                page.Items = page.Items.Take(FurniturePrintLayout.TileCount).ToList();
            foreach (var item in page.Items)
            {
                item.Title ??= "";
                item.Beco ??= "";
                item.Ref ??= "";
                item.Photo ??= "";
                item.Price ??= "";
                item.Ek ??= "";
                item.SalesPrice ??= "";
                item.RecommendedPrice ??= "";
                item.FieldValues ??= [];
                foreach (var field in FurniturePrintLayout.Fields)
                {
                    if (item.FieldValues.ContainsKey(field.Id))
                        continue;
                    var legacyValue = field.Source switch
                    {
                        "Title" => item.Title,
                        "Beco" => item.Beco,
                        "Ref" => item.Ref,
                        "Photo" => item.Photo,
                        "Price" => item.Price,
                        "SalesPrice" => item.SalesPrice,
                        "Ek" => item.Ek,
                        "RecommendedPrice" => item.RecommendedPrice,
                        _ => ""
                    };
                    item.FieldValues[field.Id] = legacyValue ?? "";
                }
            }
        }

        for (var index = EmployeePages.Count - 1; index >= 0; index--)
        {
            if (EmployeePages[index] is null)
                EmployeePages.RemoveAt(index);
        }
        if (EmployeePages.Count == 0)
            EmployeePages.Add(new EmployeePage { Name = "Filiale 1" });
        foreach (var page in EmployeePages)
        {
            page.Name = string.IsNullOrWhiteSpace(page.Name) ? "Filiale" : page.Name;
            page.Employees ??= [];
            for (var index = page.Employees.Count - 1; index >= 0; index--)
            {
                if (page.Employees[index] is null)
                    page.Employees.RemoveAt(index);
            }
            foreach (var employee in page.Employees)
            {
                employee.Name ??= "";
                employee.Number ??= "";
                employee.Code ??= "";
            }
        }

        Library ??= [];
        EmployeeLayout ??= new EmployeeLayout();
    }
}

public sealed class CatalogPage
{
    public string Name { get; set; } = "Kategorie";
    public List<CatalogItem> Items { get; set; } = [];

    public static CatalogPage Create(string name) => new()
    {
        Name = name,
        Items = Enumerable.Range(0, 15).Select(_ => new CatalogItem()).ToList()
    };
}

public sealed class CatalogItem
{
    public string Title { get; set; } = "";
    public string Beco { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Photo { get; set; } = "";
    public string Price { get; set; } = "";
    public string Ek { get; set; } = "";
    public string SalesPrice { get; set; } = "";
    public string RecommendedPrice { get; set; } = "";
    public Dictionary<string, string> FieldValues { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed record PhotoReference(string Category, string Title, int Position, string PhotoKey);

public sealed class EmployeePage
{
    public string Name { get; set; } = "Filiale";
    public List<Employee> Employees { get; set; } = [];
}

public sealed class Employee
{
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public string Code { get; set; } = "";
}

public sealed class EmployeeLayout
{
    public double Width { get; set; } = 70;
    public double Height { get; set; } = 17.5;
    public string QrPosition { get; set; } = "right";
    public double NameSize { get; set; } = 8;
    public double NumberSize { get; set; } = 5.5;
    public bool NameBold { get; set; } = true;
}

public sealed class FurniturePrintLayout
{
    public double CellWidthCm { get; set; } = 3.5;
    public double CellHeightCm { get; set; } = 3.5;
    public string PaperFormat { get; set; } = "A4";
    public string Orientation { get; set; } = "Landscape";
    public double HorizontalGapCm { get; set; } = 0.2;
    public double VerticalGapCm { get; set; } = 0.2;
    public int Columns { get; set; } = 5;
    public int TileCount { get; set; } = 15;
    public string MissingBecoText { get; set; } = "OROVIVO";
    public QrCodeSettings QrCode { get; set; } = new();
    public List<PrintElementLayout> Fields { get; set; } = [];
    public PrintElementLayout Photo { get; set; } = new() { X = 0.15, Y = 0.15, Width = 0.8, Height = 0.8, FontSize = 8 };
    public PrintElementLayout Qr { get; set; } = new() { X = 1.1, Y = 0.15, Width = 1.45, Height = 1.45, FontSize = 8 };
    public PrintElementLayout Title { get; set; } = new() { X = 0.15, Y = 1.7, Width = 3.2, Height = 0.55, FontSize = 8, Bold = true, AutoShrinkText = true };
    public PrintElementLayout Beco { get; set; } = new() { X = 0.15, Y = 2.25, Width = 3.2, Height = 0.3, FontSize = 6 };
    public PrintElementLayout Ref { get; set; } = new() { X = 0.15, Y = 2.55, Width = 3.2, Height = 0.3, FontSize = 6 };
    public PrintElementLayout Price { get; set; } = new() { X = 0.15, Y = 2.9, Width = 1.5, Height = 0.4, FontSize = 7, Bold = true };
    public PrintElementLayout Ek { get; set; } = new() { X = 1.8, Y = 2.9, Width = 1.5, Height = 0.4, FontSize = 6 };

    public void Normalize()
    {
        CellWidthCm = Math.Clamp(double.IsFinite(CellWidthCm) ? CellWidthCm : 3.5, 2, 5);
        CellHeightCm = Math.Clamp(double.IsFinite(CellHeightCm) ? CellHeightCm : 3.5, 2, 5);
        PaperFormat = PaperFormat switch
        {
            "A3" or "A4" or "A5" or "A6" or "Letter" or "Legal" => PaperFormat,
            _ => "A4"
        };
        Orientation = string.Equals(Orientation, "Portrait", StringComparison.OrdinalIgnoreCase)
            ? "Portrait"
            : "Landscape";
        HorizontalGapCm = Math.Clamp(double.IsFinite(HorizontalGapCm) ? HorizontalGapCm : 0.2, 0, 2);
        VerticalGapCm = Math.Clamp(double.IsFinite(VerticalGapCm) ? VerticalGapCm : 0.2, 0, 2);
        Columns = Math.Clamp(Columns, 1, 12);
        TileCount = Math.Clamp(TileCount, 1, 120);
        MissingBecoText = string.IsNullOrWhiteSpace(MissingBecoText) ? "OROVIVO" : MissingBecoText.Trim();
        QrCode ??= new QrCodeSettings();
        QrCode.Normalize();
        Photo ??= new PrintElementLayout();
        Qr ??= new PrintElementLayout();
        Title ??= new PrintElementLayout();
        Beco ??= new PrintElementLayout();
        Ref ??= new PrintElementLayout();
        Price ??= new PrintElementLayout();
        Ek ??= new PrintElementLayout();
        if (Fields is null || Fields.Count == 0)
        {
            Fields =
            [
                Photo.CopyWith("Foto", "Photo", "Photo"),
                Qr.CopyWith("QR-Code / Barcode", "Qr", "Qr"),
                Title.CopyWith("Beschreibung", "Text", "Title"),
                Beco.CopyWith("BECO", "Text", "Beco"),
                Ref.CopyWith("Referenznummer", "Text", "Ref"),
                Price.CopyWith("VK-Preis", "Number", "Price"),
                Ek.CopyWith("EK", "Number", "Ek")
            ];
        }
        foreach (var element in Fields)
            element.Normalize(CellWidthCm, CellHeightCm);
        QrCode.SourceField = ResolveFieldReference(QrCode.SourceField);
        QrCode.FallbackField = ResolveFieldReference(QrCode.FallbackField);
        foreach (var field in Fields.Where(field => field.Kind == "Number"))
        {
            if (string.IsNullOrEmpty(field.Suffix) && field.Source is "Price" or "Ek")
                field.Suffix = " €";
        }
    }

    private string ResolveFieldReference(string reference)
    {
        if (Fields.Any(field => field.Id == reference))
            return reference;
        var matchingSource = Fields.FirstOrDefault(field => field.Source == reference);
        return matchingSource?.Id ??
               Fields.FirstOrDefault(field => field.Kind is "Text" or "Number")?.Id ??
               Fields.First().Id;
    }

    public static (double WidthCm, double HeightCm) GetPaperDimensionsCm(string paperFormat) =>
        paperFormat switch
        {
            "A3" => (29.7, 42),
            "A5" => (14.8, 21),
            "A6" => (10.5, 14.8),
            "Letter" => (21.59, 27.94),
            "Legal" => (21.59, 35.56),
            _ => (21, 29.7)
        };

    [JsonIgnore]
    public IReadOnlyList<PrintElementLayout> Elements => Fields;

    public FurniturePrintLayout Copy() => new()
    {
        CellWidthCm = CellWidthCm,
        CellHeightCm = CellHeightCm,
        PaperFormat = PaperFormat,
        Orientation = Orientation,
        HorizontalGapCm = HorizontalGapCm,
        VerticalGapCm = VerticalGapCm,
        Columns = Columns,
        TileCount = TileCount,
        MissingBecoText = MissingBecoText,
        QrCode = QrCode.Copy(),
        Fields = Fields.Select(field => field.Copy()).ToList(),
        Photo = Photo.Copy(),
        Qr = Qr.Copy(),
        Title = Title.Copy(),
        Beco = Beco.Copy(),
        Ref = Ref.Copy(),
        Price = Price.Copy(),
        Ek = Ek.Copy()
    };

    public void CopyFrom(FurniturePrintLayout other)
    {
        CellWidthCm = other.CellWidthCm;
        CellHeightCm = other.CellHeightCm;
        PaperFormat = other.PaperFormat;
        Orientation = other.Orientation;
        HorizontalGapCm = other.HorizontalGapCm;
        VerticalGapCm = other.VerticalGapCm;
        Columns = other.Columns;
        TileCount = other.TileCount;
        MissingBecoText = other.MissingBecoText;
        QrCode = other.QrCode.Copy();
        Fields = other.Fields.Select(field => field.Copy()).ToList();
        Photo = other.Photo.Copy();
        Qr = other.Qr.Copy();
        Title = other.Title.Copy();
        Beco = other.Beco.Copy();
        Ref = other.Ref.Copy();
        Price = other.Price.Copy();
        Ek = other.Ek.Copy();
        Normalize();
    }

    public sealed class QrCodeSettings
    {
        public bool Enabled { get; set; } = true;
        public string SourceField { get; set; } = "Ref";
        public string FallbackField { get; set; } = "Beco";
        public bool UseFallback { get; set; } = true;
        public string Prefix { get; set; } = "";
        public string Suffix { get; set; } = "";

        public void Normalize()
        {
            SourceField = string.IsNullOrWhiteSpace(SourceField) ? "Ref" : SourceField.Trim();
            FallbackField = string.IsNullOrWhiteSpace(FallbackField) ? "Beco" : FallbackField.Trim();
            Prefix ??= "";
            Suffix ??= "";
        }

        public QrCodeSettings Copy() => new()
        {
            Enabled = Enabled,
            SourceField = SourceField,
            FallbackField = FallbackField,
            UseFallback = UseFallback,
            Prefix = Prefix,
            Suffix = Suffix
        };
    }
}

public sealed class PrintElementLayout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Feld";
    public string Kind { get; set; } = "Text";
    public string Source { get; set; } = "Title";
    public string StaticText { get; set; } = "";
    public string Suffix { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 0.4;
    public double FontSize { get; set; } = 7;
    public double RotationDegrees { get; set; }
    public bool Bold { get; set; }
    public bool? AutoShrinkText { get; set; }

    public PrintElementLayout Copy() => new()
    {
        Id = Id,
        Name = Name,
        Kind = Kind,
        Source = Source,
        StaticText = StaticText,
        Suffix = Suffix,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        FontSize = FontSize,
        RotationDegrees = RotationDegrees,
        Bold = Bold,
        AutoShrinkText = AutoShrinkText
    };

    public PrintElementLayout CopyWith(string name, string kind, string source)
    {
        var copy = Copy();
        copy.Name = name;
        copy.Kind = kind;
        copy.Source = source;
        return copy;
    }

    public void Normalize(double cellWidth, double cellHeight)
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id;
        Name = string.IsNullOrWhiteSpace(Name) ? "Feld" : Name.Trim();
        Kind = Kind switch
        {
            "Photo" or "Qr" or "Barcode" or "Number" => Kind,
            "Price" => "Number",
            _ => "Text"
        };
        Source = string.IsNullOrWhiteSpace(Source) ? "Title" : Source.Trim();
        StaticText ??= "";
        Suffix ??= "";
        X = Math.Clamp(double.IsFinite(X) ? X : 0, 0, cellWidth);
        Y = Math.Clamp(double.IsFinite(Y) ? Y : 0, 0, cellHeight);
        Width = Math.Clamp(double.IsFinite(Width) ? Width : 1, 0, cellWidth - X);
        Height = Math.Clamp(double.IsFinite(Height) ? Height : 0.4, 0, cellHeight - Y);
        FontSize = Math.Clamp(double.IsFinite(FontSize) ? FontSize : 7, 4, 24);
        RotationDegrees = double.IsFinite(RotationDegrees) ? Math.Clamp(RotationDegrees, -180, 180) : 0;
    }
}
