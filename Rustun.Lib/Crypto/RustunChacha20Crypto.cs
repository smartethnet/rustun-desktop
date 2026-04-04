using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Rustun.Lib.Crypto
{
    public class RustunChacha20Crypto : RustunCrypto
    {
        private Random random = new Random();
        private static int KEYS_SIZE = 32;
        private static int NONCE_SIZE = 12;
        private static int TAG_SIZE = 16; // 128bit

        private byte[] keyBytes;

        public RustunChacha20Crypto(string secret)
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
            // 生成 12 字节的随机 nonce（IV）
            byte[] nonce = new byte[NONCE_SIZE];
            random.NextBytes(nonce);

            // 初始化加密器
            byte[] tag = new byte[TAG_SIZE];

            return data;
        }

        override public byte[] Decrypt(byte[] data)
        {
            return data;
        }
    }
}
