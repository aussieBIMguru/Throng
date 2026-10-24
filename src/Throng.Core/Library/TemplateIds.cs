using System.Security.Cryptography;
using System.Text;

namespace Throng.Core.Library;

/// <summary>
/// Deterministic identifiers for built-in templates, so the same built-in always has the same Id across
/// app versions and machines (studies can then recognise "this came from built-in X").
/// </summary>
public static class TemplateIds
{
    private const string Namespace = "throng.builtin/";

    /// <summary>Derives a stable Guid from a template key.</summary>
    public static Guid FromKey(string key)
    {
        // MD5 is used purely as a stable 128-bit hash here, not for security.
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(Namespace + key));
        return new Guid(hash);
    }
}
