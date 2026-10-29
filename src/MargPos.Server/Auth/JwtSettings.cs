using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MargPos.Server.Auth;

public sealed class JwtSettings
{
    public const string Issuer = "MargPos";
    public const string Audience = "MargPos.Desktop";

    public required SymmetricSecurityKey SigningKey { get; init; }
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Uses <c>Jwt:Key</c> when configured; otherwise loads (or creates on first run) a random
    /// key file under ProgramData so each installation gets its own secret.
    /// </summary>
    public static JwtSettings Load(IConfiguration config)
    {
        var hours = config.GetValue("Jwt:LifetimeHours", 12);
        var configured = config["Jwt:Key"];
        byte[] key;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            key = Encoding.UTF8.GetBytes(configured);
        }
        else
        {
            var path = config["Jwt:KeyFile"] ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MargPos", "jwt.key");
            if (File.Exists(path))
            {
                key = Convert.FromBase64String(File.ReadAllText(path).Trim());
            }
            else
            {
                key = RandomNumberGenerator.GetBytes(64);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Convert.ToBase64String(key));
            }
        }

        if (key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 bytes.");

        return new JwtSettings { SigningKey = new SymmetricSecurityKey(key), Lifetime = TimeSpan.FromHours(hours) };
    }
}
