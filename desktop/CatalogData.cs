using System.Text.Json;
using System.Text.Json.Serialization;

namespace FuniturenEditor.Desktop;

public sealed class CatalogData
{
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
                new PhotoReference(page.Name, item.Title, index + 1, item.Photo)))
            .Where(reference => string.Equals(reference.PhotoKey, key, StringComparison.Ordinal))
            .ToList();

    public void Normalize()
    {
        Pages ??= [];
        EmployeePages ??= [];
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
            while (page.Items.Count < 15)
                page.Items.Add(new CatalogItem());
            if (page.Items.Count > 15)
                page.Items = page.Items.Take(15).ToList();
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
                if (item.QuickService)
                    item.UseBarcode = false;
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
        FurniturePrintLayout ??= new FurniturePrintLayout();
        FurniturePrintLayout.Normalize();
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
    public bool QuickService { get; set; }
    public bool UseBarcode { get; set; }
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
    public string MissingBecoText { get; set; } = "OROVIVO";
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
        MissingBecoText = string.IsNullOrWhiteSpace(MissingBecoText) ? "OROVIVO" : MissingBecoText.Trim();
        Photo ??= new PrintElementLayout();
        Qr ??= new PrintElementLayout();
        Title ??= new PrintElementLayout();
        Beco ??= new PrintElementLayout();
        Ref ??= new PrintElementLayout();
        Price ??= new PrintElementLayout();
        Ek ??= new PrintElementLayout();
        foreach (var element in Elements)
            element.Normalize(CellWidthCm, CellHeightCm);
    }

    [JsonIgnore]
    public IReadOnlyList<PrintElementLayout> Elements => [Photo, Qr, Title, Beco, Ref, Price, Ek];

    public FurniturePrintLayout Copy() => new()
    {
        CellWidthCm = CellWidthCm,
        CellHeightCm = CellHeightCm,
        MissingBecoText = MissingBecoText,
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
        MissingBecoText = other.MissingBecoText;
        Photo = other.Photo.Copy();
        Qr = other.Qr.Copy();
        Title = other.Title.Copy();
        Beco = other.Beco.Copy();
        Ref = other.Ref.Copy();
        Price = other.Price.Copy();
        Ek = other.Ek.Copy();
        Normalize();
    }
}

public sealed class PrintElementLayout
{
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
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        FontSize = FontSize,
        RotationDegrees = RotationDegrees,
        Bold = Bold,
        AutoShrinkText = AutoShrinkText
    };

    public void Normalize(double cellWidth, double cellHeight)
    {
        X = Math.Clamp(double.IsFinite(X) ? X : 0, 0, cellWidth);
        Y = Math.Clamp(double.IsFinite(Y) ? Y : 0, 0, cellHeight);
        Width = Math.Clamp(double.IsFinite(Width) ? Width : 1, 0, cellWidth - X);
        Height = Math.Clamp(double.IsFinite(Height) ? Height : 0.4, 0, cellHeight - Y);
        FontSize = Math.Clamp(double.IsFinite(FontSize) ? FontSize : 7, 4, 24);
        RotationDegrees = double.IsFinite(RotationDegrees) ? Math.Clamp(RotationDegrees, -180, 180) : 0;
    }
}
