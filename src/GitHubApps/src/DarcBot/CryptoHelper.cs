// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Security.Cryptography;

static class CryptoHelper
{
    public static RSAParameters GetRsaParameters(string privateKeyBase64)
    {
        byte[] privateKeyBytes = Convert.FromBase64String(privateKeyBase64);

        using (var memoryStream = new MemoryStream(privateKeyBytes))
        using (var binaryReader = new BinaryReader(memoryStream))
        {
            var twobytes = binaryReader.ReadUInt16();
            if (twobytes == 0x8130)
                binaryReader.ReadByte();
            else if (twobytes == 0x8230)
                binaryReader.ReadInt16();
            else
                throw new CryptographicException("Wrong data");

            twobytes = binaryReader.ReadUInt16();
            if (twobytes != 0x0102)
                throw new CryptographicException("Wrong data");

            var bt = binaryReader.ReadByte();
            if (bt != 0x00)
                throw new CryptographicException("Wrong data");

            var elements = GetIntegerSize(binaryReader);
            var paramModulus = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramE = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramD = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramP = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramQ = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramDP = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramDQ = binaryReader.ReadBytes(elements);

            elements = GetIntegerSize(binaryReader);
            var paramIQ = binaryReader.ReadBytes(elements);

            EnsureLength(ref paramD, 256);
            EnsureLength(ref paramDP, 128);
            EnsureLength(ref paramDQ, 128);
            EnsureLength(ref paramE, 3);
            EnsureLength(ref paramIQ, 128);
            EnsureLength(ref paramModulus, 256);
            EnsureLength(ref paramP, 128);
            EnsureLength(ref paramQ, 128);

            var rsaParameters = new RSAParameters
            {
                Modulus = paramModulus,
                Exponent = paramE,
                D = paramD,
                P = paramP,
                Q = paramQ,
                DP = paramDP,
                DQ = paramDQ,
                InverseQ = paramIQ
            };

            return rsaParameters;
        }
    }

    private static int GetIntegerSize(BinaryReader binary)
    {
        var bt = binary.ReadByte();

        if (bt != 0x02)
            return 0;

        bt = binary.ReadByte();

        int count;
        if (bt == 0x81)
        {
            count = binary.ReadByte();
        }
        else if (bt == 0x82)
        {
            var highbyte = binary.ReadByte();
            var lowbyte = binary.ReadByte();
            byte[] modint = { lowbyte, highbyte, 0x00, 0x00 };
            count = BitConverter.ToInt32(modint, 0);
        }
        else
        {
            count = bt;
        }

        while (binary.ReadByte() == 0x00)
            count -= 1;

        binary.BaseStream.Seek(-1, SeekOrigin.Current);

        return count;
    }

    private static void EnsureLength(ref byte[] data, int desiredLength)
    {
        if (data == null || data.Length >= desiredLength)
            return;

        int zeros = desiredLength - data.Length;

        byte[] newData = new byte[desiredLength];
        Array.Copy(data, 0, newData, zeros, data.Length);

        data = newData;
    }
}
