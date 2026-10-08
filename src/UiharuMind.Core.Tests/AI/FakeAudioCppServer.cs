namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 假 audiocpp_server：一个 shell 脚本起 python 迷你服务，读 server.json 的端口与 voice_dir。
/// 合成回「模型:音色」；不给音色时，vox 开头的模型（照 VoxCPM：会克隆、没有自带音色）回「模型:auto:seed=种子:文字」，
/// 其余照 pocket_tts 报 500。vox、echo 开头的没有自带音色。音色列表照真服务把 voice_dir 里的 wav 并进去；听写回「模型 heard you」；
/// 每次启动在 starts 里记一行
/// </summary>
internal static class FakeAudioCppServer
{
    /// <summary>
    /// 写出可执行的假服务
    /// </summary>
    /// <param name="path">脚本路径</param>
    /// <param name="directory">starts 记在哪</param>
    public static void Write(string path, string directory)
    {
        string counter = Path.Combine(directory, "starts");
        File.WriteAllText(path, $$"""
            #!/bin/sh
            echo x >> '{{counter}}'
            exec python3 -c "
            import http.server, json, sys, re, os, urllib.parse
            cfg = json.load(open(sys.argv[1]))
            port = cfg['port']
            voice_dir = cfg.get('voice_dir')
            def library():
                if not voice_dir or not os.path.isdir(voice_dir): return []
                return sorted(f[:-4] for f in os.listdir(voice_dir) if f.endswith('.wav'))
            class H(http.server.BaseHTTPRequestHandler):
                def reply(self, code, body, kind='application/json'):
                    self.send_response(code); self.send_header('Content-Type', kind); self.end_headers(); self.wfile.write(body)
                def do_GET(self):
                    if self.path == '/health': self.reply(200, b'{}')
                    elif self.path.startswith('/v1/audio/voices'):
                        model = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query).get('model', [''])[0]
                        native = [] if model.startswith(('vox', 'echo')) else ['alba', 'marius']
                        self.reply(200, json.dumps({'voices': native + library()}).encode())
                    elif self.path == '/v1/models': self.reply(200, b'{\"data\":[{\"id\":\"tts-a\",\"loaded\":true},{\"id\":\"asr-a\",\"loaded\":false}]}')
                    else: self.reply(404, b'{}')
                def do_POST(self):
                    body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
                    if self.path == '/v1/audio/speech':
                        req = json.loads(body)
                        if req.get('voice'): self.reply(200, (req['model'] + ':' + req['voice']).encode(), 'audio/wav')
                        elif req['model'].startswith('vox'):
                            self.reply(200, (req['model'] + ':auto:seed=' + str(req.get('seed')) + ':' + req['input']).encode(), 'audio/wav')
                        else: self.reply(500, b'{\"error\":\"requires a session voice\"}')
                    elif self.path == '/v1/audio/transcriptions':
                        model = re.search(rb'name=\"?model\"?\r\n(?:[^\r]+\r\n)*\r\n([^\r]+)', body).group(1).decode()
                        self.reply(200, json.dumps({'text': model + ' heard you'}).encode())
                    else: self.reply(404, b'{}')
                def log_message(self, *a): pass
            http.server.HTTPServer(('127.0.0.1', port), H).serve_forever()
            " "$2"
            """);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
