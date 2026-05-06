using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DMS_CPMS.Services.BackupRecovery
{
    public interface IBackupCryptoService
    {
        string FileExtension { get; }
        void EncryptToFile(Stream plaintext, string outputFilePath, ReadOnlySpan<byte> masterKey32Bytes);
        Stream DecryptToStream(string encryptedFilePath, ReadOnlySpan<byte> masterKey32Bytes);
        BackupFileHeader ReadHeader(string encryptedFilePath);
    }

    public sealed record BackupFileHeader(string Magic, byte Version, long CiphertextLength);

    /// <summary>
    /// Streaming encryption: AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC).
    /// File format:
    /// [Magic(12)] [Version(1)] [IV(16)] [CipherLen(8 LE)] [Ciphertext] [HMAC(32)]
    /// HMAC covers: Magic|Version|IV|CipherLen|Ciphertext
    /// </summary>
    public sealed class BackupCryptoService : IBackupCryptoService
    {
        public const string MagicValue = "CLINIXDOCSBK"; // 11 chars
        private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(MagicValue + "1"); // 12 bytes total
        private const byte CurrentVersion = 1;
        public string FileExtension => ".enc";

        public void EncryptToFile(Stream plaintext, string outputFilePath, ReadOnlySpan<byte> masterKey32Bytes)
        {
            if (masterKey32Bytes.Length != 32) throw new ArgumentException("Master key must be 32 bytes.", nameof(masterKey32Bytes));

            Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            using var output = new FileStream(outputFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            // Derive independent keys for enc/mac using HKDF-like expansion (HMACSHA256).
            Span<byte> encKey = stackalloc byte[32];
            Span<byte> macKey = stackalloc byte[32];
            DeriveKeys(masterKey32Bytes, encKey, macKey);

            Span<byte> iv = stackalloc byte[16];
            RandomNumberGenerator.Fill(iv);

            // We'll stream-encrypt, but we must write cipher length up-front.
            // Use a temp file for ciphertext so we can know length and compute HMAC without buffering.
            var tempCipherPath = Path.Combine(Path.GetTempPath(), $"clinixdocs_cipher_{Guid.NewGuid():N}.bin");
            try
            {
                long cipherLen;
                using (var tempCipher = new FileStream(tempCipherPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encKey.ToArray();
                    aes.IV = iv.ToArray();

                    using var crypto = new CryptoStream(tempCipher, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true);
                    plaintext.CopyTo(crypto);
                    crypto.FlushFinalBlock();
                    cipherLen = tempCipher.Length;
                }

                // Write header (Magic(12), Version(1), IV(16), CipherLen(8 LE))
                output.Write(MagicBytes, 0, MagicBytes.Length);
                output.WriteByte(CurrentVersion);
                output.Write(iv);

                Span<byte> lenBytes = stackalloc byte[8];
                BitConverter.TryWriteBytes(lenBytes, cipherLen);
                output.Write(lenBytes);

                // Stream ciphertext and compute HMAC as we go (including header bytes)
                using var hmac = new HMACSHA256(macKey.ToArray());
                hmac.TransformBlock(MagicBytes, 0, MagicBytes.Length, null, 0);
                hmac.TransformBlock(new[] { CurrentVersion }, 0, 1, null, 0);
                hmac.TransformBlock(iv.ToArray(), 0, 16, null, 0);
                hmac.TransformBlock(lenBytes.ToArray(), 0, 8, null, 0);

                using (var tempCipher = new FileStream(tempCipherPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[1024 * 1024];
                    int read;
                    while ((read = tempCipher.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        hmac.TransformBlock(buffer, 0, read, null, 0);
                    }
                }

                hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                output.Write(hmac.Hash!, 0, hmac.Hash!.Length);
            }
            finally
            {
                TryDelete(tempCipherPath);
                CryptographicOperations.ZeroMemory(encKey);
                CryptographicOperations.ZeroMemory(macKey);
                CryptographicOperations.ZeroMemory(iv);
            }
        }

        public Stream DecryptToStream(string encryptedFilePath, ReadOnlySpan<byte> masterKey32Bytes)
        {
            if (masterKey32Bytes.Length != 32) throw new ArgumentException("Master key must be 32 bytes.", nameof(masterKey32Bytes));

            var header = ReadHeader(encryptedFilePath);
            if (header.Magic != MagicValue + "1") throw new CryptographicException("Invalid backup file signature.");
            if (header.Version != CurrentVersion) throw new CryptographicException("Unsupported backup file version.");

            // Validate HMAC, then return a stream that decrypts ciphertext to a temp file, and opens it for reading.
            // (We avoid returning a CryptoStream over the source file because HMAC validation requires reading to end first.)
            var tempPlainPath = Path.Combine(Path.GetTempPath(), $"clinixdocs_plain_{Guid.NewGuid():N}.zip");
            try
            {
                ValidateHmac(encryptedFilePath, masterKey32Bytes);
                DecryptToFileInternal(encryptedFilePath, tempPlainPath, masterKey32Bytes);
                return new TempFileStream(tempPlainPath);
            }
            catch
            {
                TryDelete(tempPlainPath);
                throw;
            }
        }

        public BackupFileHeader ReadHeader(string encryptedFilePath)
        {
            using var fs = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 12 + 1 + 16 + 8 + 32) throw new InvalidDataException("Backup file is too small.");

            var magic = new byte[12];
            fs.ReadExactly(magic, 0, magic.Length);
            var version = (byte)fs.ReadByte();
            _ = fs.Read(new byte[16], 0, 16);
            var lenBytes = new byte[8];
            fs.ReadExactly(lenBytes, 0, lenBytes.Length);
            var cipherLen = BitConverter.ToInt64(lenBytes, 0);

            return new BackupFileHeader(Encoding.ASCII.GetString(magic), version, cipherLen);
        }

        private static void ValidateHmac(string encryptedFilePath, ReadOnlySpan<byte> masterKey32Bytes)
        {
            Span<byte> encKey = stackalloc byte[32];
            Span<byte> macKey = stackalloc byte[32];
            DeriveKeys(masterKey32Bytes, encKey, macKey);

            try
            {
                using var fs = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var totalLen = fs.Length;
                if (totalLen < 12 + 1 + 16 + 8 + 32) throw new InvalidDataException("Backup file is too small.");

                var macOffset = totalLen - 32;
                using var hmac = new HMACSHA256(macKey.ToArray());

                var buffer = new byte[1024 * 1024];
                long remaining = macOffset;
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var read = fs.Read(buffer, 0, toRead);
                    if (read <= 0) throw new EndOfStreamException();
                    hmac.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }
                hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                var expected = hmac.Hash!;
                var actual = new byte[32];
                fs.ReadExactly(actual, 0, actual.Length);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    throw new CryptographicException("Backup file integrity check failed.");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }

        private static void DecryptToFileInternal(string encryptedFilePath, string outputPlainPath, ReadOnlySpan<byte> masterKey32Bytes)
        {
            Span<byte> encKey = stackalloc byte[32];
            Span<byte> macKey = stackalloc byte[32];
            DeriveKeys(masterKey32Bytes, encKey, macKey);

            try
            {
                using var fs = new FileStream(encryptedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var totalLen = fs.Length;
                var macOffset = totalLen - 32;

                var magic = new byte[12];
                fs.ReadExactly(magic, 0, magic.Length);
                var version = (byte)fs.ReadByte();
                if (Encoding.ASCII.GetString(magic) != MagicValue + "1" || version != CurrentVersion)
                    throw new CryptographicException("Invalid backup file header.");

                var iv = new byte[16];
                fs.ReadExactly(iv, 0, iv.Length);
                var lenBytes = new byte[8];
                fs.ReadExactly(lenBytes, 0, lenBytes.Length);
                var cipherLen = BitConverter.ToInt64(lenBytes, 0);
                if (cipherLen < 0 || (12 + 1 + 16 + 8 + cipherLen + 32) != totalLen)
                    throw new InvalidDataException("Backup file length metadata is invalid.");

                Directory.CreateDirectory(Path.GetDirectoryName(outputPlainPath)!);
                using var plain = new FileStream(outputPlainPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

                using var aes = Aes.Create();
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = encKey.ToArray();
                aes.IV = iv;

                using var crypto = new CryptoStream(plain, aes.CreateDecryptor(), CryptoStreamMode.Write);

                var buffer = new byte[1024 * 1024];
                long remaining = cipherLen;
                while (remaining > 0)
                {
                    var toRead = (int)Math.Min(buffer.Length, remaining);
                    var read = fs.Read(buffer, 0, toRead);
                    if (read <= 0) throw new EndOfStreamException();
                    crypto.Write(buffer, 0, read);
                    remaining -= read;
                }
                crypto.FlushFinalBlock();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encKey);
                CryptographicOperations.ZeroMemory(macKey);
            }
        }

        private static void DeriveKeys(ReadOnlySpan<byte> masterKey32Bytes, Span<byte> encKey32, Span<byte> macKey32)
        {
            // Simple deterministic expansion with domain separation labels.
            using var h = new HMACSHA256(masterKey32Bytes.ToArray());
            var enc = h.ComputeHash(Encoding.UTF8.GetBytes("clinixdocs-backup-enc"));
            var mac = h.ComputeHash(Encoding.UTF8.GetBytes("clinixdocs-backup-mac"));
            enc.AsSpan(0, 32).CopyTo(encKey32);
            mac.AsSpan(0, 32).CopyTo(macKey32);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }

        private sealed class TempFileStream : Stream
        {
            private readonly string _path;
            private readonly FileStream _inner;

            public TempFileStream(string path)
            {
                _path = path;
                _inner = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            protected override void Dispose(bool disposing)
            {
                try
                {
                    if (disposing) _inner.Dispose();
                }
                finally
                {
                    TryDelete(_path);
                    base.Dispose(disposing);
                }
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }
            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}

