using System.Data;
using System.Diagnostics;

namespace Hangfire.Storage.MySql.Locking;

public class ResourceLock : IDisposable
{

  static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5.0);

  readonly IDbConnection _connection;
  readonly IDbTransaction? _transaction;
  readonly LockableResource _resource;
  readonly string _tablePrefix;

  private ResourceLock(
    IDbConnection connection,
    IDbTransaction? transaction,
    LockableResource resourceName,
    string tablePrefix)
  {
    _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    _transaction = transaction;
    _resource = resourceName;
    _tablePrefix = tablePrefix;
  }

  private static DateTime Now => DateTime.UtcNow;

  string LockName => $"{_tablePrefix}/{_resource}";

  private void Acquire(CancellationToken token, DateTime expiration)
  {
    // always acquire if it is free, regardless of expiration
    if (TryAcquireLock(TimeSpan.Zero))
    {
      return;
    }

    while (true)
    {
      var now = Now;
      if (now > expiration)
      {
        throw new TimeoutException("Lock acquisition period expired");
      }

      token.ThrowIfCancellationRequested();

      // trim time to be between 0s and 1s (to allow Cancellation)
      var secondsLeft = Math.Min(Math.Max(expiration.Subtract(now).TotalSeconds, 0), 1);
      if (TryAcquireLock(TimeSpan.FromSeconds(secondsLeft)))
      {
        return;
      }
    }
  }

  private bool TryAcquireLock(TimeSpan timeout)
  {
    /* MySQL documentation for GET_LOCK:
     * Returns 1 if the lock was obtained successfully, 0 if the attempt timed out (for 
     * example, because another client has previously locked the name), or NULL if an error 
     * occurred (such as running out of memory or the thread was killed with mysqladmin kill). 
     */
    using var command = _connection
      .CreateCommand("SELECT GET_LOCK(@name, @timeout);")
      .WithTransaction(_transaction)
      .AddParameter("@name", LockName)
      .AddParameter("@timeout", timeout.TotalSeconds);
    var result = command.ExecuteScalar();
    int? resultInt = result is null or DBNull
      ? null 
      : Convert.ToInt32(result);
    Debug.Assert(resultInt is null or 0 or 1);
    // I guess, if I get error (result is null), than I did not acquire lock
    return resultInt.HasValue && resultInt.Value == 1;
  }

  private void Release()
  {
    // Logger.TraceFormat("Release resource={0}", _resource);
    /* MySQL documentation for RELEASE_LOCK:
     * Releases the lock named by the string str that was obtained with GET_LOCK(). Returns 1 
     * if the lock was released, 0 if the lock was not established by this thread (in which 
     * case the lock is not released), and NULL if the named lock did not exist. The lock does 
     * not exist if it was never obtained by a call to GET_LOCK() or if it has previously 
     * been released. 
     */
    using var command = _connection
      .CreateCommand("DO RELEASE_LOCK(@name);")
      .WithTransaction(_transaction)
      .AddParameter("@name", LockName);
    command.ExecuteNonQuery();
  }

  public void Dispose() => Release();

  public static void ReleaseAll(IDbConnection connection)
  {
    /* MySQL documentation for RELEASE_ALL_LOCKS:
     *  Releases all named locks held by the current session and returns the number of locks 
     *  released (0 if there were none)
     */
    using var command = connection.CreateCommand("DO RELEASE_ALL_LOCKS();");
    command.ExecuteNonQuery();
  }

  public static IDisposable AcquireOne(
    IDbConnection connection, string tablePrefix, LockableResource resource) =>
    AcquireOne(
      connection: connection, 
      transaction: null,
      tablePrefix: tablePrefix,
      timeout: DefaultTimeout,
      token: CancellationToken.None, 
      resource: resource);

  public static IDisposable AcquireOne(
    IDbConnection connection, IDbTransaction? transaction, string tablePrefix,
    LockableResource resource) =>
    AcquireOne(
      connection: connection,
      transaction: transaction,
      tablePrefix: tablePrefix,
      timeout: DefaultTimeout,
      token: CancellationToken.None,
      resource: resource);

  public static IDisposable AcquireOne(
    IDbConnection connection, string tablePrefix,
    TimeSpan timeout, CancellationToken token,
    LockableResource resource) =>
    AcquireOne(
      connection: connection,
      transaction: null,
      tablePrefix: tablePrefix,
      timeout: timeout,
      token: token,
      resource: resource);

  public static IDisposable AcquireOne(
    IDbConnection connection, IDbTransaction? transaction, string tablePrefix,
    TimeSpan timeout, CancellationToken token,
    LockableResource resource) =>
    AcquireMany(
      connection: connection,
      transaction: transaction,
      tablePrefix: tablePrefix,
      timeout: timeout,
      token: token,
      resources: [resource]);

  public static IDisposable AcquireMany(
    IDbTransaction transaction, string tablePrefix,
    TimeSpan timeout, CancellationToken token,
    IEnumerable<LockableResource> resources) => 
      transaction.Connection is null
      ? throw new ArgumentException("Connection must be set", nameof(transaction))
      : AcquireMany(
          connection: transaction.Connection,
          transaction: transaction,
          tablePrefix: tablePrefix,
          timeout: timeout,
          token: token,
          resources: resources);

  public static IDisposable AcquireMany(
    IDbConnection connection, IDbTransaction? transaction, string tablePrefix,
    TimeSpan timeout, CancellationToken token,
    IEnumerable<LockableResource> resources)
  {
    if (connection is null)
    {
      throw new ArgumentNullException(nameof(connection));
    }
    if (tablePrefix is null)
    {
      throw new ArgumentNullException(nameof(tablePrefix));
    }
    if (resources is null)
    {
      throw new ArgumentNullException(nameof(resources));
    }

    var handles = new DisposableBag();
    try
    {
      var expiration = Now.Add(timeout); // stop trying @
      // Originialy, resources where sorted by resource name alphabetically ("To prevent
      // dead-lock"). I don't see any reason for that specific ordering, so I order them by
      // value, so they are ordered the same way every time (I guess, for preventing
      // dead-locks)
      var orderedResourceNames = resources.OrderBy(x => x);
      foreach (var resourceName in orderedResourceNames)
      {
        var handle = new ResourceLock(
          connection, transaction,
          resourceName, tablePrefix
          );
        handle.Acquire(token, expiration);
        handles.Add(handle);
        token.ThrowIfCancellationRequested();
      }
    }
    catch
    {
      handles.Dispose();
      throw;
    }

    return handles;
  }
}
