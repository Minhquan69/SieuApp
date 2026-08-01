using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace V3SClient.libs
{
    internal static class PlaybackTokenStore
    {
        private const string TokenFileName = "playback-token.dat";

        private static string TokenPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "iVista",
                "V3SClient",
                TokenFileName);

        internal static string Load()
        {
            try
            {
                if (!File.Exists(TokenPath))
                {
                    return null;
                }

                byte[] encrypted = File.ReadAllBytes(TokenPath);
                byte[] plaintext = ProtectedData.Unprotect(
                    encrypted,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException)
            {
                LoggerManager.LogWarn("Không thể đọc playback token cục bộ cho Windows user hiện tại.");
                return null;
            }
            catch (IOException)
            {
                LoggerManager.LogWarn("Không thể đọc playback token cục bộ.");
                return null;
            }
        }

        internal static void Save(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("Playback token is required.", nameof(token));
            }

            string directory = Path.GetDirectoryName(TokenPath);
            Directory.CreateDirectory(directory);

            byte[] plaintext = Encoding.UTF8.GetBytes(token);
            byte[] encrypted = ProtectedData.Protect(
                plaintext,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenPath, encrypted);
        }
    }
}
