namespace TouchAssistant.Core;

internal readonly record struct PadEvent(string Kind, double X = 0.0, double Y = 0.0, int Button = 0);
