using System.Runtime.CompilerServices;
using Avalonia.Controls;
using TrayAppDotNETCommon.UI;
using Xunit;

namespace TrayAppDotNETCommon.XmlSourceGenerator.Tests;

public sealed class UIResourceScopeTests
{
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
        Assert.Equal("expected", errors[0].Message);
    }

    [Fact]
    public void RegistrationAfterDisposalRunsImmediately()
    {
        UIResourceScope scope = new(nameof(RegistrationAfterDisposalRunsImmediately));
        scope.Dispose();
        int cleanupCount = 0;

        scope.Add(() => cleanupCount++);

        Assert.Equal(1, cleanupCount);
    }

    [Fact]
    public void OwnedResourceIsDisposedOnce()
    {
        UIResourceScope scope = new(nameof(OwnedResourceIsDisposedOnce));
        CountingDisposable resource = scope.Own(new CountingDisposable());

        scope.Dispose();
        scope.Dispose();

        Assert.Equal(1, resource.DisposeCount);
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<Control> CreateRetiredGenerationReference()
    {
        Border root = new() { Child = new TextBlock { Text = "collectible" } };
        UIContentGeneration generation = new(nameof(CreateRetiredGenerationReference), root);
        WeakReference<Control> reference = generation.CreateRootWeakReference();
        generation.Dispose();
        return reference;
    }

    private static void ForceCollection()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    private sealed class CountingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
