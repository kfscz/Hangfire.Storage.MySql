namespace System.Collections.Generic;

static class AluCollectionExtensions
{
#if !NET5_0_OR_GREATER
  public static TValue GetValueOrDefault<TKey, TValue>(
    this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
  {
    if (dictionary is null)
    {
      throw new ArgumentNullException(nameof(dictionary));
    }
    return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
  }

  public static HashSet<TSource> ToHashSet<TSource>(
    this IEnumerable<TSource> source) 
    => source.ToHashSet(comparer: null);

  public static HashSet<TSource> ToHashSet<TSource>(
    this IEnumerable<TSource> source, IEqualityComparer<TSource>? comparer)
  {
    if (source is null)
    {
      throw new ArgumentNullException(nameof(source));
    }
    // Don't pre-allocate based on knowledge of size, as potentially many elements will be dropped.
    return new HashSet<TSource>(source, comparer);
  }

#endif
}
