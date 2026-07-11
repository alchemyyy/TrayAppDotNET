namespace BrightnessTrayAppDotNET.DDCCI;

/// <summary>
/// Depth-first flattening extension used to walk the capability-string parse tree.
/// </summary>
public static class RecursiveSelectExtensions
{
    extension<TSource>(IEnumerable<TSource> source)
    {
        public IEnumerable<TSource> RecursiveSelect(Func<TSource, IEnumerable<TSource>?> childSelector)
            =>
                source.RecursiveSelect(childSelector, element => element);

        public IEnumerable<TResult> RecursiveSelect<TResult>(Func<TSource, IEnumerable<TSource>?> childSelector,
            Func<TSource, TResult> selector)
            =>
                source.RecursiveSelect(childSelector, (element, _, _) => selector(element));

        public IEnumerable<TResult> RecursiveSelect<TResult>(Func<TSource, IEnumerable<TSource>?> childSelector,
            Func<TSource, int, TResult> selector)
            =>
                source.RecursiveSelect(childSelector, (element, index, _) => selector(element, index));

        public IEnumerable<TResult> RecursiveSelect<TResult>(Func<TSource, IEnumerable<TSource>?> childSelector,
            Func<TSource, int, int, TResult> selector)
            => RecursiveSelect(source, childSelector, selector, 0);
    }

    private static IEnumerable<TResult> RecursiveSelect<TSource, TResult>(
        IEnumerable<TSource> source,
        Func<TSource, IEnumerable<TSource>?> childSelector,
        Func<TSource, int, int, TResult> selector,
        int depth)
    {
        return source.SelectMany((element, index) =>
            Enumerable.Repeat(selector(element, index, depth), 1)
                .Concat(RecursiveSelect(
                    childSelector(element) ?? [],
                    childSelector, selector, depth + 1)));
    }
}
