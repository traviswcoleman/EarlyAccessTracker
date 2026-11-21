using System.Reflection;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace EarlyAccessTracker.App;

public static class SecretsLocation
{
    public static string FilePath { get; } = PathHelper.GetSecretsPathFromSecretsId(
        typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()!.UserSecretsId);
}
