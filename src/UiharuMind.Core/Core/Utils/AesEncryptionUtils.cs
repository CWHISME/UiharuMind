using System.Security.Cryptography;
using System.Text;

namespace UiharuMind.Core.Core.Utils;

public class AesEncryptionUtils
{
    // 固定盐派生密钥:与设备无关,配置文件复制到其他设备后仍可正常解密
    private static readonly byte[] Key = GenerateEncryptionKey("UiharuMind_FixedKey_Salt", 32); // 256位密钥
    private static readonly byte[] Iv = GenerateEncryptionKey("UiharuMind_FixedIv_Salt", 16); // 128位IV

    public static string EncryptString(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        using (Aes aesAlg = Aes.Create())
        {
            aesAlg.Key = Key;
            aesAlg.IV = Iv;

            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(
                           msEncrypt, aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV), CryptoStreamMode.Write))
                using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                {
                    swEncrypt.Write(plainText);
                }

                return Convert.ToBase64String(msEncrypt.ToArray());
            }
        }
    }

    /// <summary>
    /// 解密字符串。密文非法(如旧版明文配置)时返回空字符串,不抛异常
    /// </summary>
    /// <param name="cipherText">密文</param>
    /// <returns>明文;解密失败返回空字符串</returns>
    public static string DecryptString(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;
        try
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Key;
                aesAlg.IV = Iv;

                byte[] cipher = Convert.FromBase64String(cipherText);
                using (MemoryStream msDecrypt = new MemoryStream(cipher))
                {
                    using (CryptoStream csDecrypt = new CryptoStream(
                               msDecrypt, aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV), CryptoStreamMode.Read))
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        return srDecrypt.ReadToEnd();
                    }
                }
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static byte[] GenerateEncryptionKey(string salt, int length)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(salt));
            byte[] output = new byte[length];
            Buffer.BlockCopy(hash, 0, output, 0, Math.Min(hash.Length, length));
            return output;
        }
    }
}
