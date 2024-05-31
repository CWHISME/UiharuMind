// See https://aka.ms/new-console-template for more information

using CliFx;

// Console.WriteLine("Hello, World!");
//
// // Console.WriteLine(JsonSerializer.Serialize(UiharuCoreManager.Instance.Setting));
// // UiharuCoreManager.Instance.Setting.Save();
// await UiharuCoreManager.Instance.LLamaCppServer.ScanLocalModels();
// UiharuCoreManager.Instance.LLamaCppServer.SaveConfig();
await new CliApplicationBuilder()
    .AddCommandsFromThisAssembly()
    .Build()
    .RunAsync();