using TaskManagerTrayAppDotNET.Models;
using TaskManagerTrayAppDotNET.UI;
using Xunit;

namespace TaskManagerTrayAppDotNET.Tests;

public sealed class SemanticProcessSectionsTests
{
    [Fact]
    public void SectionsRequireSemanticGrouping()
    {
        Assert.True(SemanticProcessSections.IsEnabled(ProcessGroupingStyle.Semantic));
        Assert.False(SemanticProcessSections.IsEnabled(ProcessGroupingStyle.ParentProcess));
        Assert.False(SemanticProcessSections.IsEnabled(ProcessGroupingStyle.None));
    }

    [Fact]
    public void SectionsUseTaskManagerOrderAndLabels()
    {
        Assert.Equal(expected: 3, SemanticProcessSections.Count);
        Assert.Equal(expected: 2, SemanticProcessSections.RowsPerSection);
        Assert.Equal(
            SemanticProcessGroupClassification.App,
            SemanticProcessSections.GetClassification(sectionIndex: 0));
        Assert.Equal(
            SemanticProcessGroupClassification.Background,
            SemanticProcessSections.GetClassification(sectionIndex: 1));
        Assert.Equal(
            SemanticProcessGroupClassification.Windows,
            SemanticProcessSections.GetClassification(sectionIndex: 2));
        Assert.Equal(
            "Apps (18)",
            SemanticProcessSections.GetTitle(SemanticProcessGroupClassification.App, 18));
        Assert.Equal(
            "Background processes (280)",
            SemanticProcessSections.GetTitle(
                SemanticProcessGroupClassification.Background,
                280));
        Assert.Equal(
            "Windows processes (92)",
            SemanticProcessSections.GetTitle(SemanticProcessGroupClassification.Windows, 92));
    }

    [Fact]
    public void SectionRowKeysAreDistinctFromGroupSyntheticKeys()
    {
        HashSet<ProcessInstanceKey> keys = [];
        for (int sectionIndex = 0; sectionIndex < SemanticProcessSections.Count; sectionIndex++)
        {
            SemanticProcessGroupClassification classification =
                SemanticProcessSections.GetClassification(sectionIndex);
            Assert.True(keys.Add(SemanticProcessSections.GetInstanceKey(
                classification,
                SemanticProcessSectionRowKind.Spacer)));
            Assert.True(keys.Add(SemanticProcessSections.GetInstanceKey(
                classification,
                SemanticProcessSectionRowKind.Header)));
        }

        Assert.DoesNotContain(
            new ProcessInstanceKey(
                SemanticProcessSections.FirstGroupSyntheticProcessID,
                CreationTimeTicks: 0),
            keys);
    }
}
