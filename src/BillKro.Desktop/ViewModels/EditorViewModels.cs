using CommunityToolkit.Mvvm.ComponentModel;
using BillKro.Desktop.Services;
using BillKro.Shared;

namespace BillKro.Desktop.ViewModels;

public static class Choices
{
    public static IReadOnlyList<string> Units { get; } =
        ["PCS", "STRIP", "TAB", "CAP", "BTL", "BOX", "TUBE", "VIAL", "AMP", "SACHET", "PKT", "KG", "GM", "LTR", "ML"];

    /// <summary>Current GST slabs plus the legacy 12% and 28% for older stock.</summary>
    public static IReadOnlyList<decimal> GstRates { get; } = [0m, 0.25m, 3m, 5m, 12m, 18m, 28m, 40m];

    public static IReadOnlyList<DrugSchedule> Schedules { get; } = Enum.GetValues<DrugSchedule>();
    public static IReadOnlyList<CustomerType> CustomerTypes { get; } = Enum.GetValues<CustomerType>();
    public static IReadOnlyList<UserRole> Roles { get; } = Enum.GetValues<UserRole>();
}

public partial class ItemEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE719";

    private readonly ApiClient _api;
    private readonly int? _id;

    public ItemEditorViewModel(ApiClient api, ItemDto? existing)
    {
        _api = api;
        _id = existing?.Id;
        if (existing is null) return;

        _code = existing.Code;
        _name = existing.Name;
        _barcode = existing.Barcode;
        _manufacturer = existing.Manufacturer;
        _category = existing.Category;
        _pack = existing.Pack;
        _unit = existing.Unit;
        _hsnCode = existing.HsnCode;
        _gstRate = existing.GstRate;
        _mrp = existing.Mrp;
        _saleRate = existing.SaleRate;
        _wholesaleRate = existing.WholesaleRate;
        _hospitalRate = existing.HospitalRate;
        _purchaseRate = existing.PurchaseRate;
        _marginPercent = existing.MarginPercent;
        _wholesaleMarginPercent = existing.WholesaleMarginPercent;
        _isBatchTracked = existing.IsBatchTracked;
        _schedule = existing.Schedule;
        _reorderLevel = existing.ReorderLevel;
        _isActive = existing.IsActive;
    }

    public bool IsNew => _id is null;
    public override string Title => IsNew ? "New item" : $"Edit item {Code}";
    public ItemDto? Result { get; private set; }

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _barcode;
    [ObservableProperty] private string? _manufacturer;
    [ObservableProperty] private string? _category;
    [ObservableProperty] private string? _pack;
    [ObservableProperty] private string _unit = "PCS";
    [ObservableProperty] private string? _hsnCode;
    [ObservableProperty] private decimal _gstRate = 5m;
    [ObservableProperty] private decimal _mrp;
    [ObservableProperty] private decimal _saleRate;
    [ObservableProperty] private decimal _wholesaleRate;
    [ObservableProperty] private decimal _hospitalRate;
    [ObservableProperty] private decimal _purchaseRate;
    [ObservableProperty] private decimal _marginPercent;
    [ObservableProperty] private decimal _wholesaleMarginPercent;
    [ObservableProperty] private DrugSchedule _schedule;
    [ObservableProperty] private decimal _reorderLevel;
    [ObservableProperty] private bool _isActive = true;
    [ObservableProperty] private decimal _openingQty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOpeningQty))]
    private bool _isBatchTracked;

    public bool ShowOpeningQty => IsNew && !IsBatchTracked;

    /// <summary>Sale and wholesale default to MRP until the user sets them.</summary>
    partial void OnMrpChanged(decimal oldValue, decimal newValue)
    {
        if (SaleRate == 0 || SaleRate == oldValue) SaleRate = newValue;
        if (WholesaleRate == 0 || WholesaleRate == oldValue) WholesaleRate = newValue;
    }

    protected override async Task SaveCoreAsync()
    {
        var req = new ItemUpsertRequest
        {
            Code = Code, Name = Name, Barcode = Barcode, Manufacturer = Manufacturer, Category = Category,
            Pack = Pack, Unit = Unit, HsnCode = HsnCode, GstRate = GstRate, Mrp = Mrp, SaleRate = SaleRate,
            WholesaleRate = WholesaleRate, HospitalRate = HospitalRate, PurchaseRate = PurchaseRate,
            MarginPercent = MarginPercent, WholesaleMarginPercent = WholesaleMarginPercent, IsBatchTracked = IsBatchTracked,
            Schedule = Schedule, ReorderLevel = ReorderLevel, IsActive = IsActive,
            OpeningQty = ShowOpeningQty ? OpeningQty : 0,
        };
        Result = IsNew
            ? await _api.PostAsync<ItemDto>("api/items", req)
            : await _api.PutAsync<ItemDto>($"api/items/{_id}", req);
    }
}

public partial class BatchEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE823";

    private readonly ApiClient _api;
    private readonly ItemDto _item;
    private readonly int? _id;

    public BatchEditorViewModel(ApiClient api, ItemDto item, BatchDto? existing)
    {
        _api = api;
        _item = item;
        _id = existing?.Id;
        _mrp = existing?.Mrp ?? item.Mrp;
        _saleRate = existing?.SaleRate ?? item.SaleRate;
        _purchaseRate = existing?.PurchaseRate ?? item.PurchaseRate;
        _wholesaleRate = existing?.WholesaleRate ?? item.WholesaleRate;
        _hospitalRate = existing?.HospitalRate ?? item.HospitalRate;
        if (existing is null) return;

        _batchNo = existing.BatchNo;
        _barcode = existing.Barcode;
        _mfgText = BatchDates.Format(existing.MfgDate);
        _expiryText = existing.ExpiryDate?.ToString("dd-MM-yyyy") ?? "";
    }

    public bool IsNew => _id is null;
    public override string Title => IsNew ? $"New batch — {_item.Name}" : $"Edit batch {BatchNo} — {_item.Name}";

    [ObservableProperty] private string _batchNo = "";
    [ObservableProperty] private string? _barcode;
    [ObservableProperty] private string _mfgText = "";
    [ObservableProperty] private string _expiryText = "";
    [ObservableProperty] private decimal _mrp;
    [ObservableProperty] private decimal _saleRate;
    [ObservableProperty] private decimal _purchaseRate;
    [ObservableProperty] private decimal _wholesaleRate;
    [ObservableProperty] private decimal _hospitalRate;
    [ObservableProperty] private decimal _openingQty;

    protected override async Task SaveCoreAsync()
    {
        const string hint = "Use month/year as printed on the pack, e.g. 03/27, or a full date like 31-03-2027.";
        if (!BatchDates.TryParseExpiry(ExpiryText, out var expiry))
            throw new ApiException($"Expiry date is not valid. {hint}");
        if (!BatchDates.TryParseMfg(MfgText, out var mfg))
            throw new ApiException($"Mfg. date is not valid. {hint}");

        var req = new BatchUpsertRequest
        {
            BatchNo = BatchNo,
            Barcode = Barcode,
            MfgDate = mfg,
            ExpiryDate = expiry,
            Mrp = Mrp, SaleRate = SaleRate, PurchaseRate = PurchaseRate,
            WholesaleRate = WholesaleRate, HospitalRate = HospitalRate,
            OpeningQty = IsNew ? OpeningQty : 0,
        };
        if (IsNew) await _api.PostAsync<BatchDto>($"api/items/{_item.Id}/batches", req);
        else await _api.PutAsync<BatchDto>($"api/batches/{_id}", req);
    }
}

public partial class CustomerEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE716";

    private readonly ApiClient _api;
    private readonly int? _id;

    public CustomerEditorViewModel(ApiClient api, CustomerDto? existing)
    {
        _api = api;
        _id = existing?.Id;
        if (existing is null) return;

        _code = existing.Code;
        _name = existing.Name;
        _phone = existing.Phone;
        _email = existing.Email;
        _gstin = existing.Gstin;
        _address = existing.Address;
        _type = existing.Type;
        _creditLimit = existing.CreditLimit;
        _creditDays = existing.CreditDays;
        _openingBalance = existing.OpeningBalance;
        _isActive = existing.IsActive;
    }

    public bool IsNew => _id is null;
    public override string Title => IsNew ? "New customer" : $"Edit customer {Code}";
    public CustomerDto? Result { get; private set; }

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _gstin;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private CustomerType _type;
    [ObservableProperty] private decimal _creditLimit;
    [ObservableProperty] private int _creditDays = 30;
    [ObservableProperty] private decimal _openingBalance;
    [ObservableProperty] private bool _isActive = true;

    protected override async Task SaveCoreAsync()
    {
        var req = new CustomerUpsertRequest
        {
            Code = Code, Name = Name, Phone = Phone, Email = Email, Gstin = Gstin, Address = Address,
            Type = Type, CreditLimit = CreditLimit, CreditDays = CreditDays, OpeningBalance = OpeningBalance, IsActive = IsActive,
        };
        Result = IsNew
            ? await _api.PostAsync<CustomerDto>("api/customers", req)
            : await _api.PutAsync<CustomerDto>($"api/customers/{_id}", req);
    }
}

public partial class SupplierEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE77B";

    private readonly ApiClient _api;
    private readonly int? _id;

    public SupplierEditorViewModel(ApiClient api, SupplierDto? existing)
    {
        _api = api;
        _id = existing?.Id;
        if (existing is null) return;

        _code = existing.Code;
        _name = existing.Name;
        _phone = existing.Phone;
        _email = existing.Email;
        _gstin = existing.Gstin;
        _drugLicenseNo = existing.DrugLicenseNo;
        _address = existing.Address;
        _creditDays = existing.CreditDays;
        _openingBalance = existing.OpeningBalance;
        _isActive = existing.IsActive;
    }

    public bool IsNew => _id is null;
    public override string Title => IsNew ? "New supplier" : $"Edit supplier {Code}";
    public SupplierDto? Result { get; private set; }

    [ObservableProperty] private string? _code;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string? _phone;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _gstin;
    [ObservableProperty] private string? _drugLicenseNo;
    [ObservableProperty] private string? _address;
    [ObservableProperty] private int _creditDays = 30;
    [ObservableProperty] private decimal _openingBalance;
    [ObservableProperty] private bool _isActive = true;

    protected override async Task SaveCoreAsync()
    {
        var req = new SupplierUpsertRequest
        {
            Code = Code, Name = Name, Phone = Phone, Email = Email, Gstin = Gstin,
            DrugLicenseNo = DrugLicenseNo, Address = Address, CreditDays = CreditDays, OpeningBalance = OpeningBalance, IsActive = IsActive,
        };
        Result = IsNew
            ? await _api.PostAsync<SupplierDto>("api/suppliers", req)
            : await _api.PutAsync<SupplierDto>($"api/suppliers/{_id}", req);
    }
}

public partial class UserEditorViewModel : EditorViewModel
{
    public override string Glyph => "\uE77B";

    private readonly ApiClient _api;
    private readonly int? _id;

    public UserEditorViewModel(ApiClient api, UserDto? existing)
    {
        _api = api;
        _id = existing?.Id;
        if (existing is null) return;

        _username = existing.Username;
        _fullName = existing.FullName;
        _role = existing.Role;
        _isActive = existing.IsActive;
    }

    public bool IsNew => _id is null;
    public bool IsExisting => !IsNew;
    public override string Title => IsNew ? "New user" : $"Edit user {Username}";

    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private UserRole _role = UserRole.Cashier;
    [ObservableProperty] private bool _isActive = true;

    /// <summary>Temporary password; the user must change it at first sign-in.</summary>
    [ObservableProperty] private string _password = "";

    protected override async Task SaveCoreAsync()
    {
        if (IsNew) await _api.PostAsync<UserDto>("api/users", new UserCreateRequest(Username, FullName, Role, Password));
        else await _api.PutAsync<UserDto>($"api/users/{_id}", new UserUpdateRequest(FullName, Role, IsActive));
    }
}

public partial class ResetPasswordViewModel(ApiClient api, UserDto user) : EditorViewModel
{
    public override string Glyph => "\uE72E";

    public override string Title => $"Reset password — {user.FullName}";

    [ObservableProperty] private string _newPassword = "";

    protected override Task SaveCoreAsync() =>
        api.PostAsync($"api/users/{user.Id}/reset-password", new ResetPasswordRequest(NewPassword));
}
