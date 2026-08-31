using System.Runtime.CompilerServices;
using Avalonia.Controls;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UIResourceScopeTests
{
    private const int ReplacementStressCount = 2_000;

    [Fact]
    public void DisposeCancelsThenReleasesInReverseOrder()
    {
        UIResourceScope scope = new(nameof(DisposeCancelsThenReleasesInReverseOrder));
        List<string> events = [];
        CancellationToken cancellationToken = scope.CancellationToken;
        scope.Add(() => events.Add(cancellationToken.IsCancellationRequested ? "first-canceled" : "first-live"));
        scope.Add(() => events.Add("second"));

        scope.Dispose();

        Assert.Equal(["second", "first-canceled"], events);
        Assert.True(scope.IsDisposed);
    }

    [Fact]
    public void DisposeContinuesAfterCleanupFailure()
    {
        List<Exception> errors = [];
        List<string> events = [];
        UIResourceScope scope = new(nameof(DisposeContinuesAfterCleanupFailure), errors.Add);
        scope.Add(() => events.Add("last"));
        scope.Add(static () => throw new InvalidOperationException("expected"));
        scope.Add(() => events.Add("first"));

        scope.Dispose();

        Assert.Equal(["first", "last"], events);
        Assert.Single(errors);
        Assert.Equal(expected: "expected", errors[0].Message);
    }

    [Fact]
    public void RegistrationAfterDisposalRunsImmediately()
    {
        UIResourceScope scope = new(nameof(RegistrationAfterDisposalRunsImmediately));
        scope.Dispose();
        int cleanupCount = 0;

        scope.Add(() => cleanupCount++);

        Assert.Equal(expected: 1, cleanupCount);
    }

    [Fact]
    public void OwnedResourceIsDisposedOnce()
    {
        UIResourceScope scope = new(nameof(OwnedResourceIsDisposedOnce));
        CountingDisposable resource = scope.Own(new CountingDisposable());

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(expected: 1, resource.DisposeCount);
    }

    [Fact]
    public void IndependentlyDisposedChildIsNotRetainedByParent()
    {
        (UIResourceScope parent, WeakReference<UIResourceScope> childReference) =
            CreateDisposedChildReference();

        ForceCollection();

        Assert.False(childReference.TryGetTarget(out UIResourceScope? retainedChild));
        Assert.Null(retainedChild);
        parent.Dispose();
    }

    [Fact]
    public void ParentDisposesLiveChild()
    {
        UIResourceScope parent = new(nameof(ParentDisposesLiveChild));
        UIResourceScope child = parent.CreateChild("Child");

        parent.Dispose();

        Assert.True(child.IsDisposed);
    }

    [Fact]
    public void ContentGenerationClearsDetachedRoot()
    {
        TextBlock child = new() { Text = "retired" };
        Border root = new() { Child = child, DataContext = new object() };
        UIContentGeneration generation = new(nameof(ContentGenerationClearsDetachedRoot), root);

        generation.Dispose();

        Assert.True(generation.IsDisposed);
        Assert.Null(root.Child);
        Assert.Null(root.DataContext);
        Assert.Throws<ObjectDisposedException>(() => generation.Root);
    }

    [Fact]
    public void RetiredGenerationDoesNotRetainRoot()
    {
        WeakReference<Control> rootReference = CreateRetiredGenerationReference();

        ForceCollection();

        Assert.False(rootReference.TryGetTarget(out Control? retainedRoot));
        Assert.Null(retainedRoot);
    }

    [Fact]
    public void ThousandsOfGenerationReplacementsRetireEveryPreviousRoot()
    {
        (UIContentGeneration activeGeneration, List<WeakReference<Control>> retiredRoots, int disposedCount) =
            RunReplacementStress();

        ForceCollection();

        int retainedCount = retiredRoots.Count(reference => reference.TryGetTarget(out Control? _));
        Assert.Equal(expected: 0, retainedCount);
        Assert.Equal(ReplacementStressCount - 1, disposedCount);
        Assert.False(activeGeneration.IsDisposed);
        activeGeneration.Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Control> CreateRetiredGenerationReference()
    {
        Border root = new() { Child = new TextBlock { Text = "collectible" } };
        UIContentGeneration generation = new(nameof(CreateRetiredGenerationReference), root);
        WeakReference<Control> reference = generation.CreateRootWeakReference();
        generation.Dispose();
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UIResourceScope Parent, WeakReference<UIResourceScope> ChildReference)
        CreateDisposedChildReference()
    {
        UIResourceScope parent = new(nameof(CreateDisposedChildReference));
        UIResourceScope child = parent.CreateChild("Child");
        WeakReference<UIResourceScope> reference = new(child);
        child.Dispose();
        return (parent, reference);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UIContentGeneration ActiveGeneration, List<WeakReference<Control>> RetiredRoots,
        int DisposedCount) RunReplacementStress()
    {
        ContentControl host = new();
        List<WeakReference<Control>> retiredRoots = [];
        int disposedCount = 0;
        UIContentGeneration? activeGeneration = null;

        for (int index = 0; index < ReplacementStressCount; index++)
        {
            UIResourceScope resources = new($"Stress.{index}");
            resources.Add(() => disposedCount++);
            Border root = new() { Child = new TextBlock { Text = index.ToString() } };
            UIContentGeneration replacement = new($"Stress.{index}", root, resources);

            UIContentGeneration? previous = activeGeneration;
            host.Content = replacement.Root;
            activeGeneration = replacement;
            if (previous == null) continue;

            retiredRoots.Add(previous.CreateRootWeakReference());
            previous.Dispose();
        }

        return (activeGeneration!, retiredRoots, disposedCount);
    }

    private static void ForceCollection()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(generation: 2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
