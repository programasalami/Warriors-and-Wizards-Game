# Extra patch rules for the browser build (exec'd by patch_sources.py; `rule` and `replace_file` are in scope).
# Each 'old' must match exactly once. Keep patches tiny - anything bigger belongs in a hand-written replacement under web/shim.

# The console logger starts a background thread, which single-threaded WebAssembly cannot do: log through the browser console instead.
rule('AlloyClient/AlloyClient/Logging/Logging.cs',
     ('builder.AddConsole(options => { options.FormatterName = SingleLineConsoleFormatter.FormatterName; })\n                .AddConsoleFormatter<SingleLineConsoleFormatter, ConsoleFormatterOptions>()',
      'builder.AddProvider(new WarriorsWeb.WebLoggerProvider())\n                .SetMinimumLevel(LogLevel.Debug)'))

# WebGL2 (unlike desktop GL) does not let a buffer that was ever bound to COPY_WRITE_BUFFER be bound to ELEMENT_ARRAY_BUFFER, and the
# desktop IndexBuffer creates its buffer through CopyWriteBuffer: use the element-array target for every operation on index buffers.
rule('AlloyClient/Alloy.Engine/Graphics/Buffers/IndexBuffer.cs',
     ('BufferTarget.CopyWriteBuffer', 'BufferTarget.ElementArrayBuffer', 6))

# The account server is reached through the page's configured api base (same-origin /api -> proxy, see main.js WW_CONFIG). Relative
# endpoint paths so "/api/" is kept, and no thread-pool/blocking assumptions.
rule('AlloyClient/AlloyClient/AppEngine/AppEngineClient.cs',
     ('Client.BaseAddress = new Uri(Settings.AppEngineUrl);', 'Client.BaseAddress = new Uri(WarriorsWeb.WebHost.ApiBase);'),
     ('client.PostAsync(endpoint, content, cancellationTokenSource.Token)', 'client.PostAsync(endpoint.TrimStart((char)47), content, cancellationTokenSource.Token)'))

# Raw TCP sockets do not exist in browsers: hand-written WebSocket version in web/shim/WebClient.cs.
replace_file('AlloyClient/AlloyClient/Networking/Client.cs', 'shim/WebClient.cs')

# Interpreted WebAssembly is far slower than native, so start-up work can starve the request timers: give the account server much longer.
rule('AlloyClient/AlloyClient/Core/Settings.cs',
     ('public const int AppEngineTimeout = 10000;', 'public const int AppEngineTimeout = 120000;'))

# Shadows use a data texture instead of a uniform block (see tools/port_shaders.py port_shadow_vert / shim/UniformBuffer.cs).
replace_file('AlloyClient/Alloy.Engine/Graphics/Buffers/UniformBuffer.cs', 'shim/UniformBuffer.cs')
rule('AlloyClient/Alloy.Engine/Graphics/Shader.cs',
     ('public void SetValue(string uniform, UniformBuffer buffer) => GL.BindBufferBase(BufferTarget.UniformBuffer, GetUniformBlock(uniform), buffer.Handle);',
      'public void SetValue(string uniform, UniformBuffer buffer) => buffer.BindAsTexture();'))

# Opening a link (the "download the desktop client" button) goes through the page instead of launching a process; also marks this build as the web one.
replace_file('AlloyClient/AlloyClient/Utils/ClientPlatform.cs', 'shim/ClientPlatform.cs')
