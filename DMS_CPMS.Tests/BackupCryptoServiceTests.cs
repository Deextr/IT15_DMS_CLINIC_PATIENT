using System.Security.Cryptography;
using DMS_CPMS.Services.BackupRecovery;

namespace DMS_CPMS.Tests;

public class BackupCryptoServiceTests
{
    private static byte[] RandomKey32()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    [Fact]
    public void Encrypt_then_decrypt_returns_original_bytes()
    {
        var crypto = new BackupCryptoService();
        var key = RandomKey32();
        var plaintext = new byte[1024 * 64];
        RandomNumberGenerator.Fill(plaintext);

        var tempEnc = Path.Combine(Path.GetTempPath(), $"clinixdocs_test_{Guid.NewGuid():N}.enc");
        try
        {
            using (var input = new MemoryStream(plaintext))
            {
                crypto.EncryptToFile(input, tempEnc, key);
            }

            var expectedMagic = System.Text.Encoding.ASCII.GetBytes("CLINIXDOCSBK");
            var actualMagic = new byte[expectedMagic.Length];
            using (var fs = new FileStream(tempEnc, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.ReadExactly(actualMagic, 0, actualMagic.Length);
                var version = fs.ReadByte();
                Assert.Equal(1, version);
            }
            Assert.Equal(expectedMagic, actualMagic);

            var header = crypto.ReadHeader(tempEnc);
            Assert.Equal("CLINIXDOCSBK", header.Magic);
            Assert.Equal((byte)1, header.Version);

            using var decrypted = crypto.DecryptToStream(tempEnc, key);
            using var ms = new MemoryStream();
            decrypted.CopyTo(ms);
            var roundtrip = ms.ToArray();

            Assert.Equal(plaintext, roundtrip);
        }
        finally
        {
            try { if (File.Exists(tempEnc)) File.Delete(tempEnc); } catch { }
        }
    }

    [Fact]
    public void Tampering_with_file_fails_integrity_check()
    {
        var crypto = new BackupCryptoService();
        var key = RandomKey32();
        var plaintext = new byte[1024];
        RandomNumberGenerator.Fill(plaintext);

        var tempEnc = Path.Combine(Path.GetTempPath(), $"clinixdocs_test_{Guid.NewGuid():N}.enc");
        try
        {
            using (var input = new MemoryStream(plaintext))
            {
                crypto.EncryptToFile(input, tempEnc, key);
            }

            // Flip a byte somewhere in ciphertext area (after header, before HMAC).
            using (var fs = new FileStream(tempEnc, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // Header: 12 + 1 + 16 + 8 = 37 bytes
                fs.Position = 40;
                var b = fs.ReadByte();
                fs.Position = 40;
                fs.WriteByte((byte)(b ^ 0xFF));
            }

            Assert.Throws<CryptographicException>(() =>
            {
                using var _ = crypto.DecryptToStream(tempEnc, key);
            });
        }
        finally
        {
            try { if (File.Exists(tempEnc)) File.Delete(tempEnc); } catch { }
        }
    }
}

