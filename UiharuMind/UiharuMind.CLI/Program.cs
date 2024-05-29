// See https://aka.ms/new-console-template for more information

using System.Text.Json;
using UiharuMind.Core;

Console.WriteLine("Hello, World!");

// Console.WriteLine(JsonSerializer.Serialize(UiharuCoreManager.Instance.Setting));
// UiharuCoreManager.Instance.Setting.Save();
await UiharuCoreManager.Instance.LLamaCppServer.ScanLocalModels();
UiharuCoreManager.Instance.LLamaCppServer.SaveConfig();