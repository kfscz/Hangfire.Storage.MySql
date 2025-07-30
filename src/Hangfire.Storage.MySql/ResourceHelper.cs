using System.Reflection;

namespace Hangfire.Storage.MySql;

static class ResourceHelper
{

  public static string GetStringResource(string fileNameWithExtension)
  {
    if (fileNameWithExtension is null)
    {
      throw new ArgumentNullException(nameof(fileNameWithExtension));
    }
#if NET45
    var assembly = typeof(ResourceHelper).Assembly;
#else
    var assembly = typeof(ResourceHelper).GetTypeInfo().Assembly;
#endif
    var resourceName = $"{typeof(ResourceHelper).Namespace}.{fileNameWithExtension}";
    using var stream = assembly.GetManifestResourceStream(resourceName) 
      ?? throw new InvalidOperationException(
        $"Requested resource `{resourceName}` was not found in the assembly `{assembly}`.");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
  }
}