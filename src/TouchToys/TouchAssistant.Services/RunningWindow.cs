using System.Collections.Generic;

namespace TouchAssistant.Services;

internal record RunningWindow(nint Handle, string Title, string Target, List<WindowTile>? Tiles = null);
