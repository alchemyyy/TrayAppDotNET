using Avalonia.Controls;

namespace TrayAppDotNETCommon.UI;

/// <summary>
/// Owns one replaceable UI root and every resource whose lifetime is tied to that root.
/// </summary>
public sealed class UIContentGeneration : IDisposable
{
    private static long s_nextID;

    private readonly Action<Control>? _releaseRoot;
    private readonly Action<Exception>? _logError;
    private Control? _root;
    private int _disposed;

    public UIContentGeneration(
        string ownerName,
        Control root,
        UIResourceScope? resources = null,
        Action<Control>? releaseRoot = null,
        Action<Exception>? logError = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerName);
        ArgumentNullException.ThrowIfNull(root);

        OwnerName = ownerName;
        ID = Interlocked.Increment(ref s_nextID);
        _root = root;
        Resources = resources ?? new UIResourceScope(ownerName, logError);
        _releaseRoot = releaseRoot;
        _logError = logError;
    }

    /// <summary>Gets the diagnostic identity of this generation.</summary>
    public long ID { get; }

    /// <summary>Gets the owner label used in cleanup diagnostics.</summary>
    public string OwnerName { get; }

    /// <summary>Gets the generation resource scope.</summary>
    public UIResourceScope Resources { get; }

    /// <summary>Gets whether this generation has been retired.</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Gets the generation root while it is active or under construction.</summary>
    public Control Root => _root ?? throw new ObjectDisposedException(OwnerName);

    /// <summary>Creates a weak reference suitable for retirement collection tests.</summary>
    public WeakReference<Control> CreateRootWeakReference() => new(Root);

    /// <summary>Cancels work, releases owned resources, and severs the detached root from its descendants.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        Control? root = Interlocked.Exchange(ref _root, null);
        Resources.Dispose();
        if (root == null) return;

        try
        {
            if (_releaseRoot != null)
                _releaseRoot(root);
            else
                ReleaseRoot(root);
        }
        catch (Exception exception)
        {
            Log(exception);
        }
    }

    private static void ReleaseRoot(Control root)
    {
        root.DataContext = null;
        switch (root)
        {
            case ContentControl contentControl:
                contentControl.Content = null;
                return;

            case Decorator decorator:
                decorator.Child = null;
                return;

            case Panel panel:
                panel.Children.Clear();
                return;

            case ItemsControl itemsControl:
                itemsControl.ItemsSource = null;
                itemsControl.Items.Clear();
                return;
        }
    }

    private void Log(Exception exception)
    {
        if (_logError != null)
        {
            try
            {
                _logError(exception);
                return;
            }
            catch (Exception loggerException)
            {
                TADNLog.Log(
                    $"UIContentGeneration '{OwnerName}' logger failed: {loggerException.GetType().Name}: " +
                    loggerException.Message);
            }
        }

        TADNLog.Log(
            $"UIContentGeneration '{OwnerName}' root release failed: {exception.GetType().Name}: " +
            exception.Message);
    }
}
