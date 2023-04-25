using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Microsoft.DotNet.Authentication.Algorithms;

public class OneTimePasswordGenerator
{
    private readonly byte[] _seed;

    public OneTimePasswordGenerator(string secretBase32Encoded)
    {
        _seed = ConvertFromBase32(secretBase32Encoded);
    }
    
    public string Generate(DateTimeOffset timestamp)
    {
        byte[] timestampBy30sBytes = BitConverter.GetBytes(timestamp.ToUnixTimeSeconds() / 30);
        Array.Reverse((Array)timestampBy30sBytes);
        byte[] hash;
        using (HMACSHA1 hmacsha1 = new HMACSHA1(_seed)) // lgtm [cs/weak-hmacs] Algorithm specified by OTP standard
            hash = hmacsha1.ComputeHash(timestampBy30sBytes);
        Array.Reverse((Array)hash);
        int num = (int)hash[0] & 15;
        return ((BitConverter.ToUInt32(hash, hash.Length - num - 4) & (uint)int.MaxValue) % 1000000U).ToString("D6");
    }

    private static byte[] ConvertFromBase32(string seed)
    {
        List<byte> byteList = new List<byte>(200);
        byte num1 = 0;
        byte num2 = 0;
        for (int index = 0; index < seed.Length; ++index)
        {
            byte num3 = (byte)(8U - (uint)num1);
            char c = seed[index];
            if (c != '=')
            {
                byte num4 = DecodeBase32Char(c);
                if (num3 > (byte)5)
                {
                    num2 = (byte)((uint)num2 << 5 | (uint)num4);
                    num1 += (byte)5;
                }
                else
                {
                    num1 = (byte)(5U - (uint)num3);
                    byte num5 = (byte)((uint)num4 >> (int)num1);
                    byte num6 = (byte)((uint)(byte)((uint)num2 << (int)num3) | (uint)num5);
                    num2 = (byte)((uint)num4 & (uint)((int)byte.MaxValue >> 8 - (int)num1));
                    byteList.Add(num6);
                }
            }
            else
                break;
        }
        if (num1 != (byte)0)
        {
            byte num3 = (byte)(8U - (uint)num1);
            byte num4 = (byte)((uint)num2 << (int)num3);
            byteList.Add(num4);
        }
        return byteList.ToArray();
    }

    private static byte DecodeBase32Char(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c >= 'A' && c <= 'Z')
            return (byte)((uint)c - 65U);
        if (c >= '2' && c <= '7')
            return (byte)((int)c - 50 + 26);

        throw new InvalidOperationException($"Char {c} is not a valid base32 character.");
    }
}
