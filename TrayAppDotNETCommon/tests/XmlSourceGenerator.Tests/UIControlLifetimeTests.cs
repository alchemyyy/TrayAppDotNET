using Avalonia.Controls;
using Avalonia.Media;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UIControlLifetimeTests
{
    [Fact]
    public void SearchableListRebuildDisposesRetiredFactoryContent() => AvaloniaTestHost.Run(() =>
    {
        int createdCount = 0;
        int disposedCount = 0;
        SettingsSearchableListBox list = new(Palette());
        list.Items.Add(new SettingsSearchableListBoxItem(
            "item",
            "Item",
            contentFactory: () => new CountingControl(
                () => createdCount++,
                () => disposedCount++)));

        Assert.Equal(1, createdCount);
        Assert.Equal(0, disposedCount);

        list.ItemPadding = new Avalonia.Thickness(2);
        Assert.Equal(2, createdCount);
        Assert.Equal(1, disposedCount);

        list.Items.Clear();
        Assert.Equal(2, createdCount);
        Assert.Equal(2, disposedCount);

        list.Dispose();
        Assert.Equal(2, disposedCount);
    });

    [Fact]
    public void SearchableListClearPerformsOneEmptyGenerationBuild() => AvaloniaTestHost.Run(() =>
    {
        int createdCount = 0;
        SettingsSearchableListBox list = new(Palette());
        for (int index = 0; index < 3; index++)
        {
            int itemIndex = index;
            list.Items.Add(new SettingsSearchableListBoxItem(
                itemIndex,
                $"Item {itemIndex}",
                contentFactory: () =>
                {
                    createdCount++;
                    return new Border();
                }));
        }

        createdCount = 0;
        list.Items.Clear();

        Assert.Equal(0, createdCount);
        list.Dispose();
    });

    [Fact]
    public void SearchableListFailedCandidateKeepsActiveRowsAndCollection()
        => AvaloniaTestHost.Run(() =>
        {
            int createdCount = 0;
            int disposedCount = 0;
            SettingsSearchableListBox list = new(Palette());
            list.Items.Add(new SettingsSearchableListBoxItem(
                "stable",
                "Stable",
                contentFactory: () => new CountingControl(
                    () => createdCount++,
                    () => disposedCount++)));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                list.Items.Add(new SettingsSearchableListBoxItem(
                    "failing",
                    "Failing",
                    contentFactory: static () => throw new InvalidOperationException("expected row failure"))));

            Assert.Equal("expected row failure", exception.Message);
            Assert.Single(list.Items);
            Assert.Equal("stable", list.Items[0].Tag);
            Assert.Equal(2, createdCount);
            Assert.Equal(1, disposedCount);

            list.ItemMargin = new Avalonia.Thickness(1);
            Assert.Equal(3, createdCount);
            Assert.Equal(2, disposedCount);
            list.Dispose();
            Assert.Equal(3, disposedCount);
        });

    [Fact]
    public void ComboBoxDisposesMeasuredSelectedAndItemFactoryContent() => AvaloniaTestHost.Run(() =>
    {
        int createdCount = 0;
        int disposedCount = 0;
        SettingsComboBox comboBox = new(Palette(), autoSizeToText: false);
        SettingsComboBoxItem item = new(
            "item",
            "Item",
            Palette(),
            () => new CountingControl(
                () => createdCount++,
                () => disposedCount++));
        comboBox.Items.Add(item);
        comboBox.SelectedItem = item;

        Assert.Equal(2, createdCount);
        Assert.Equal(0, disposedCount);

        comboBox.AutoSizeToText = true;
        Assert.Equal(3, createdCount);
        Assert.Equal(1, disposedCount);

        comboBox.SelectedItem = null;
        Assert.Equal(4, createdCount);
        Assert.Equal(3, disposedCount);

        comboBox.Items.Clear();
        Assert.Equal(4, disposedCount);

        comboBox.Dispose();
        Assert.Equal(4, disposedCount);
    });

    [Fact]
    public void ComboBoxFailedSelectionContentKeepsPreviousSelection()
        => AvaloniaTestHost.Run(() =>
        {
            int factoryCallCount = 0;
            SettingsComboBox comboBox = new(Palette(), autoSizeToText: false);
            SettingsComboBoxItem item = new(
                "item",
                "Item",
                Palette(),
                () =>
                {
                    factoryCallCount++;
                    return factoryCallCount > 1
                        ? throw new InvalidOperationException("expected selection failure")
                        : new Border();
                });
            comboBox.Items.Add(item);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                comboBox.SelectedItem = item);

            Assert.Equal("expected selection failure", exception.Message);
            Assert.Null(comboBox.SelectedItem);
            Assert.Equal(-1, comboBox.SelectedIndex);
            comboBox.Dispose();
        });

    private static SettingsPalette Palette() => new(
        Colors.Black,
        Colors.White,
        Colors.Gray,
        Colors.DarkGray,
        Colors.DimGray,
        Colors.Black,
        Colors.DarkGray,
        Colors.LightGray,
        Colors.Gray,
        Colors.Blue,
        Colors.Blue,
        Colors.White,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.DarkBlue,
        Colors.Blue,
        Colors.Gray,
        Colors.White,
        Colors.Red,
        Colors.DarkRed,
        Colors.White);

    private sealed class CountingControl : Border, IDisposable
    {
        private readonly Action _disposed;
        private int _disposeState;

        public CountingControl(Action created, Action disposed)
        {
            _disposed = disposed;
            created();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
            _disposed();
        }
    }
}
