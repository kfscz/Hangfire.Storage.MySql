using System;
using System.Collections;
using System.Collections.Generic;
using Hangfire.Logging;

namespace Hangfire.Storage.MySql.JobQueue;

public class PersistentJobQueueProviderCollection : IEnumerable<IPersistentJobQueueProvider>
{
  static readonly ILog Logger = LogProvider.GetLogger(typeof(PersistentJobQueueProviderCollection));

  readonly List<IPersistentJobQueueProvider> _providers = new();
  readonly Dictionary<string, IPersistentJobQueueProvider> _providersByQueue = new(StringComparer.OrdinalIgnoreCase);
  readonly IPersistentJobQueueProvider _defaultProvider;

  public PersistentJobQueueProviderCollection(IPersistentJobQueueProvider defaultProvider)
  {
    if (defaultProvider is null) throw new ArgumentNullException(nameof(defaultProvider));
    _defaultProvider = defaultProvider;
    _providers.Add(_defaultProvider);
  }

  public void Add(IPersistentJobQueueProvider provider, IEnumerable<string> queues)
  {
    if (provider is null) throw new ArgumentNullException(nameof(provider));
    if (queues is null) throw new ArgumentNullException(nameof(queues));
    Logger.TraceFormat("Add providers");
    _providers.Add(provider);
    foreach (var queue in queues)
    {
      _providersByQueue.Add(queue, provider);
    }
  }

  public IPersistentJobQueueProvider GetProvider(string queue) => _providersByQueue.ContainsKey(queue)
        ? _providersByQueue[queue]
        : _defaultProvider;

  public IEnumerator<IPersistentJobQueueProvider> GetEnumerator() => _providers.GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
