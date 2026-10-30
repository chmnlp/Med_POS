using CommunityToolkit.Mvvm.ComponentModel;
using MargPos.Shared;

namespace MargPos.Desktop.ViewModels;

/// <summary>One line on the bill being built. Amount and availability are set by the bill on recalculation.</summary>
public partial class BillLineViewModel : ObservableObject
{
    private readonly Func<SellableBatchDto, decimal> _defaultRate;

    public BillLineViewModel(ItemDto item, IReadOnlyList<SellableBatchDto> batches, SellableBatchDto batch,
        decimal qty, Func<SellableBatchDto, decimal> defaultRate)
    {
        Item = item;
        Batches = batches;
        _defaultRate = defaultRate;
        _batch = batch;
        _qty = qty;
        _rate = defaultRate(batch);
    }

    public ItemDto Item { get; }
    public IReadOnlyList<SellableBatchDto> Batches { get; }

    /// <summary>Show a batch picker only when there is a real choice.</summary>
    public bool CanPickBatch => Item.IsBatchTracked && Batches.Count > 1;
    public bool IsScheduled => Item.Schedule != DrugSchedule.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverStock))]
    private SellableBatchDto _batch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverStock))]
    private decimal _qty;

    [ObservableProperty] private decimal _rate;
    [ObservableProperty] private decimal _discountPercent;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private int _lineNo;

    /// <summary>Batch stock left for this line after other lines on the bill using the same batch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverStock))]
    private decimal _available;

    public bool IsOverStock => Qty > Available;

    /// <summary>Switching batch re-applies that batch's price.</summary>
    partial void OnBatchChanged(SellableBatchDto value) => Rate = _defaultRate(value);

    public void Reprice() => Rate = _defaultRate(Batch);

    public SaleLineRequest ToRequest() => new(Item.Id, Batch.Id, Qty, Rate, DiscountPercent);
}
