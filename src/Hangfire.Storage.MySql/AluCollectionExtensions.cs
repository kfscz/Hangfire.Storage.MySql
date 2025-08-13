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
#endif
}
