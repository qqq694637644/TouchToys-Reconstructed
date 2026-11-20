using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;

internal static class Program
{
    private static int passed;
    private static int failed;

    private static void Main()
    {
        Run("touchpad.single-finger-click", TouchpadSingleFingerClick);
        Run("touchpad.double-tap-binding", TouchpadDoubleTapBinding);
        Run("touchpad.pinch-out-binding", TouchpadPinchOutBinding);
        Run("touchpad.two-finger-scroll", TouchpadTwoFingerScroll);
        Run("touchpad.three-finger-scroll", TouchpadThreeFingerScroll);
        Run("touchpad.two-finger-swipe", TouchpadTwoFingerSwipe);
        Run("touchpad.hold-binding", TouchpadHoldBinding);
        Run("middle-pad.double-tap", MiddlePadDoubleTap);
        Run("middle-pad.hold", MiddlePadHold);
        Run("multitouch.two-finger-swipe", MultiTouchTwoFingerSwipe);
        Run("multitouch.hold", MultiTouchHold);
        Run("multitouch.rejects-wide-contact-set", MultiTouchRejectsWideContactSet);
        Run("custom-shape.translation-scale-normalization", ShapeNormalization);
        Run("custom-shape.single-stroke-match", ShapeSingleStrokeMatch);
        Run("custom-shape.recording-completion", ShapeRecordingCompletion);
        Run("custom-shape.multi-finger-path-match", ShapeSequenceMultiFingerMatch);
        Run("geometry.control-center-four-sides", ControlCenterFourSides);
        Run("geometry.bottom-edge-thresholds", BottomEdgeThresholds);
        Run("geometry.reveal-and-dial-index", RevealAndDialIndex);
        Run("relay.idle-reset-guard", RelayIdleResetGuard);
        Run("wheel.default-action-labels", WheelDefaultActionLabels);

        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        Environment.ExitCode = failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine($"FAIL {name}: {error}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void WheelDefaultActionLabels()
    {
        Localization.Use("zh-CN");
        List<GestureAction> actions = Settings.Defaults();
        string[] expected =
        {
            "任务视图", "控制中心", "下个应用", "应用设置",
            "显示桌面", "切换方舟", "上个应用", "虚拟 Dial"
        };

        for (int i = 0; i < expected.Length; i++)
            Equal(expected[i], WheelDrawing.ResolveLabel(actions[i]), $"default wheel label {i}");

        GestureAction custom = GestureAction.BuiltIn("TaskView") { Label = "自定义任务" };
        Equal("自定义任务", WheelDrawing.ResolveLabel(custom), "custom label takes precedence");

        custom.Label = "  ";
        Equal("任务视图", WheelDrawing.ResolveLabel(custom), "whitespace label uses fallback");
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
    }

    private static void Near(double expected, double actual, string message, double tolerance = 0.001)
    {
        if (Math.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"{message}: expected={expected}, actual={actual}");
    }

    private static void TouchpadSingleFingerClick()
    {
        List<PadEvent> events = new();
        TouchpadGesture gesture = new(events.Add);
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Feed(583, 1, 100, 100, 50);
        Equal(1, events.Count, "click event count");
        Equal("Click", events[0].Kind, "click event kind");
        Equal(513, events[0].Button, "left-click virtual key");
    }

    private static void TouchpadDoubleTapBinding()
    {
        List<string> calls = new();
        TouchpadGesture gesture = new(_ => { },
            (fingers, name) => fingers == 1 && name == "DoubleTap",
            (_, name) => calls.Add(name));
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Feed(583, 1, 100, 100, 40);
        gesture.Feed(582, 1, 100, 100, 100);
        gesture.Feed(583, 1, 100, 100, 140);
        Equal(1, calls.Count, "double-tap callback count");
        Equal("DoubleTap", calls[0], "double-tap callback");
    }

    private static void TouchpadPinchOutBinding()
    {
        List<string> calls = new();
        TouchpadGesture gesture = new(_ => { },
            (fingers, name) => fingers == 2 && name is "PinchOut" or "PinchIn",
            (_, name) => calls.Add(name));
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Feed(582, 2, 200, 100, 1);
        gesture.Feed(581, 1, 80, 100, 50);
        gesture.Feed(581, 2, 220, 100, 60);
        Assert(calls.Count > 0, "pinch callback was not emitted");
        Assert(calls.All(x => x == "PinchOut"), "pinch direction was not outward");
    }

    private static void TouchpadTwoFingerScroll()
    {
        List<PadEvent> events = new();
        TouchpadGesture gesture = new(events.Add);
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Feed(582, 2, 200, 100, 1);
        gesture.Feed(581, 1, 100, 120, 50);
        gesture.Feed(581, 2, 200, 120, 60);
        PadEvent wheel = events.LastOrDefault(x => x.Kind == "Wheel");
        Equal("Wheel", wheel.Kind, "two-finger scroll event");
        Near(-30, wheel.Y, "two-finger scroll direction and scale");
    }

    private static void TouchpadThreeFingerScroll()
    {
        List<PadEvent> events = new();
        TouchpadGesture gesture = new(events.Add);
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Feed(582, 2, 150, 100, 10);
        gesture.Feed(582, 3, 125, 100, 20);
        gesture.Feed(581, 3, 125, 120, 160);
        PadEvent wheel = events.LastOrDefault(x => x.Kind == "Wheel");
        Equal("Wheel", wheel.Kind, "three-finger scroll event");
        Near(-60, wheel.Y, "three-finger scroll scale");
    }

    private static void TouchpadTwoFingerSwipe()
    {
        List<string> calls = new();
        TouchpadGesture gesture = new(_ => { },
            (fingers, name) => fingers == 2 && name == "Right",
            (_, name) => calls.Add(name));
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Feed(582, 2, 200, 100, 10);
        gesture.Feed(581, 1, 150, 100, 50);
        gesture.Feed(581, 2, 250, 100, 60);
        gesture.Feed(583, 1, 150, 100, 70);
        gesture.Feed(583, 2, 250, 100, 80);
        Equal(1, calls.Count, "two-finger swipe callback count");
        Equal("Right", calls[0], "two-finger swipe direction");
    }

    private static void TouchpadHoldBinding()
    {
        List<string> calls = new();
        TouchpadGesture gesture = new(_ => { },
            (fingers, name) => fingers == 1 && name == "Hold",
            (_, name) => calls.Add(name));
        gesture.Feed(582, 1, 100, 100, 0);
        gesture.Tick(500);
        Equal(1, calls.Count, "hold callback count");
        Equal("Hold", calls[0], "hold callback");
    }

    private static void MiddlePadDoubleTap()
    {
        List<string> calls = new();
        TouchpadMiddleGesture gesture = new(
            name => name == "DoubleTap",
            (name, _) => calls.Add(name),
            () => 500);
        gesture.Down(10, 10, 0);
        gesture.Up(10, 10, 20);
        gesture.Down(10, 10, 100);
        gesture.Up(10, 10, 120);
        Equal(1, calls.Count, "middle-area double-tap callback count");
        Equal("DoubleTap", calls[0], "middle-area double-tap callback");
    }

    private static void MiddlePadHold()
    {
        List<string> calls = new();
        TouchpadMiddleGesture gesture = new(
            name => name == "Hold",
            (name, _) => calls.Add(name),
            () => 300);
        gesture.Down(10, 10, 0);
        gesture.Tick(300);
        gesture.Up(10, 10, 350);
        Equal(1, calls.Count, "middle-area hold callback count");
        Equal("Hold", calls[0], "middle-area hold callback");
    }

    private static void MultiTouchTwoFingerSwipe()
    {
        MultiTouchRecognizer recognizer = new();
        recognizer.Feed(1, 100, 100, down: true, up: false, cancelled: false, 0, 500, 200);
        recognizer.Feed(2, 150, 100, down: true, up: false, cancelled: false, 20, 500, 200);
        recognizer.Feed(1, 170, 100, down: false, up: false, cancelled: false, 80, 500, 200);
        recognizer.Feed(2, 220, 100, down: false, up: false, cancelled: false, 90, 500, 200);
        recognizer.Feed(1, 170, 100, down: false, up: true, cancelled: false, 100, 500, 200);
        MultiTouchRecognizer.Match? match = recognizer.Feed(2, 220, 100, down: false, up: true, cancelled: false, 110, 500, 200);
        Assert(match.HasValue, "two-finger swipe did not match");
        Equal(2, match.Value.Fingers, "matched finger count");
        Equal("Right", match.Value.Gesture, "matched swipe direction");
    }

    private static void MultiTouchHold()
    {
        MultiTouchRecognizer recognizer = new();
        recognizer.Feed(1, 100, 100, true, false, false, 0, 500, 200);
        recognizer.Feed(2, 150, 100, true, false, false, 10, 500, 200);
        recognizer.Feed(1, 100, 100, false, true, false, 250, 500, 200);
        MultiTouchRecognizer.Match? match = recognizer.Feed(2, 150, 100, false, true, false, 260, 500, 200);
        Assert(match.HasValue, "two-finger hold did not match");
        Equal("Hold", match.Value.Gesture, "matched hold gesture");
    }

    private static void MultiTouchRejectsWideContactSet()
    {
        MultiTouchRecognizer recognizer = new();
        recognizer.Feed(1, 0, 0, true, false, false, 0, 50, 200);
        recognizer.Feed(2, 100, 0, true, false, false, 10, 50, 200);
        recognizer.Feed(1, 0, 0, false, true, false, 30, 50, 200);
        MultiTouchRecognizer.Match? match = recognizer.Feed(2, 100, 0, false, true, false, 40, 50, 200);
        Assert(!match.HasValue, "too-wide contact set should be rejected");
    }

    private static List<GesturePoint> HorizontalStroke(double offsetX = 0, double offsetY = 0, double scale = 1)
    {
        return new List<GesturePoint>
        {
            new(offsetX, offsetY),
            new(offsetX + 50 * scale, offsetY),
            new(offsetX + 100 * scale, offsetY)
        };
    }

    private static void ShapeNormalization()
    {
        List<GesturePoint> first = ShapeGesture.Normalize(HorizontalStroke());
        List<GesturePoint> transformed = ShapeGesture.Normalize(HorizontalStroke(350, -90, 2.5));
        Equal(64, first.Count, "normalized sample count");
        Equal(64, transformed.Count, "transformed normalized sample count");
        for (int i = 0; i < 64; i++)
        {
            Near(first[i].X, transformed[i].X, $"normalized X sample {i}", 0.00001);
            Near(first[i].Y, transformed[i].Y, $"normalized Y sample {i}", 0.00001);
        }
    }

    private static void ShapeSingleStrokeMatch()
    {
        List<GesturePoint> template = HorizontalStroke();
        MultiTouchBinding binding = new()
        {
            Fingers = 2,
            Gesture = "Custom",
            Points = template,
            Action = new GestureAction { Kind = ActionKind.Keys, Keys = new List<int> { 513 } }
        };
        MultiTouchBinding? match = ShapeGesture.Match(HorizontalStroke(120, 80, 1.8), 2, new[] { binding });
        Assert(ReferenceEquals(binding, match), "translated/scaled custom stroke did not match its template");
        Assert(ShapeGesture.Match(new[] { new GesturePoint(0, 0), new GesturePoint(20, 0) }, 2, new[] { binding }) is null,
            "short custom stroke should not match");
    }

    private static void ShapeRecordingCompletion()
    {
        ShapeRecording recording = new();
        recording.Down(1, 0, 0, 0, 2, 200);
        recording.Down(2, 0, 10, 10, 2, 200);
        recording.Move(1, 100, 0);
        recording.Move(2, 100, 10);
        recording.Up(1);
        Assert(!recording.Complete, "recording must wait for all contacts to release");
        recording.Up(2);
        Assert(recording.Complete, "valid two-contact shape recording did not complete");
        Equal(2, recording.Paths.Count, "recorded stroke count");
    }

    private static void ShapeSequenceMultiFingerMatch()
    {
        MultiTouchBinding binding = new()
        {
            Fingers = 2,
            Gesture = "Custom",
            Paths = new List<List<GesturePoint>>
            {
                HorizontalStroke(),
                HorizontalStroke(0, 10)
            },
            Action = new GestureAction { Kind = ActionKind.Keys, Keys = new List<int> { 514 } }
        };
        MultiTouchSettings options = new()
        {
            MaxSpacing = 200,
            MatchStrictness = 60,
            Bindings = new List<MultiTouchBinding> { binding }
        };
        ShapeSequence sequence = new();
        sequence.Feed(1, 0, 0, down: true, up: false, cancel: false, 0, options);
        sequence.Feed(2, 0, 10, down: true, up: false, cancel: false, 10, options);
        sequence.Feed(1, 100, 0, down: false, up: false, cancel: false, 50, options);
        sequence.Feed(2, 100, 10, down: false, up: false, cancel: false, 60, options);
        sequence.Feed(1, 100, 0, down: false, up: true, cancel: false, 70, options);
        MultiTouchBinding? match = sequence.Feed(2, 100, 10, down: false, up: true, cancel: false, 80, options);
        Assert(ReferenceEquals(binding, match), "two-finger custom path sequence did not match");
    }

    private static void ControlCenterFourSides()
    {
        ControlCenterLayout left = ControlCenterGeometry.Compute(100, 500, 0, 0, 1920, 1080, 0, 0, 1920, 1080, 1, PanelSide.Left);
        ControlCenterLayout right = ControlCenterGeometry.Compute(1800, 500, 0, 0, 1920, 1080, 0, 0, 1920, 1080, 1, PanelSide.Right);
        ControlCenterLayout top = ControlCenterGeometry.Compute(100, 500, 0, 0, 1920, 1080, 0, 0, 1920, 1080, 1, PanelSide.Top);
        ControlCenterLayout bottom = ControlCenterGeometry.Compute(100, 500, 0, 0, 1920, 1080, 0, 0, 1920, 1080, 1, PanelSide.Bottom);
        Equal(12, left.TargetX, "left panel target X");
        Equal(1458, right.TargetX, "right panel target X");
        Equal(-850, top.OffY, "top panel off-screen Y");
        Equal(1080, bottom.OffY, "bottom panel off-screen Y");
        Equal(450, left.Width, "panel width");
        Equal(850, left.Height, "panel height");
        Equal(12, top.Y(1), "top panel revealed Y");
        Equal(12, bottom.Y(1), "bottom panel revealed Y");
    }

    private static void BottomEdgeThresholds()
    {
        Near(0, BottomGesture.BlurProgress(24), "card blur start");
        Near(1, BottomGesture.BlurProgress(204), "card blur completion");
        Assert(!BottomGesture.StartMenuReached(449, 1080, 1), "start menu threshold should not trigger early");
        Assert(BottomGesture.StartMenuReached(450, 1080, 1), "start menu threshold should trigger at 450 DIP");
        Equal(0, BottomGesture.Release(84, 1079, 0, 1080, 1), "short release should dismiss");
        Equal(1, BottomGesture.Release(85, 1079, 0, 1080, 1), "longer release should commit cards");
    }

    private static void RevealAndDialIndex()
    {
        Near(0, GestureGeometry.Reveal(-10, 100), "negative reveal clamp");
        Near(0.5, GestureGeometry.Reveal(50, 100), "half reveal");
        Near(1, GestureGeometry.Reveal(200, 100), "upper reveal clamp");
        Equal(0, GestureGeometry.DialIndex(-90, 8), "dial top segment");
        Equal(2, GestureGeometry.DialIndex(0, 8), "dial right segment");
        Equal(4, GestureGeometry.DialIndex(90, 8), "dial bottom segment");
        Equal(6, GestureGeometry.DialIndex(180, 8), "dial left segment");
    }

    private static void RelayIdleResetGuard()
    {
        Equal((uint)1500, RelayRecovery.RetryDelay(1), "first relay retry delay");
        Equal((uint)3000, RelayRecovery.RetryDelay(2), "second relay retry delay");
        Equal((uint)30000, RelayRecovery.RetryDelay(6), "relay retry cap");
        Assert(RelayRecovery.CanReset(0, 60000, 0, onlyDown: true), "idle first-down reset should be allowed");
        Assert(!RelayRecovery.CanReset(1, 60000, 0, onlyDown: true), "active contacts must prevent reset");
        Assert(!RelayRecovery.CanReset(0, 59999, 0, onlyDown: true), "short idle must prevent reset");
        Assert(!RelayRecovery.CanReset(0, 60000, 5, onlyDown: true), "unexpected error must prevent reset");
    }
}
