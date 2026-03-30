using System.Security.Cryptography;
using System.Text;

namespace Rustun.Lib.Crypto
{
    public class RustunAes256Crypto : RustunCrypto
    {
        private Random random = new Random();
        private static int KEYS_SIZE = 32;
        private static int NONCE_SIZE = 12;
        private static int TAG_SIZE = 128;

        private byte[] keyBytes;

        public RustunAes256Crypto(string secret)
        {
            var bytes = Encoding.UTF8.GetBytes(secret);
            keyBytes = new byte[KEYS_SIZE];
            if (bytes.Length >= KEYS_SIZE)
            {
                Buffer.BlockCopy(bytes, 0, keyBytes, 0, KEYS_SIZE);
            }
            else
            {
                Buffer.BlockCopy(bytes, 0, keyBytes, 0, bytes.Length);
                for (int i = bytes.Length; i < KEYS_SIZE; i++)
                {
                    keyBytes[i] = 0;
                }
            }
        }

        override public byte[] Encrypt(byte[] data)
        {
            using(var aes = new AesGcm(this.keyBytes, TAG_SIZE))
            {
                byte[] nonce = new byte[NONCE_SIZE];
                random.NextBytes(nonce);
                byte[] ciphertext = new byte[data.Length];
                byte[] tag = new byte[TAG_SIZE / 8];
                aes.Encrypt(nonce, data, ciphertext, tag);

                // 拼接 nonce + tag + ciphertext
                byte[] result = new byte[NONCE_SIZE + tag.Length + ciphertext.Length];
                Buffer.BlockCopy(nonce, 0, result, 0, NONCE_SIZE);
                Buffer.BlockCopy(tag, 0, result, NONCE_SIZE, tag.Length);
                Buffer.BlockCopy(ciphertext, 0, result, NONCE_SIZE + tag.Length, ciphertext.Length);
                return result;
            }
        }

        override public byte[] Decrypt(byte[] data)
        {
            using (var aes = new AesGcm(this.keyBytes, TAG_SIZE))
            {
                byte[] nonce = new byte[NONCE_SIZE];
                byte[] tag = new byte[TAG_SIZE / 8];
                byte[] ciphertext = new byte[data.Length - NONCE_SIZE - tag.Length];
                Buffer.BlockCopy(data, 0, nonce, 0, NONCE_SIZE);
                Buffer.BlockCopy(data, NONCE_SIZE, tag, 0, tag.Length);
                Buffer.BlockCopy(data, NONCE_SIZE + tag.Length, ciphertext, 0, ciphertext.Length);
                byte[] plaintext = new byte[ciphertext.Length];
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return plaintext;
            }
        }
    }
}
