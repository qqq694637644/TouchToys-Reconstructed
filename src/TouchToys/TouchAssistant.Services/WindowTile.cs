using TouchAssistant.Native;

namespace TouchAssistant.Services;

internal record WindowTile(RunningWindow Window, Win32.RECT Bounds, Win32.RECT RestoreBounds);
