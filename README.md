# GrogHLSLCompileTool
A helper to call fxc.exe shader compiler through wine to cover all my entry points and macros.

This is a Linux thing made to call Wine to invoke fxc to build shaders.  Since there's no open source of the D3DCompiler dll out there, this seems the only way to get shaders built on linux.

There's a bit of effort to make the output friendly with VSCode's problem matchers.  See ExampleTasks.json.
