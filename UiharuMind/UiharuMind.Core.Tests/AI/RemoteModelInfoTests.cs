using System.Text.Json;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.AI;

public class RemoteModelInfoTests
{
    private const string TestApiKey = "f30f9148bd0aaba38fe60077e89df104.uhnv7eX1ctIs8XOK";

    [Fact]
    public void ApiKey_SerializedValue_IsEncryptedNotPlaintext()
    {
        var info = new RemoteModelInfo { ApiKey = TestApiKey };

        string json = JsonSerializer.Serialize(info, SaveUtility.JsonOptions);

        Assert.DoesNotContain(TestApiKey, json);
        Assert.Contains("\"ApiKey\"", json);
    }

    [Fact]
    public void ApiKey_RoundTrip_ThroughSerialization()
    {
        var info = new RemoteModelInfo { ApiKey = TestApiKey };

        string json = JsonSerializer.Serialize(info, SaveUtility.JsonOptions);
        var restored = JsonSerializer.Deserialize<RemoteModelInfo>(json, SaveUtility.JsonOptions);

        Assert.Equal(TestApiKey, restored!.ApiKey);
    }

    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        string encrypted = AesEncryptionUtils.EncryptString(TestApiKey);

        Assert.NotEqual(TestApiKey, encrypted);
        Assert.Equal(TestApiKey, AesEncryptionUtils.DecryptString(encrypted));
    }

    [Fact]
    public void DecryptString_InvalidCipherText_ReturnsEmpty()
    {
        Assert.Equal("", AesEncryptionUtils.DecryptString("not-a-valid-cipher"));
    }

    [Fact]
    public void ApiKey_DifferentValues_ProduceDifferentCipherText()
    {
        string a = AesEncryptionUtils.EncryptString("key-one");
        string b = AesEncryptionUtils.EncryptString("key-two");

        Assert.NotEqual(a, b);
    }
}
