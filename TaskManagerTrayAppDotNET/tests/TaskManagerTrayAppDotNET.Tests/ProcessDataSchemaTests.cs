using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class ProcessDataSchemaTests
{
    [Fact]
    public void EveryVisibleCatalogColumnHasExactlyOneStoragePath()
    {
        List<ProcessColumnSetting> settings = [];
        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
            settings.Add(Setting(definition.Kind, true));
        ProcessDataSchema schema = ProcessDataSchema.Create(settings);

        foreach (ProcessTableColumnDefinition definition in ProcessTableColumnCatalog.Definitions)
        {
            int storagePathCount = 0;
            if (ProcessDataSchema.UsesIdentityTextStorage(definition.Kind)
                || ProcessDataSchema.UsesIdentityNumericStorage(definition.Kind))
            {
                storagePathCount++;
            }

            if (definition.Lifetime == ProcessTableColumnLifetime.Static)
            {
                int slot = ProcessDataSchema.StoresText(definition.Kind)
                    ? schema.GetStaticTextSlot(definition.Kind)
                    : schema.GetStaticNumericSlot(definition.Kind);
                if (slot >= 0) storagePathCount++;
            }
            else
            {
                int slot = ProcessDataSchema.StoresText(definition.Kind)
                    ? schema.GetDynamicTextSlot(definition.Kind)
                    : schema.GetDynamicNumericSlot(definition.Kind);
                if (slot >= 0) storagePathCount++;
            }

            Assert.True(schema.IsVisible(definition.Kind));
            Assert.Equal(1, storagePathCount);
        }
    }

    [Fact]
    public void CreateStoresOnlyVisibleColumnsAndDoesNotDuplicateIdentityText()
    {
        List<ProcessColumnSetting> settings =
        [
            Setting(ProcessTableColumnKind.Name, true),
            Setting(ProcessTableColumnKind.ProcessID, true),
            Setting(ProcessTableColumnKind.UserName, true),
            Setting(ProcessTableColumnKind.CommandLine, false),
            Setting(ProcessTableColumnKind.CPU, true),
            Setting(ProcessTableColumnKind.GPUEngine, true)
        ];

        ProcessDataSchema schema = ProcessDataSchema.Create(settings);

        Assert.True(schema.IsVisible(ProcessTableColumnKind.Name));
        Assert.True(schema.IsVisible(ProcessTableColumnKind.ProcessID));
        Assert.True(schema.IsVisible(ProcessTableColumnKind.UserName));
        Assert.True(schema.IsVisible(ProcessTableColumnKind.CPU));
        Assert.True(schema.IsVisible(ProcessTableColumnKind.GPUEngine));
        Assert.False(schema.IsVisible(ProcessTableColumnKind.CommandLine));
        Assert.Equal(0, schema.StaticNumericCount);
        Assert.Equal(0, schema.StaticTextCount);
        Assert.Equal(1, schema.DynamicNumericCount);
        Assert.Equal(1, schema.DynamicTextCount);
        Assert.Equal(-1, schema.GetStaticNumericSlot(ProcessTableColumnKind.ProcessID));
        Assert.Equal(-1, schema.GetStaticTextSlot(ProcessTableColumnKind.Name));
        Assert.Equal(-1, schema.GetStaticTextSlot(ProcessTableColumnKind.UserName));
        Assert.Equal(-1, schema.GetStaticTextSlot(ProcessTableColumnKind.CommandLine));
    }

    [Fact]
    public void ReorderingColumnsKeepsTheCompactStorageSchemaStable()
    {
        List<ProcessColumnSetting> first =
        [
            Setting(ProcessTableColumnKind.CommandLine, true),
            Setting(ProcessTableColumnKind.CPU, true),
            Setting(ProcessTableColumnKind.ProcessID, true),
            Setting(ProcessTableColumnKind.Status, true)
        ];
        List<ProcessColumnSetting> reordered =
        [
            Setting(ProcessTableColumnKind.Status, true),
            Setting(ProcessTableColumnKind.ProcessID, true),
            Setting(ProcessTableColumnKind.CPU, true),
            Setting(ProcessTableColumnKind.CommandLine, true)
        ];

        ProcessDataSchema firstSchema = ProcessDataSchema.Create(first);
        ProcessDataSchema reorderedSchema = ProcessDataSchema.Create(reordered);

        Assert.Equal(firstSchema.VisibleMask, reorderedSchema.VisibleMask);
        Assert.Equal(
            firstSchema.GetStaticNumericSlot(ProcessTableColumnKind.ProcessID),
            reorderedSchema.GetStaticNumericSlot(ProcessTableColumnKind.ProcessID));
        Assert.Equal(
            firstSchema.GetStaticTextSlot(ProcessTableColumnKind.CommandLine),
            reorderedSchema.GetStaticTextSlot(ProcessTableColumnKind.CommandLine));
        Assert.Equal(
            firstSchema.GetDynamicNumericSlot(ProcessTableColumnKind.CPU),
            reorderedSchema.GetDynamicNumericSlot(ProcessTableColumnKind.CPU));
        Assert.Equal(
            firstSchema.GetDynamicNumericSlot(ProcessTableColumnKind.Status),
            reorderedSchema.GetDynamicNumericSlot(ProcessTableColumnKind.Status));
    }

    [Fact]
    public void AdditionalSearchColumnsAreStoredWithoutMakingThemVisibleInSettings()
    {
        List<ProcessColumnSetting> settings =
        [
            Setting(ProcessTableColumnKind.ProcessID, true),
            Setting(ProcessTableColumnKind.CommandLine, false),
            Setting(ProcessTableColumnKind.Lifetime, false)
        ];
        ulong searchColumnsMask = ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.CommandLine)
                                  | ProcessTableColumnCatalog.GetMask(ProcessTableColumnKind.Lifetime);

        ProcessDataSchema schema = ProcessDataSchema.Create(
            settings,
            searchColumnsMask);

        Assert.True(schema.IsVisible(ProcessTableColumnKind.ProcessID));
        Assert.True(schema.IsVisible(ProcessTableColumnKind.CommandLine));
        Assert.True(schema.IsVisible(ProcessTableColumnKind.Lifetime));
        Assert.True(schema.GetStaticTextSlot(ProcessTableColumnKind.CommandLine) >= 0);
        Assert.True(schema.GetDynamicNumericSlot(ProcessTableColumnKind.Lifetime) >= 0);
        Assert.False(settings[1].Visible);
        Assert.False(settings[2].Visible);
    }

    [Fact]
    public void MutableNativeContextsUseDynamicStorage()
    {
        ProcessTableColumnDefinition jobObject = ProcessTableColumnCatalog.Get(ProcessTableColumnKind.JobObjectID);
        ProcessTableColumnDefinition enterprise = ProcessTableColumnCatalog.Get(ProcessTableColumnKind.EnterpriseContext);

        Assert.Equal(ProcessTableColumnLifetime.Dynamic, jobObject.Lifetime);
        Assert.Equal(ProcessTableColumnLifetime.Dynamic, enterprise.Lifetime);
        Assert.False(ProcessDataSchema.StoresText(ProcessTableColumnKind.JobObjectID));
        Assert.True(ProcessDataSchema.StoresText(ProcessTableColumnKind.EnterpriseContext));
    }

    [Fact]
    public void SnapshotBufferUsesGeometricCapacityAndFlatDynamicStorage()
    {
        ProcessDataSchema schema = ProcessDataSchema.Create(
        [
            Setting(ProcessTableColumnKind.ProcessID, true),
            Setting(ProcessTableColumnKind.SessionID, true),
            Setting(ProcessTableColumnKind.CPU, true),
            Setting(ProcessTableColumnKind.GPUEngine, true)
        ]);
        ProcessSnapshotBuffer source = new();
        source.BeginWrite(schema, 3);
        ProcessImageIdentity image = new("test", "test.exe", string.Empty, string.Empty, default);

        for (int rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            long[] staticValues = new long[schema.StaticNumericCount];
            staticValues[schema.GetStaticNumericSlot(ProcessTableColumnKind.SessionID)] = rowIndex;
            long[] dynamicValues = new long[schema.DynamicNumericCount];
            dynamicValues[schema.GetDynamicNumericSlot(ProcessTableColumnKind.CPU)] =
                BitConverter.DoubleToInt64Bits(rowIndex + 0.5);
            string?[] dynamicText = new string?[schema.DynamicTextCount];
            dynamicText[schema.GetDynamicTextSlot(ProcessTableColumnKind.GPUEngine)] = "engine " + rowIndex;
            ProcessStaticData staticData = new()
            {
                InstanceKey = new ProcessInstanceKey(rowIndex + 10, rowIndex + 100),
                Image = image,
                UserName = "user",
                NumericValues = staticValues,
                TextValues = []
            };
            source.SetRow(rowIndex, staticData, dynamicValues, dynamicText);
        }

        source.CompleteWrite(3);
        ProcessSnapshotBuffer copy = new();
        copy.CopyFrom(source);

        Assert.Equal(3, copy.Count);
        Assert.Equal(256, copy.Capacity);
        Assert.Equal(copy.Capacity * schema.DynamicNumericCount, copy.DynamicNumericValues.Length);
        Assert.Equal(copy.Capacity * schema.DynamicTextCount, copy.DynamicTextValues.Length);
        Assert.Equal(2.5, BitConverter.Int64BitsToDouble(copy.GetDynamicNumeric(2, ProcessTableColumnKind.CPU)));
        Assert.Equal("engine 2", copy.GetDynamicText(2, ProcessTableColumnKind.GPUEngine));
        Assert.Same(source.StaticRows[1], copy.StaticRows[1]);

        copy.Reset();
        Assert.Null(copy.Schema);
        Assert.Equal(0, copy.Count);
        Assert.Empty(copy.StaticRows);
        Assert.Empty(copy.DynamicNumericValues);
        Assert.Empty(copy.DynamicTextValues);
    }

    private static ProcessColumnSetting Setting(ProcessTableColumnKind column, bool visible) =>
        new()
        {
            Column = column,
            Visible = visible,
            Width = ProcessTableColumnCatalog.Get(column).DefaultWidth
        };
}
