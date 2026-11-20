namespace TouchAssistant.Native;

internal readonly record struct EdgeSwipe(EdgeKind Kind, EdgePhase Phase, Win32.POINT Origin, double Distance, double Velocity);
