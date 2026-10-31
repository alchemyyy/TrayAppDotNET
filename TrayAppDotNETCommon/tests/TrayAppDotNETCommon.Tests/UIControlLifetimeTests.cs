using Avalonia.Controls;
using TrayAppDotNETCommon.UI.Controls;
using Xunit;

namespace TrayAppDotNETCommon.Tests;

public sealed class UIControlLifetimeTests
{
    [Fact]
    public void SearchableListRebuildDisposesRetiredFactoryContent() => AvaloniaTestHost.Run(() =>
    {
        int createdCount = 0;
        int disposedCount = 0;
        SettingsSearchableListBox list = new(TestPalettes.Default());
        list.Items.Add(new SettingsSearchableListBoxItem(
            tag: "item",
            text: "Item",
            contentFactory: () => new CountingControl(
                () => createdCount++,
                () => disposedCount++)));

        Assert.Equal(expected: 1, createdCount);
        Assert.Equal(expected: 0, disposedCount);

        list.ItemPadding = new Avalonia.Thickness(2);
        Assert.Equal(expected: 2, createdCount);
        Assert.Equal(expected: 1, disposedCount);

        list.Items.Clear();
        Assert.Equal(expected: 2, createdCount);
        Assert.Equal(expected: 2, disposedCount);

        list.Dispose();
        Assert.Equal(expected: 2, disposedCount);
    });

    [Fact]
    public void SearchableListClearPerformsOneEmptyGenerationBuild() => AvaloniaTestHost.Run(() =>
    {
        int createdCount = 0;
        SettingsSearchableListBox list = new(TestPalettes.Default());
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

        Assert.Equal(expected: 0, createdCount);
        list.Dispose();
    });

    [Fact]
    public void SearchableListFailedCandidateKeepsActiveRowsAndCollection()
        => AvaloniaTestHost.Run(() =>
        {
            int createdCount = 0;
            int disposedCount = 0;
            SettingsSearchableListBox list = new(TestPalettes.Default());
            list.Items.Add(new SettingsSearchableListBoxItem(
                tag: "stable",
                text: "Stable",
                contentFactory: () => new CountingControl(
                    () => createdCount++,
                    () => disposedCount++)));

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                list.Items.Add(new SettingsSearchableListBoxItem(
                    tag: "failing",
                    text: "Failing",
                    contentFactory: static () => throw new InvalidOperationException("expected row failure"))));

            Assert.Equal(expected: "expected row failure", exception.Message);
            Assert.Single(list.Items);
            Assert.Equal(expected: "stable", list.Items[0].Tag);
            Assert.Equal(expected: 2, createdCount);
            Assert.Equal(expected: 1, disposedCount);

            list.ItemMargin = new Avalonia.Thickness(1);
            Assert.Equal(expected: 3, createdCount);
            Assert.Equal(expected: 2, disposedCount);
            list.Dispose();
            Assert.Equal(expected: 3, disposedCount);
        });

    [Fact]
    public void ComboBoxDisposesMeasuredSelectedAndItemFactoryContent() => AvaloniaTestHost.Run(() =>
    {
        int createdCount = 0;
        int disposedCount = 0;
        SettingsComboBox comboBox = new(TestPalettes.Default(), autoSizeToText: false);
        SettingsComboBoxItem item = new(
            tag: "item",
            text: "Item",
            TestPalettes.Default(),
            () => new CountingControl(
                () => createdCount++,
                () => disposedCount++));
        comboBox.Items.Add(item);
        comboBox.SelectedItem = item;

        Assert.Equal(expected: 2, createdCount);
        Assert.Equal(expected: 0, disposedCount);

        comboBox.AutoSizeToText = true;
        Assert.Equal(expected: 3, createdCount);
        Assert.Equal(expected: 1, disposedCount);

        comboBox.SelectedItem = null;
        Assert.Equal(expected: 4, createdCount);
        Assert.Equal(expected: 3, disposedCount);

        comboBox.Items.Clear();
        Assert.Equal(expected: 4, disposedCount);

        comboBox.Dispose();
        Assert.Equal(expected: 4, disposedCount);
    });

    [Fact]
    public void ComboBoxFailedSelectionContentKeepsPreviousSelection()
        => AvaloniaTestHost.Run(() =>
        {
            int factoryCallCount = 0;
            SettingsComboBox comboBox = new(TestPalettes.Default(), autoSizeToText: false);
            SettingsComboBoxItem item = new(
                tag: "item",
                text: "Item",
                TestPalettes.Default(),
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

            Assert.Equal(expected: "expected selection failure", exception.Message);
            Assert.Null(comboBox.SelectedItem);
            Assert.Equal(expected: -1, comboBox.SelectedIndex);
            comboBox.Dispose();
        });

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
            if (Interlocked.Exchange(ref _disposeState, value: 1) != 0) return;
            _disposed();
        }
    }
}
