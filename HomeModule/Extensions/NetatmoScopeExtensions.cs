using Netatmo.Net.Model;
using System.Linq;

namespace Netatmo.Net.Extensions
{
    public static class NetatmoScopeExtensions
    {
        public static string ToScopeString(this NetatmoScope[] scopes)
        {
            var scopeString = scopes.Aggregate("", (current, netatmoScope) => string.IsNullOrEmpty(current) ? current + $"{netatmoScope}" : current + $" {netatmoScope}");
            return scopeString;
        }
    }
}
