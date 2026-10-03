using CliFx;

return await new CommandLineApplicationBuilder()
    .AddCommandsFromThisAssembly()
    .Build()
    .RunAsync();
