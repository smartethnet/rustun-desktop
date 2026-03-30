using System.Text;

namespace Rustun.Lib.Crypto
{
    public class RustunXorCrypto : RustunCrypto
    {
        private readonly byte[] key;
        public RustunXorCrypto(string secret)
        {
            this.key = Encoding.UTF8.GetBytes(secret);
        }

        override public byte[] Encrypt(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(data[i] ^ key[i % key.Length]);
            }
            return data;
        }

        override public byte[] Decrypt(byte[] data)
        {
            return Encrypt(data);
        }
    }
}
