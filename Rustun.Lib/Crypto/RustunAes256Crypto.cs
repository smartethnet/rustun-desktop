using System;
using System.Security.Cryptography;
using System.Text;

namespace Rustun.Lib.Crypto
{
    public class RustunAes256Crypto : RustunCrypto
    {
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSizeBits = 128;
        private const int TagSizeBytes = TagSizeBits / 8;

        private readonly byte[] _keyBytes;

        public RustunAes256Crypto(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            _keyBytes = new byte[KeySize];
            if (bytes.Length >= KeySize)
            {
                Buffer.BlockCopy(bytes, 0, _keyBytes, 0, KeySize);
            }
            else
            {
                Buffer.BlockCopy(bytes, 0, _keyBytes, 0, bytes.Length);
            }
        }

        public override byte[] Encrypt(byte[] data)
        {
            var nonce = new byte[NonceSize];
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[data.Length];
            var tag = new byte[TagSizeBytes];

            using (var aes = new AesGcm(_keyBytes, TagSizeBits))
            {
                aes.Encrypt(nonce, data, ciphertext, tag);
            }

            // nonce + tag + ciphertext（保持与既有密文格式兼容）
            var result = new byte[NonceSize + tag.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, result, NonceSize, tag.Length);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize + tag.Length, ciphertext.Length);
            return result;
        }

        public override byte[] Decrypt(byte[] data)
        {
            if (data.Length < NonceSize + TagSizeBytes)
            {
                throw new ArgumentException("data too short", nameof(data));
            }

            ReadOnlySpan<byte> span = data;
            var nonce = span.Slice(0, NonceSize);
            var tag = span.Slice(NonceSize, TagSizeBytes);
            var ciphertext = span.Slice(NonceSize + TagSizeBytes);

            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(_keyBytes, TagSizeBits))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }
    }
}
