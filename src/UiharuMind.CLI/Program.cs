using CliFx;

return await new CommandLineApplicationBuilder()
    .SetDescription(
        "Drive the running UiharuMind desktop app over its developer control channel. " +
        "Start the app with 'app start', then run one step with 'app call <step> --args <json>' " +
        "or a whole script with 'app run <script.jsonl>'. " +
        "Profile directory: UIHARU_HOME (default ~/.uiharu); desktop executable: UIHARU_DESKTOP_EXE. " +
        "While the channel is open, 'app guide' prints the full usage guide.")
    .AddCommandsFromThisAssembly()
    .Build()
    .RunAsync();