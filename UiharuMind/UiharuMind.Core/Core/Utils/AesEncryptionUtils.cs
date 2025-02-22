using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public class AesEncryptionUtils
{
    private static readonly string MacAddress = GetDeviceMacAddress();
    private static readonly byte[] Key = GenerateEncryptionKey(MacAddress, 32); // 256位密钥
    private static readonly byte[] Iv = GenerateEncryptionKey("IV_CAT", 16); // 128位IV

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
            //已知问题：不同的 key 和 iv 加解密的结果相同，相当于只是明文混淆，不知道是什么原因
            //调用 GenerateKey 和 GenerateIV 方法会报错？什么情况...
            aesAlg.Key = Key;
            aesAlg.IV = Iv;
            // aesAlg.GenerateKey();
            // aesAlg.GenerateIV();

            ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);

            byte[] cipher = Convert.FromBase64String(cipherText);

            using (MemoryStream msDecrypt = new MemoryStream(cipher))
            {
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    var plainText = srDecrypt.ReadToEnd();
                    // Log.Warning(
                    //     $"MacAddress:{MacAddress}  plainText:{plainText}  Key:{BitConverter.ToString(aesAlg.Key)}  Iv:{BitConverter.ToString(aesAlg.IV)}");
                    return plainText;
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
            return ("No network interfaces found.");
        }

        return nic.GetPhysicalAddress().ToString();
    }

    private static byte[] GenerateEncryptionKey(string macAddress, int length)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(macAddress));
            byte[] output = new byte[length];
            Buffer.BlockCopy(hash, 0, output, 0, Math.Min(hash.Length, length));
            return output;
        }
    }
}