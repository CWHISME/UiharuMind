using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.Core.Utils;

public class AesEncryptionUtils
{
    private static readonly string MacAddress = GetDeviceMacAddress();
    private static readonly byte[] Key = GenerateEncryptionKey(MacAddress, 32); // 256位密钥
    private static readonly byte[] Iv = GenerateEncryptionKey(MacAddress, 16); // 128位IV

    // 加密字符串
    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.Key = Key;
            aesAlg.IV = Iv;

            ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV);

            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(plainText);
                }

                return Convert.ToBase64String(msEncrypt.ToArray());
            }
        }
    }

    // 解密字符串
    public static string DecryptString(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.Key = Key;
            aesAlg.IV = Iv;

            ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

            byte[] cipher = Convert.FromBase64String(cipherText);

            using (MemoryStream msDecrypt = new MemoryStream(cipher))
            {
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    return srDecrypt.ReadToEnd();
                }
            }
        }
    }

    // 获取设备的MAC地址
    private static string GetDeviceMacAddress()
    {
        var nic = NetworkInterface
            .GetAllNetworkInterfaces()
            .FirstOrDefault(n =>
                n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);

        if (nic == null)
        {
            throw new InvalidOperationException("No network interfaces found.");
        }

        return nic.GetPhysicalAddress().ToString();
    }

    // 生成固定长度的加密密钥
    private static byte[] GenerateEncryptionKey(string macAddress, int keyLength)
    {
        byte[] macBytes = Encoding.UTF8.GetBytes(macAddress);
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hashBytes = sha256.ComputeHash(macBytes);
            if (keyLength <= hashBytes.Length)
            {
                return hashBytes.Take(keyLength).ToArray();
            }
            else
            {
                throw new ArgumentException("Key length cannot be greater than the hash length.");
            }
        }
    }
}