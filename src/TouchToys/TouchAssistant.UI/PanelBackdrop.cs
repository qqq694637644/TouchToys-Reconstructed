using System;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.System.Power;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.ViewManagement;

namespace TouchAssistant.UI;

internal sealed class PanelBackdrop : SystemBackdrop
{
	private sealed class BackdropTarget : Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop
	{
		private readonly Action<Windows.UI.Composition.CompositionBrush?> changed;

		private Windows.UI.Composition.CompositionBrush? brush;

		public Windows.UI.Composition.CompositionBrush SystemBackdrop
		{
			get
			{
				return brush;
			}
			set
			{
				brush = value;
				changed(value);
			}
		}

		public BackdropTarget(Action<Windows.UI.Composition.CompositionBrush?> changed)
		{
			this.changed = changed;
		}
	}

	private DesktopAcrylicController? controller;

	private SystemBackdropConfiguration? configuration;

	private XamlRoot? connectedRoot;

	private Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop? connectedTarget;

	private BackdropTarget? controllerTarget;

	private Windows.UI.Composition.CompositionBrush? transparentFallback;

	private Windows.UI.Composition.CompositionMaskBrush? opacityBrush;

	private Windows.UI.Composition.CompositionColorBrush? opacityMask;

	private Windows.UI.Composition.CompositionVisualSurface? maskSurface;

	private Windows.UI.Composition.CompositionSurfaceBrush? maskSurfaceBrush;

	private Windows.UI.Composition.ContainerVisual? maskVisual;

	private Windows.UI.Composition.CompositionRoundedRectangleGeometry? roundedGeometry;

	private bool roundedMask;

	private Vector2 maskHostSize;

	private Vector2 maskContentSize;

	private float maskRadius;

	private float opacity = 1f;

	internal bool LightTint { get; init; }

	internal bool PreserveAcrylicOpacity { get; init; }

	internal float TintOpacityOverride { get; init; } = -1f;

	internal float LuminosityOpacityOverride { get; init; } = -1f;

	internal string Diagnostics => $"State={controller?.State}; Theme={configuration?.Theme}; Tint={controller?.TintOpacity}; Luminosity={controller?.LuminosityOpacity}; Alpha={opacity}; Active={configuration?.IsInputActive}; HighContrast={configuration?.IsHighContrast}; AdvancedEffects={new UISettings().AdvancedEffectsEnabled}; EnergySaver={PowerManager.EnergySaverStatus}";

	internal void SetRoundedBounds(double hostWidth, double hostHeight, double width, double height, double scale)
	{
		roundedMask = true;
		maskHostSize = new Vector2((float)(hostWidth * scale), (float)(hostHeight * scale));
		maskContentSize = new Vector2((float)(width * scale), (float)(height * scale));
		maskRadius = (float)(10.0 * scale);
		ApplyOpacity();
	}

	internal void SetOpacity(double value)
	{
		opacity = (float)Math.Clamp(value, 0.0, 1.0);
		ApplyOpacity();
	}

	private void ApplyOpacity()
	{
		if (connectedTarget == null)
		{
			return;
		}
		if (!PreserveAcrylicOpacity && (double)opacity < 0.999 && transparentFallback != null)
		{
			try
			{
				connectedTarget.SystemBackdrop = transparentFallback;
				return;
			}
			catch
			{
				return;
			}
		}
		Windows.UI.Composition.CompositionBrush compositionBrush = controllerTarget?.SystemBackdrop;
		if ((object)compositionBrush == null)
		{
			if (transparentFallback != null)
			{
				connectedTarget.SystemBackdrop = transparentFallback;
			}
			return;
		}
		if (opacityBrush == null && opacity >= 1f && !roundedMask)
		{
			connectedTarget.SystemBackdrop = compositionBrush;
			return;
		}
		if (opacityBrush == null)
		{
			Windows.UI.Composition.Compositor compositor = compositionBrush.Compositor;
			opacityMask = compositor.CreateColorBrush();
			if (roundedMask)
			{
				Windows.UI.Composition.ShapeVisual shapeVisual = compositor.CreateShapeVisual();
				roundedGeometry = compositor.CreateRoundedRectangleGeometry();
				Windows.UI.Composition.CompositionSpriteShape compositionSpriteShape = compositor.CreateSpriteShape(roundedGeometry);
				compositionSpriteShape.FillBrush = opacityMask;
				shapeVisual.Shapes.Add(compositionSpriteShape);
				maskVisual = shapeVisual;
			}
			else
			{
				Windows.UI.Composition.SpriteVisual spriteVisual = compositor.CreateSpriteVisual();
				spriteVisual.Brush = opacityMask;
				maskVisual = spriteVisual;
			}
			maskSurface = compositor.CreateVisualSurface();
			maskSurface.SourceVisual = maskVisual;
			maskSurfaceBrush = compositor.CreateSurfaceBrush(maskSurface);
			maskSurfaceBrush.Stretch = Windows.UI.Composition.CompositionStretch.Fill;
			opacityBrush = compositor.CreateMaskBrush();
			opacityBrush.Mask = maskSurfaceBrush;
		}
		opacityBrush.Source = compositionBrush;
		connectedTarget.SystemBackdrop = opacityBrush;
		maskVisual.Size = (roundedMask ? maskHostSize : new Vector2(1f, 1f));
		maskSurface.SourceSize = maskVisual.Size;
		if (roundedGeometry != null)
		{
			roundedGeometry.Offset = (maskHostSize - maskContentSize) / 2f;
			roundedGeometry.Size = maskContentSize;
			roundedGeometry.CornerRadius = new Vector2(maskRadius);
		}
		opacityMask.Color = Color.FromArgb((byte)Math.Round(opacity * 255f), byte.MaxValue, byte.MaxValue, byte.MaxValue);
	}

	protected override void OnTargetConnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target, XamlRoot root)
	{
		base.OnTargetConnected(target, root);
		GetDefaultSystemBackdropConfiguration(target, root);
		connectedRoot = root;
		connectedTarget = target;
		if (root.Content is FrameworkElement frameworkElement)
		{
			frameworkElement.ActualThemeChanged += ContentThemeChanged;
		}
		configuration = new SystemBackdropConfiguration
		{
			IsInputActive = true
		};
		try
		{
			transparentFallback = new Windows.UI.Composition.Compositor().CreateColorBrush(Color.FromArgb(0, 0, 0, 0));
		}
		catch
		{
			transparentFallback = null;
		}
		Refresh();
		if (!DesktopAcrylicController.IsSupported())
		{
			if (connectedTarget != null && transparentFallback != null)
			{
				connectedTarget.SystemBackdrop = transparentFallback;
			}
			return;
		}
		controller = new DesktopAcrylicController
		{
			Kind = DesktopAcrylicKind.Thin
		};
		controllerTarget = new BackdropTarget(delegate(Windows.UI.Composition.CompositionBrush? brush)
		{
			if (brush == null)
			{
				if (connectedTarget != null)
				{
					connectedTarget.SystemBackdrop = null;
				}
			}
			else
			{
				ApplyOpacity();
			}
		});
		controller.AddSystemBackdropTarget(controllerTarget);
		controller.SetSystemBackdropConfiguration(configuration);
		Refresh();
	}

	protected override void OnDefaultSystemBackdropConfigurationChanged(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target, XamlRoot root)
	{
		Refresh();
	}

	internal void Refresh()
	{
		if (configuration == null || connectedRoot == null)
		{
			return;
		}
		configuration.IsInputActive = true;
		configuration.Theme = ((!(connectedRoot.Content is FrameworkElement { ActualTheme: ElementTheme.Dark })) ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark);
		configuration.IsHighContrast = new AccessibilitySettings().HighContrast;
		if (LightTint && controller != null)
		{
			controller.TintColor = Color.FromArgb(byte.MaxValue, 24, 24, 28);
			controller.TintOpacity = 0.35f;
			controller.LuminosityOpacity = 0.2f;
		}
		if (controller != null)
		{
			if (TintOpacityOverride >= 0f)
			{
				controller.TintOpacity = TintOpacityOverride;
			}
			if (LuminosityOpacityOverride >= 0f)
			{
				controller.LuminosityOpacity = LuminosityOpacityOverride;
			}
		}
		ApplyOpacity();
	}

	internal void PrepareForShow()
	{
		Refresh();
	}

	private void ContentThemeChanged(FrameworkElement sender, object args)
	{
		Refresh();
	}

	protected override void OnTargetDisconnected(Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop target)
	{
		if (connectedRoot?.Content is FrameworkElement frameworkElement)
		{
			frameworkElement.ActualThemeChanged -= ContentThemeChanged;
		}
		connectedRoot = null;
		connectedTarget = null;
		configuration = null;
		if (controllerTarget != null)
		{
			controller?.RemoveSystemBackdropTarget(controllerTarget);
		}
		controllerTarget = null;
		controller?.Dispose();
		controller = null;
		opacityBrush?.Dispose();
		opacityBrush = null;
		opacityMask?.Dispose();
		opacityMask = null;
		transparentFallback?.Dispose();
		transparentFallback = null;
		maskSurfaceBrush?.Dispose();
		maskSurfaceBrush = null;
		maskSurface?.Dispose();
		maskSurface = null;
		maskVisual?.Dispose();
		maskVisual = null;
		roundedGeometry?.Dispose();
		roundedGeometry = null;
		base.OnTargetDisconnected(target);
	}
}
