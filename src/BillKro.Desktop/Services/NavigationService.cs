namespace BillKro.Desktop.Services;

/// <summary>Lets pages (e.g. dashboard shortcuts) switch the main window to another page.</summary>
public class NavigationService
{
    /// <summary>Set by the signed-in main window; returns false when the user has no access to the page.</summary>
    public Func<Type, bool>? Handler { get; set; }

    public bool GoTo<TPage>() => Handler?.Invoke(typeof(TPage)) ?? false;
}
