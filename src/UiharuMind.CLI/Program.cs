using CliFx;

await new CommandLineApplicationBuilder()
    .AddCommandsFromThisAssembly()
    .Build()
    .RunAsync();
