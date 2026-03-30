using System;
using System.Collections.Generic;
using System.Text;

namespace Rustun.Lib.Crypto
{
    public class RustunCrypto
    {
        virtual public byte[] Encrypt(byte[] input)
        {
            return input;
        }

        virtual public byte[] Decrypt(byte[] input)
        {
            return input;
        }
    }
}
