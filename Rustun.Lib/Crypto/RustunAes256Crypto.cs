using System;
using System.Security.Cryptography;
using System.Text;

namespace Rustun.Lib.Crypto
{
    public class RustunAes256Crypto : RustunCrypto
    {
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;

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
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(_keyBytes, TagSize))
            {
                aes.Encrypt(nonce, data, ciphertext, tag);
            }

            // nonce || ciphertext || tag — 与 Java Cipher.doFinal（密文 + 标签）拼接顺序一致
            var result = new byte[NonceSize + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
            Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, tag.Length);
            return result;
        }

        public override byte[] Decrypt(byte[] data)
        {
            if (data.Length < NonceSize + TagSize)
            {
                throw new ArgumentException("data too short", nameof(data));
            }

            ReadOnlySpan<byte> span = data;
            var nonce = span.Slice(0, NonceSize);
            var ciphertext = span.Slice(NonceSize, data.Length - NonceSize - TagSize);
            var tag = span.Slice(data.Length - TagSize, TagSize);

            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(_keyBytes, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }
    }
}
