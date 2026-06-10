using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using CanvasDirectXAlphaMode = Microsoft.Graphics.DirectX.DirectXAlphaMode;
using CanvasDirectXPixelFormat = Microsoft.Graphics.DirectX.DirectXPixelFormat;
using WinDirectXPixelFormat = Windows.Graphics.DirectX.DirectXPixelFormat;

namespace TodoApp;

internal static class LiquidBackdropEffect
{
    private const string BackdropSourceName = "Backdrop";
    private const string DisplacementMapSourceName = "DisplacementMap";
    private const string LiquidDisplacementEffectName = "LiquidDisplacement";
    private const string LiquidBackdropTransformEffectName = "LiquidBackdropTransform";
    private const string LiquidMaterialBlurEffectName = "LiquidMaterialBlur";
    private const string LiquidDisplacementAmountProperty = "LiquidDisplacement.Amount";
    private const string LiquidBackdropTransformMatrixProperty = "LiquidBackdropTransform.TransformMatrix";
    private const string LiquidSampleBaseXProperty = "LiquidSampleBaseX";
    private const string LiquidSampleBaseYProperty = "LiquidSampleBaseY";
    private const string GlintPeakProperty = "LiquidGlintPeak";
    private const string GlintRestProperty = "LiquidGlintRest";
    private const string GlintScaleXProperty = "LiquidGlintScaleX";
    private const string GlintScaleYProperty = "LiquidGlintScaleY";
    private const string BaseRotationProperty = "LiquidBaseRotation";
    private const string PassiveRimLayerProperty = "LiquidPassiveRim";
    private const byte NeutralDisplacementChannel = 128;
    private const int DisplacementMapSize = 128;
    private const int GlintLayerCount = 4;
    private const int EdgeBandCount = 4;
    private const int SamplePassCount = 3;
    private const float BackdropSamplePull = 0.96f;
    private const float ChromaticBackdropSamplePull = 1.10f;
    private const float DisplacementAmountFloor = 8.0f;
    private const float DisplacementAmountCeiling = 50.0f;
    private const float ChromaticDisplacementAmountCeiling = 56.0f;

    private static readonly ConditionalWeakTable<Compositor, DisplacementMapCache> CurvedDisplacementMaps = new();

    private enum ChromaticChannel
    {
        Cyan,
        Warm
    }

    private enum RimEdge
    {
        Top,
        Bottom,
        Left,
        Right
    }

    public static void Attach(FrameworkElement host, string? profileName)
    {
        if (!VisualThemeManager.CurrentDefinition.UsesLiquidBackdrop)
        {
            Detach(host);
            return;
        }

        var profile = DistortionProfile.FromTag(profileName);
        var isDarkTheme = host.ActualTheme == ElementTheme.Dark;
        var hostVisual = ElementCompositionPreview.GetElementVisual(host);
        var compositor = hostVisual.Compositor;
        var root = compositor.CreateContainerVisual();

        BindSize(compositor, root, hostVisual);
        ApplyRoundedClip(compositor, hostVisual, profile.CornerRadius);
        ApplyRoundedClip(compositor, root, profile.CornerRadius);

        AddSoftMaterialTintPass(compositor, root, profile.Material, isDarkTheme);
        AddPass(compositor, root, new Vector2(0.0f, 0.0f), profile.BaseOpacity, driftAmount: 1.1f, durationSeconds: 9.0);
        AddPass(compositor, root, new Vector2(-15.5f, 5.8f), profile.CoolOpacity, driftAmount: 3.2f, durationSeconds: 7.4);
        AddPass(compositor, root, new Vector2(16.8f, -6.4f), profile.WarmOpacity, driftAmount: 2.8f, durationSeconds: 8.6);
        AddLensPasses(compositor, root, profile.LensOpacity, profile.Curvature);
        AddPrismRefractionPasses(compositor, root, profile.PrismOpacity, profile.Curvature);
        AddEdgePasses(compositor, root, profile.EdgeOpacity, profile.Curvature);
        AddCornerLensingPasses(compositor, root, profile.CornerLens, profile.Curvature);
        AddObliqueRefractionPasses(compositor, root, profile.Oblique, profile.Curvature);
        AddDepthRimPasses(compositor, root, profile.Rim);
        AddMeniscusSheenPasses(compositor, root, profile.GlintAlphaScale, profile.Highlight);
        AddAmbientReflectionPasses(compositor, root, profile.GlintAlphaScale, profile.Highlight);
        AddMicroCausticPasses(compositor, root, profile.GlintAlphaScale, profile.Highlight);
        AddSpecularGlint(compositor, root, profile.GlintAlphaScale, profile.Highlight);

        ElementCompositionPreview.SetElementChildVisual(host, root);
    }

    public static void Detach(FrameworkElement host)
    {
        ElementCompositionPreview.GetElementVisual(host).Clip = null;
        ElementCompositionPreview.SetElementChildVisual(host, null);
    }

    public static void RefreshTree(DependencyObject root)
    {
        RefreshTree(root, attach: VisualThemeManager.CurrentDefinition.UsesLiquidBackdrop);
    }

    private static void RefreshTree(DependencyObject node, bool attach)
    {
        if (node is FrameworkElement element && IsBackdropHost(element))
        {
            if (attach && element.IsLoaded && element.ActualWidth > 0 && element.ActualHeight > 0)
            {
                Attach(element, element.Tag as string);
            }
            else
            {
                Detach(element);
            }
        }

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            RefreshTree(VisualTreeHelper.GetChild(node, i), attach);
        }
    }

    public static void AnimatePointerResponse(
        FrameworkElement surface,
        Point pointerPosition,
        float offsetStrength,
        float scale,
        double durationMilliseconds)
    {
        var targetOffset = Vector3.Zero;
        var returnDirection = Vector3.Zero;
        var animatedHostCount = AnimateClosestBackdropHosts(surface, targetOffset, returnDirection, targetScale: 1.0f, durationMilliseconds);
        if (animatedHostCount == 0)
        {
            AnimateNearestBackdropHosts(surface, targetOffset, returnDirection, targetScale: 1.0f, durationMilliseconds);
        }
    }

    private static void AddPass(
        Compositor compositor,
        ContainerVisual root,
        Vector2 sampleOffset,
        float opacity,
        float driftAmount,
        double durationSeconds)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = CreateRefractedBackdropBrush(compositor, sampleOffset);
        sprite.Opacity = opacity;
        sprite.Offset = Vector3.Zero;

        BindSize(compositor, sprite, root);
        BindCenterPoint(compositor, sprite);
        StartOffsetDrift(compositor, sprite, Vector2.Zero, driftAmount * 0.18f, durationSeconds);
        StartOpacityBreath(compositor, sprite, opacity, durationSeconds + 1.8);
        root.Children.InsertAtTop(sprite);
    }

    private static int AnimateClosestBackdropHosts(
        DependencyObject node,
        Vector3 targetOffset,
        Vector3 returnDirection,
        float targetScale,
        double durationMilliseconds)
    {
        var closestDepth = FindClosestBackdropDepth(node, currentDepth: 0);
        if (closestDepth < 0)
        {
            return 0;
        }

        return AnimateBackdropHostsAtDepth(
            node,
            currentDepth: 0,
            targetDepth: closestDepth,
            targetOffset,
            returnDirection,
            targetScale,
            durationMilliseconds);
    }

    private static int FindClosestBackdropDepth(DependencyObject node, int currentDepth)
    {
        if (node is FrameworkElement element
            && IsBackdropHost(element)
            && ElementCompositionPreview.GetElementChildVisual(element) is not null)
        {
            return currentDepth;
        }

        var closestDepth = -1;
        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            var childDepth = FindClosestBackdropDepth(VisualTreeHelper.GetChild(node, i), currentDepth + 1);
            if (childDepth >= 0 && (closestDepth < 0 || childDepth < closestDepth))
            {
                closestDepth = childDepth;
            }
        }

        return closestDepth;
    }

    private static int AnimateBackdropHostsAtDepth(
        DependencyObject node,
        int currentDepth,
        int targetDepth,
        Vector3 targetOffset,
        Vector3 returnDirection,
        float targetScale,
        double durationMilliseconds)
    {
        var animatedHostCount = 0;

        if (currentDepth == targetDepth
            && node is FrameworkElement element
            && IsBackdropHost(element)
            && ElementCompositionPreview.GetElementChildVisual(element) is Visual visual)
        {
            AnimateHostVisual(element, visual, targetOffset, returnDirection, targetScale, durationMilliseconds);
            AnimateGlintVisual(visual, targetOffset, targetScale, durationMilliseconds);
            AnimateOpticalLayers(visual, targetOffset, durationMilliseconds);
            animatedHostCount++;
        }

        if (currentDepth >= targetDepth)
        {
            return animatedHostCount;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < childCount; i++)
        {
            animatedHostCount += AnimateBackdropHostsAtDepth(
                VisualTreeHelper.GetChild(node, i),
                currentDepth + 1,
                targetDepth,
                targetOffset,
                returnDirection,
                targetScale,
                durationMilliseconds);
        }

        return animatedHostCount;
    }

    private static void AnimateNearestBackdropHosts(
        DependencyObject source,
        Vector3 targetOffset,
        Vector3 returnDirection,
        float targetScale,
        double durationMilliseconds)
    {
        var parent = VisualTreeHelper.GetParent(source);
        for (var depth = 0; depth < 7 && parent is not null; depth++)
        {
            if (AnimateClosestBackdropHosts(parent, targetOffset, returnDirection, targetScale, durationMilliseconds) > 0)
            {
                return;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }
    }

    private static bool IsBackdropHost(FrameworkElement element)
    {
        return element.Tag is "Card" or "Lane" or "Toolbar" or "Panel" or "Sidebar" or "TitleCapsule" or "ContentPanel" or "Flyout";
    }

    private static void AddDiffuseMaterialPass(
        Compositor compositor,
        ContainerVisual root,
        SoftMaterialProfile material)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = CreateBlurredBackdropBrush(compositor, material.BlurAmount, material.Saturation);
        sprite.Opacity = material.Opacity;
        sprite.Offset = new Vector3(-material.Overscan, -material.Overscan, 0);

        BindOverscanSize(compositor, sprite, root, material.Overscan);
        BindCenterPoint(compositor, sprite);
        StartOpacityBreath(compositor, sprite, material.Opacity, 10.8);
        root.Children.InsertAtTop(sprite);
    }

    private static void AddSoftMaterialTintPass(
        Compositor compositor,
        ContainerVisual root,
        SoftMaterialProfile material,
        bool isDarkTheme)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = CreateSoftMaterialTintBrush(compositor, isDarkTheme);
        sprite.Opacity = isDarkTheme
            ? Math.Clamp(material.Opacity * 0.58f, 0.030f, 0.118f)
            : Math.Clamp(material.Opacity * 0.74f, 0.042f, 0.150f);
        sprite.Offset = new Vector3(-material.Overscan, -material.Overscan, 0);

        BindOverscanSize(compositor, sprite, root, material.Overscan);
        StartOpacityBreath(compositor, sprite, sprite.Opacity, 11.2);
        root.Children.InsertAtBottom(sprite);
    }

    private static CompositionBrush CreateSoftMaterialTintBrush(Compositor compositor, bool isDarkTheme)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.0f, 0.0f);
        brush.EndPoint = new Vector2(1.0f, 1.0f);

        if (isDarkTheme)
        {
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(44, 8, 14, 22)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.30f, Color.FromArgb(22, 90, 210, 255)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(0.68f, Color.FromArgb(20, 255, 122, 217)));
            brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(38, 14, 24, 32)));
            return brush;
        }

        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(54, 255, 255, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.34f, Color.FromArgb(22, 178, 232, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.68f, Color.FromArgb(24, 255, 250, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(34, 238, 255, 248)));
        return brush;
    }

    private static void AnimateHostVisual(
        FrameworkElement host,
        Visual visual,
        Vector3 targetOffset,
        Vector3 returnDirection,
        float targetScale,
        double durationMilliseconds)
    {
        var compositor = visual.Compositor;
        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        visual.CenterPoint = new Vector3((float)host.ActualWidth / 2.0f, (float)host.ActualHeight / 2.0f, 0);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Duration = duration;
        offsetAnimation.InsertKeyFrame(1.0f, Vector3.Zero);
        visual.StartAnimation("Offset", offsetAnimation);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Duration = duration;
        if (targetScale >= 1.0f)
        {
            scaleAnimation.InsertKeyFrame(0.58f, new Vector3(targetScale + 0.004f, targetScale + 0.004f, 1.0f));
            scaleAnimation.InsertKeyFrame(1.0f, new Vector3(targetScale, targetScale, 1.0f));
        }
        else
        {
            scaleAnimation.InsertKeyFrame(0.58f, new Vector3(targetScale - 0.004f, targetScale - 0.004f, 1.0f));
            scaleAnimation.InsertKeyFrame(1.0f, new Vector3(targetScale, targetScale, 1.0f));
        }
        visual.StartAnimation("Scale", scaleAnimation);
    }

    private static void AnimateGlintVisual(
        Visual rootVisual,
        Vector3 targetOffset,
        float targetScale,
        double durationMilliseconds)
    {
        if (rootVisual is not ContainerVisual root || root.Children.Count == 0)
        {
            return;
        }

        var glints = root.Children
            .OfType<SpriteVisual>()
            .Where(sprite => ReadVisualScalar(sprite, PassiveRimLayerProperty, 0.0f) < 0.5f)
            .Take(GlintLayerCount)
            .ToArray();
        if (glints.Length < GlintLayerCount)
        {
            return;
        }

        var compositor = root.Compositor;
        var duration = TimeSpan.FromMilliseconds(Math.Max(120, durationMilliseconds + 80));
        var isResting = targetOffset.LengthSquared() < 0.01f && Math.Abs(targetScale - 1.0f) < 0.001f;

        AnimateSingleGlint(compositor, glints[0], targetOffset, duration, isResting, offsetMultiplier: -2.46f, yMultiplier: -1.64f);
        AnimateSingleGlint(compositor, glints[1], targetOffset, duration, isResting, offsetMultiplier: -0.92f, yMultiplier: -0.96f);
        AnimateSingleGlint(compositor, glints[2], targetOffset, duration, isResting, offsetMultiplier: -1.88f, yMultiplier: -1.22f);
        AnimateSingleGlint(compositor, glints[3], targetOffset, duration, isResting, offsetMultiplier: -1.40f, yMultiplier: -1.10f);
    }

    private static void AnimateOpticalLayers(
        Visual rootVisual,
        Vector3 targetOffset,
        double durationMilliseconds)
    {
        if (rootVisual is not ContainerVisual root || root.Children.Count == 0)
        {
            return;
        }

        var compositor = root.Compositor;
        var duration = TimeSpan.FromMilliseconds(Math.Max(130, durationMilliseconds + 60));
        var magnitude = Math.Clamp(targetOffset.Length() / 10.0f, 0.0f, 1.25f);
        var isResting = targetOffset.LengthSquared() < 0.01f;

        var lensRegions = root.Children.OfType<ContainerVisual>().ToArray();
        for (var i = 0; i < lensRegions.Length; i++)
        {
            AnimateLensRegion(compositor, lensRegions[i], targetOffset, magnitude, isResting, duration, i);
            AnimateRefractionSampleFlow(compositor, lensRegions[i], targetOffset, magnitude, isResting, duration, i);
            AnimateDisplacementBrushes(compositor, lensRegions[i], magnitude, isResting, duration);
        }

        var sprites = root.Children
            .OfType<SpriteVisual>()
            .Where(sprite => ReadVisualScalar(sprite, PassiveRimLayerProperty, 0.0f) < 0.5f)
            .ToArray();
        var edgeBands = sprites.Skip(GlintLayerCount).Take(EdgeBandCount).ToArray();
        for (var i = 0; i < edgeBands.Length; i++)
        {
            AnimateEdgeBand(compositor, edgeBands[i], targetOffset, magnitude, isResting, duration, i);
            AnimateRefractionSampleFlow(compositor, edgeBands[i], targetOffset, magnitude, isResting, duration, i + 5);
            AnimateDisplacementBrushes(compositor, edgeBands[i], magnitude, isResting, duration);
        }

        var samplePasses = sprites.Skip(GlintLayerCount + EdgeBandCount).Take(SamplePassCount).ToArray();
        for (var i = 0; i < samplePasses.Length; i++)
        {
            AnimateSamplePass(compositor, samplePasses[i], targetOffset, magnitude, isResting, duration, i);
            AnimateRefractionSampleFlow(compositor, samplePasses[i], targetOffset, magnitude, isResting, duration, i + 11);
            AnimateDisplacementBrushes(compositor, samplePasses[i], magnitude, isResting, duration);
        }

    }

    private static void AnimateDisplacementBrushes(
        Compositor compositor,
        Visual visual,
        float magnitude,
        bool isResting,
        TimeSpan duration)
    {
        if (visual is SpriteVisual sprite)
        {
            AnimateDisplacementBrush(compositor, sprite, magnitude, isResting, duration);
            return;
        }

        if (visual is not ContainerVisual container)
        {
            return;
        }

        foreach (var child in container.Children)
        {
            AnimateDisplacementBrushes(compositor, child, magnitude, isResting, duration);
        }
    }

    private static void AnimateDisplacementBrush(
        Compositor compositor,
        SpriteVisual sprite,
        float magnitude,
        bool isResting,
        TimeSpan duration)
    {
        if (sprite.Brush is not CompositionEffectBrush brush)
        {
            return;
        }

        try
        {
            var amount = isResting
                ? DisplacementAmountFloor
                : Math.Clamp(9.5f + magnitude * 12.0f, DisplacementAmountFloor, 24.0f);
            var animation = compositor.CreateScalarKeyFrameAnimation();
            animation.Duration = duration;
            animation.InsertKeyFrame(1.0f, amount);
            brush.Properties.StartAnimation(LiquidDisplacementAmountProperty, animation);
        }
        catch
        {
            // Some effect brushes only wrap a transform fallback and do not expose displacement properties.
        }
    }

    private static void AnimateRefractionSampleFlow(
        Compositor compositor,
        Visual visual,
        Vector3 targetOffset,
        float magnitude,
        bool isResting,
        TimeSpan duration,
        int depth)
    {
        if (visual is SpriteVisual sprite)
        {
            if (sprite.Brush is not CompositionEffectBrush brush)
            {
                return;
            }

            AnimateBackdropSampleTransform(compositor, brush, targetOffset, magnitude, isResting, depth);

            var animation = compositor.CreateVector3KeyFrameAnimation();
            animation.Duration = duration;
            animation.InsertKeyFrame(1.0f, Vector3.Zero);
            sprite.StartAnimation("Translation", animation);
            return;
        }

        if (visual is not ContainerVisual container)
        {
            return;
        }

        var childIndex = 0;
        foreach (var child in container.Children)
        {
            AnimateRefractionSampleFlow(compositor, child, targetOffset, magnitude, isResting, duration, depth + childIndex + 1);
            childIndex++;
        }
    }

    private static void AnimateBackdropSampleTransform(
        Compositor compositor,
        CompositionBrush brush,
        Vector3 targetOffset,
        float magnitude,
        bool isResting,
        int depth)
    {
        if (brush is not CompositionEffectBrush effectBrush)
        {
            return;
        }

        TryAnimateBackdropSampleTransform(compositor, effectBrush, targetOffset, magnitude, isResting, depth);

        try
        {
            if (effectBrush.GetSourceParameter(BackdropSourceName) is CompositionBrush backdropSource)
            {
                AnimateBackdropSampleTransform(compositor, backdropSource, targetOffset, magnitude, isResting, depth + 1);
            }
        }
        catch
        {
            // Not every effect in the chain exposes a backdrop source parameter.
        }
    }

    private static void TryAnimateBackdropSampleTransform(
        Compositor compositor,
        CompositionEffectBrush brush,
        Vector3 targetOffset,
        float magnitude,
        bool isResting,
        int depth)
    {
        try
        {
            if (brush.Properties.TryGetScalar(LiquidSampleBaseXProperty, out var baseX) != CompositionGetValueStatus.Succeeded
                || brush.Properties.TryGetScalar(LiquidSampleBaseYProperty, out var baseY) != CompositionGetValueStatus.Succeeded)
            {
                return;
            }

            var direction = depth % 2 == 0 ? 1.0f : -1.0f;
            var pointerPull = 0.16f + magnitude * 0.10f + Math.Min(depth, 8) * 0.010f;
            var tangentPull = 0.040f + magnitude * 0.040f;
            var targetX = baseX;
            var targetY = baseY;
            if (!isResting)
            {
                var deltaX = targetOffset.X * pointerPull - targetOffset.Y * tangentPull * direction;
                var deltaY = targetOffset.Y * (pointerPull * 0.78f) + targetOffset.X * tangentPull * direction;
                targetX += Math.Clamp(deltaX, -0.48f, 0.48f);
                targetY += Math.Clamp(deltaY, -0.42f, 0.42f);
            }

            var sampleAnimation = compositor.CreateExpressionAnimation("sampleMatrix");
            sampleAnimation.SetMatrix3x2Parameter("sampleMatrix", Matrix3x2.CreateTranslation(targetX, targetY));
            brush.Properties.StartAnimation(LiquidBackdropTransformMatrixProperty, sampleAnimation);
        }
        catch
        {
            // Matrix effect-property animation is opportunistic; the static sample transform still renders correctly.
        }
    }

    private static void AnimateLensRegion(
        Compositor compositor,
        Visual lens,
        Vector3 targetOffset,
        float magnitude,
        bool isResting,
        TimeSpan duration,
        int index)
    {
        var direction = index % 2 == 0 ? 1.0f : -1.0f;
        var scaleX = isResting ? 1.0f : 1.0f + magnitude * (0.042f + index * 0.007f);
        var scaleY = isResting ? 1.0f : 1.0f - magnitude * (0.022f + index * 0.005f);
        var baseRotation = ReadVisualScalar(lens, BaseRotationProperty, 0.0f);
        var rotationDelta = isResting ? 0.0f : direction * ((targetOffset.X * 0.45f) - (targetOffset.Y * 0.19f));
        var rotation = baseRotation + rotationDelta;

        AnimateVector3Property(compositor, lens, "Scale", new Vector3(scaleX, scaleY, 1.0f), duration);
        AnimateScalarProperty(compositor, lens, "RotationAngleInDegrees", rotation, duration);
    }

    private static void AnimateEdgeBand(
        Compositor compositor,
        Visual edge,
        Vector3 targetOffset,
        float magnitude,
        bool isResting,
        TimeSpan duration,
        int index)
    {
        var direction = index % 2 == 0 ? -1.0f : 1.0f;
        var scaleX = isResting ? 1.0f : 1.0f + magnitude * (index < 2 ? 0.086f : 0.034f);
        var scaleY = isResting ? 1.0f : 1.0f + magnitude * (index < 2 ? 0.036f : 0.090f);
        var rotation = isResting ? 0.0f : direction * ((targetOffset.X + targetOffset.Y) * 0.22f);

        AnimateVector3Property(compositor, edge, "Scale", new Vector3(scaleX, scaleY, 1.0f), duration);
        AnimateScalarProperty(compositor, edge, "RotationAngleInDegrees", rotation, duration);
    }

    private static void AnimateSamplePass(
        Compositor compositor,
        Visual sample,
        Vector3 targetOffset,
        float magnitude,
        bool isResting,
        TimeSpan duration,
        int index)
    {
        var direction = index % 2 == 0 ? 1.0f : -1.0f;
        var scaleX = isResting ? 1.0f : 1.0f + magnitude * (0.018f + index * 0.005f);
        var scaleY = isResting ? 1.0f : 1.0f - magnitude * (0.010f + index * 0.003f);
        var rotation = isResting ? 0.0f : direction * ((targetOffset.X * 0.070f) + (targetOffset.Y * 0.050f));

        AnimateVector3Property(compositor, sample, "Scale", new Vector3(scaleX, scaleY, 1.0f), duration);
        AnimateScalarProperty(compositor, sample, "RotationAngleInDegrees", rotation, duration);
    }

    private static void AnimateVector3Property(
        Compositor compositor,
        Visual target,
        string propertyName,
        Vector3 targetValue,
        TimeSpan duration)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(1.0f, targetValue);
        target.StartAnimation(propertyName, animation);
    }

    private static void AnimateScalarProperty(
        Compositor compositor,
        Visual target,
        string propertyName,
        float targetValue,
        TimeSpan duration)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = duration;
        animation.InsertKeyFrame(1.0f, targetValue);
        target.StartAnimation(propertyName, animation);
    }

    private static void AnimateSingleGlint(
        Compositor compositor,
        SpriteVisual glint,
        Vector3 targetOffset,
        TimeSpan duration,
        bool isResting,
        float offsetMultiplier,
        float yMultiplier)
    {
        var opacityPeak = ReadVisualScalar(glint, GlintPeakProperty, 0.22f);
        var opacityRest = ReadVisualScalar(glint, GlintRestProperty, 0.08f);
        var glintScaleX = ReadVisualScalar(glint, GlintScaleXProperty, 1.0f);
        var glintScaleY = ReadVisualScalar(glint, GlintScaleYProperty, 1.0f);
        var baseRotation = ReadVisualScalar(glint, BaseRotationProperty, glint.RotationAngleInDegrees);

        var opacityAnimation = compositor.CreateScalarKeyFrameAnimation();
        opacityAnimation.Duration = duration;
        if (isResting)
        {
            opacityAnimation.InsertKeyFrame(0.45f, opacityPeak * 0.54f);
            opacityAnimation.InsertKeyFrame(1.0f, 0.0f);
        }
        else
        {
            opacityAnimation.InsertKeyFrame(0.0f, 0.0f);
            opacityAnimation.InsertKeyFrame(0.52f, opacityPeak);
            opacityAnimation.InsertKeyFrame(1.0f, opacityRest);
        }
        glint.StartAnimation("Opacity", opacityAnimation);

        var offsetAnimation = compositor.CreateVector3KeyFrameAnimation();
        offsetAnimation.Duration = duration;
        offsetAnimation.InsertKeyFrame(1.0f, new Vector3(targetOffset.X * offsetMultiplier * 0.38f, targetOffset.Y * yMultiplier * 0.38f, 0));
        glint.StartAnimation("Offset", offsetAnimation);

        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.Duration = duration;
        scaleAnimation.InsertKeyFrame(0.55f, new Vector3(glintScaleX * 1.08f, glintScaleY * 0.64f, 1.0f));
        scaleAnimation.InsertKeyFrame(
            1.0f,
            isResting
                ? new Vector3(glintScaleX, glintScaleY * 0.42f, 1.0f)
                : new Vector3(glintScaleX * 1.04f, glintScaleY * 0.52f, 1.0f));
        glint.StartAnimation("Scale", scaleAnimation);

        var rotationDirection = offsetMultiplier < -1.5f ? -1.0f : 1.0f;
        var rotationDelta = isResting
            ? 0.0f
            : rotationDirection * ((targetOffset.X * 0.36f) - (targetOffset.Y * 0.20f));
        var rotationAnimation = compositor.CreateScalarKeyFrameAnimation();
        rotationAnimation.Duration = duration;
        rotationAnimation.InsertKeyFrame(0.52f, baseRotation + rotationDelta * 1.14f);
        rotationAnimation.InsertKeyFrame(1.0f, baseRotation + rotationDelta);
        glint.StartAnimation("RotationAngleInDegrees", rotationAnimation);
    }

    private static float ReadVisualScalar(CompositionObject visual, string propertyName, float fallback)
    {
        try
        {
            return visual.Properties.TryGetScalar(propertyName, out var value) == CompositionGetValueStatus.Succeeded
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void AddSpecularGlint(
        Compositor compositor,
        ContainerVisual root,
        float alphaScale,
        HighlightProfile highlight)
    {
        AddGlintLayer(
            compositor,
            root,
            CreateGlintBrush(compositor, Color.FromArgb(ScaleAlpha(48, alphaScale * highlight.ColorAlphaScale), 255, 95, 210)),
            rotationDegrees: highlight.PrimaryRotation,
            verticalBias: highlight.PrimaryBias,
            highlight.PrimaryPeak,
            highlight.PrimaryRest,
            highlight.ScaleX,
            highlight.ScaleY);

        AddGlintLayer(
            compositor,
            root,
            CreateGlintBrush(compositor, Color.FromArgb(ScaleAlpha(54, alphaScale * highlight.ColorAlphaScale), 90, 210, 255)),
            rotationDegrees: highlight.SecondaryRotation,
            verticalBias: highlight.SecondaryBias,
            highlight.SecondaryPeak,
            highlight.SecondaryRest,
            highlight.ScaleX * 0.94f,
            highlight.ScaleY * 0.86f);

        AddGlintLayer(
            compositor,
            root,
            CreateGlintBrush(compositor, Color.FromArgb(ScaleAlpha(90, alphaScale * highlight.WhiteAlphaScale), 255, 255, 255)),
            rotationDegrees: highlight.HotRotation,
            verticalBias: highlight.HotBias,
            highlight.HotPeak,
            highlight.HotRest,
            highlight.ScaleX * 0.98f,
            highlight.ScaleY * 0.92f);

        AddGlintLayer(
            compositor,
            root,
            CreateFocusGlintBrush(compositor, Color.FromArgb(ScaleAlpha(118, alphaScale * highlight.WhiteAlphaScale), 255, 255, 255)),
            rotationDegrees: highlight.HotRotation + 6.8f,
            verticalBias: highlight.HotBias + 0.18f,
            Math.Min(0.48f, highlight.HotPeak * 1.42f),
            Math.Min(0.12f, highlight.HotRest * 1.24f),
            highlight.ScaleX * 0.34f,
            highlight.ScaleY * 1.32f);
    }

    private static void AddLensPasses(
        Compositor compositor,
        ContainerVisual root,
        float opacity,
        CurvatureProfile curvature)
    {
        AddLensRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.08f, 0.14f),
            sizeMultiplier: new Vector2(0.84f, 0.62f),
            sampleOffset: new Vector2(-28.0f, 16.0f),
            opacity,
            cornerRadius: 22.0f,
            driftAmount: 2.4f,
            durationSeconds: 8.8,
            curvature);

        AddLensRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.0f, 0.0f),
            sizeMultiplier: new Vector2(1.0f, 0.22f),
            sampleOffset: new Vector2(4.0f, -28.0f),
            opacity * 0.74f,
            cornerRadius: 18.0f,
            driftAmount: 1.6f,
            durationSeconds: 7.2,
            curvature);

        AddLensRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.0f, 0.18f),
            sizeMultiplier: new Vector2(0.26f, 0.68f),
            sampleOffset: new Vector2(-30.0f, 12.0f),
            opacity * 0.52f,
            cornerRadius: 16.0f,
            driftAmount: 1.7f,
            durationSeconds: 7.8,
            curvature);

        AddLensRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.74f, 0.12f),
            sizeMultiplier: new Vector2(0.26f, 0.72f),
            sampleOffset: new Vector2(30.0f, -12.0f),
            opacity * 0.50f,
            cornerRadius: 16.0f,
            driftAmount: 1.5f,
            durationSeconds: 8.2,
            curvature);
    }

    private static void AddLensRegion(
        Compositor compositor,
        ContainerVisual root,
        Vector2 offsetMultiplier,
        Vector2 sizeMultiplier,
        Vector2 sampleOffset,
        float opacity,
        float cornerRadius,
        float driftAmount,
        double durationSeconds,
        CurvatureProfile curvature)
    {
        var region = compositor.CreateContainerVisual();
        BindRelativeSize(compositor, region, root, sizeMultiplier);
        BindRelativeOffset(compositor, region, root, offsetMultiplier);
        BindCenterPoint(compositor, region);
        ApplyRoundedClip(compositor, region, cornerRadius);

        var refractedSample = compositor.CreateSpriteVisual();
        refractedSample.Brush = CreateDisplacedBackdropBrush(compositor, sampleOffset, amountScale: 0.90f, curvature);
        refractedSample.Opacity = opacity;
        refractedSample.Offset = Vector3.Zero;

        BindSize(compositor, refractedSample, region);
        BindCenterPoint(compositor, refractedSample);
        StartOffsetDrift(compositor, refractedSample, Vector2.Zero, driftAmount * 0.20f, durationSeconds);

        region.Children.InsertAtTop(refractedSample);
        root.Children.InsertAtTop(region);
    }

    private static void AddPrismRefractionPasses(
        Compositor compositor,
        ContainerVisual root,
        float opacity,
        CurvatureProfile curvature)
    {
        AddPrismRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.035f, 0.045f),
            sizeMultiplier: new Vector2(0.93f, 0.18f),
            primarySampleOffset: new Vector2(-26.0f, -12.0f),
            secondarySampleOffset: new Vector2(14.0f, 6.0f),
            opacity,
            cornerRadius: 18.0f,
            rotationDegrees: -1.2f,
            curvature);

        AddPrismRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.045f, 0.785f),
            sizeMultiplier: new Vector2(0.91f, 0.15f),
            primarySampleOffset: new Vector2(24.0f, 13.0f),
            secondarySampleOffset: new Vector2(-12.0f, -5.5f),
            opacity * 0.82f,
            cornerRadius: 16.0f,
            rotationDegrees: 1.0f,
            curvature);

        AddPrismRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.045f, 0.12f),
            sizeMultiplier: new Vector2(0.16f, 0.76f),
            primarySampleOffset: new Vector2(-24.0f, 12.0f),
            secondarySampleOffset: new Vector2(9.0f, -7.0f),
            opacity * 0.70f,
            cornerRadius: 16.0f,
            rotationDegrees: 1.6f,
            curvature);

        AddPrismRegion(
            compositor,
            root,
            offsetMultiplier: new Vector2(0.795f, 0.105f),
            sizeMultiplier: new Vector2(0.16f, 0.78f),
            primarySampleOffset: new Vector2(25.0f, -12.0f),
            secondarySampleOffset: new Vector2(-10.0f, 6.0f),
            opacity * 0.66f,
            cornerRadius: 16.0f,
            rotationDegrees: -1.4f,
            curvature);
    }

    private static void AddPrismRegion(
        Compositor compositor,
        ContainerVisual root,
        Vector2 offsetMultiplier,
        Vector2 sizeMultiplier,
        Vector2 primarySampleOffset,
        Vector2 secondarySampleOffset,
        float opacity,
        float cornerRadius,
        float rotationDegrees,
        CurvatureProfile curvature)
    {
        var region = compositor.CreateContainerVisual();
        BindRelativeSize(compositor, region, root, sizeMultiplier);
        BindRelativeOffset(compositor, region, root, offsetMultiplier);
        BindCenterPoint(compositor, region);
        ApplyRoundedClip(compositor, region, cornerRadius);
        region.RotationAngleInDegrees = rotationDegrees;
        region.Properties.InsertScalar(BaseRotationProperty, rotationDegrees);

        AddPrismSample(compositor, region, primarySampleOffset, opacity, curvature);
        AddPrismSample(compositor, region, secondarySampleOffset, opacity * 0.42f, curvature);

        root.Children.InsertAtTop(region);
    }

    private static void AddPrismSample(
        Compositor compositor,
        ContainerVisual region,
        Vector2 sampleOffset,
        float opacity,
        CurvatureProfile curvature)
    {
        var sample = compositor.CreateSpriteVisual();
        sample.Brush = CreateDisplacedBackdropBrush(compositor, sampleOffset, amountScale: 1.12f, curvature);
        sample.Opacity = opacity;
        sample.Offset = Vector3.Zero;

        BindSize(compositor, sample, region);
        BindCenterPoint(compositor, sample);
        region.Children.InsertAtTop(sample);
    }

    private static void AddObliqueRefractionPasses(
        Compositor compositor,
        ContainerVisual root,
        ObliqueRefractionProfile profile,
        CurvatureProfile curvature)
    {
        AddObliqueRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.025f, 0.070f),
            sizeMultiplier: new Vector2(0.95f, profile.PrimaryBandHeight),
            primarySampleOffset: new Vector2(-34.0f, 19.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(18.0f, -10.0f) * profile.SampleScale,
            opacity: profile.Opacity,
            cornerRadius: 18.0f,
            rotationDegrees: profile.PrimaryRotation,
            driftSeconds: 7.1);

        AddObliqueRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.045f, 0.735f),
            sizeMultiplier: new Vector2(0.92f, profile.PrimaryBandHeight * 0.78f),
            primarySampleOffset: new Vector2(32.0f, -17.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(-15.0f, 8.0f) * profile.SampleScale,
            opacity: profile.Opacity * 0.78f,
            cornerRadius: 16.0f,
            rotationDegrees: profile.SecondaryRotation,
            driftSeconds: 7.7);

        AddObliqueRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.130f, 0.425f),
            sizeMultiplier: new Vector2(0.74f, profile.CrossRibbonHeight),
            primarySampleOffset: new Vector2(-28.0f, 25.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(21.0f, -18.0f) * profile.SampleScale,
            opacity: profile.Opacity * 0.62f,
            cornerRadius: 14.0f,
            rotationDegrees: profile.CrossRotation,
            driftSeconds: 8.3);

        AddChromaticFringeRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.055f, 0.060f),
            sizeMultiplier: new Vector2(0.90f, profile.FringeHeight),
            rotationDegrees: profile.PrimaryRotation - 0.8f,
            opacityScale: 1.0f);

        AddChromaticFringeRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.080f, 0.790f),
            sizeMultiplier: new Vector2(0.86f, profile.FringeHeight * 0.82f),
            rotationDegrees: profile.SecondaryRotation + 0.6f,
            opacityScale: 0.72f);
    }

    private static void AddCornerLensingPasses(
        Compositor compositor,
        ContainerVisual root,
        CornerLensProfile profile,
        CurvatureProfile curvature)
    {
        AddCornerLensRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.0f, 0.0f),
            primarySampleOffset: new Vector2(-36.0f, -26.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(18.0f, 14.0f) * profile.SampleScale,
            opacityScale: 1.0f,
            driftAnchor: new Vector2(-1.2f, -0.7f),
            driftSeconds: 7.4);

        AddCornerLensRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(1.0f - profile.SizeScale, 0.0f),
            primarySampleOffset: new Vector2(38.0f, -25.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(-17.0f, 13.0f) * profile.SampleScale,
            opacityScale: 0.92f,
            driftAnchor: new Vector2(1.1f, -0.8f),
            driftSeconds: 7.9);

        AddCornerLensRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(0.0f, 1.0f - profile.SizeScale),
            primarySampleOffset: new Vector2(-35.0f, 28.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(17.0f, -13.0f) * profile.SampleScale,
            opacityScale: 0.86f,
            driftAnchor: new Vector2(-1.0f, 0.9f),
            driftSeconds: 8.1);

        AddCornerLensRegion(
            compositor,
            root,
            profile,
            curvature,
            offsetMultiplier: new Vector2(1.0f - profile.SizeScale, 1.0f - profile.SizeScale),
            primarySampleOffset: new Vector2(37.0f, 27.0f) * profile.SampleScale,
            secondarySampleOffset: new Vector2(-18.0f, -12.0f) * profile.SampleScale,
            opacityScale: 0.80f,
            driftAnchor: new Vector2(1.0f, 0.8f),
            driftSeconds: 8.5);
    }

    private static void AddCornerLensRegion(
        Compositor compositor,
        ContainerVisual root,
        CornerLensProfile profile,
        CurvatureProfile curvature,
        Vector2 offsetMultiplier,
        Vector2 primarySampleOffset,
        Vector2 secondarySampleOffset,
        float opacityScale,
        Vector2 driftAnchor,
        double driftSeconds)
    {
        var region = compositor.CreateContainerVisual();
        BindRelativeSize(compositor, region, root, new Vector2(profile.SizeScale));
        BindRelativeOffset(compositor, region, root, offsetMultiplier);
        BindCenterPoint(compositor, region);
        ApplyRoundedClip(compositor, region, profile.Radius);

        AddCornerLensSample(
            compositor,
            region,
            primarySampleOffset,
            profile.Opacity * opacityScale,
            profile.AmountScale,
            driftAnchor,
            profile.DriftAmount,
            driftSeconds,
            curvature);

        AddCornerLensSample(
            compositor,
            region,
            secondarySampleOffset,
            profile.Opacity * opacityScale * 0.34f,
            profile.AmountScale * 0.72f,
            -driftAnchor * 0.65f,
            profile.DriftAmount * 0.48f,
            driftSeconds + 0.9,
            curvature);

        root.Children.InsertAtTop(region);
    }

    private static void AddCornerLensSample(
        Compositor compositor,
        ContainerVisual region,
        Vector2 sampleOffset,
        float opacity,
        float amountScale,
        Vector2 driftAnchor,
        float driftAmount,
        double driftSeconds,
        CurvatureProfile curvature)
    {
        var sample = compositor.CreateSpriteVisual();
        sample.Brush = CreateDisplacedBackdropBrush(compositor, sampleOffset, amountScale, curvature);
        sample.Opacity = opacity;
        sample.Offset = new Vector3(driftAnchor.X, driftAnchor.Y, 0);

        BindSize(compositor, sample, region);
        BindCenterPoint(compositor, sample);
        StartOffsetDrift(compositor, sample, driftAnchor, driftAmount, driftSeconds);
        StartOpacityBreath(compositor, sample, opacity, driftSeconds + 0.7);
        region.Children.InsertAtTop(sample);
    }

    private static void AddObliqueRegion(
        Compositor compositor,
        ContainerVisual root,
        ObliqueRefractionProfile profile,
        CurvatureProfile curvature,
        Vector2 offsetMultiplier,
        Vector2 sizeMultiplier,
        Vector2 primarySampleOffset,
        Vector2 secondarySampleOffset,
        float opacity,
        float cornerRadius,
        float rotationDegrees,
        double driftSeconds)
    {
        var region = compositor.CreateContainerVisual();
        BindRelativeSize(compositor, region, root, sizeMultiplier);
        BindRelativeOffset(compositor, region, root, offsetMultiplier);
        BindCenterPoint(compositor, region);
        ApplyRoundedClip(compositor, region, cornerRadius);
        region.RotationAngleInDegrees = rotationDegrees;
        region.Properties.InsertScalar(BaseRotationProperty, rotationDegrees);

        AddObliqueSample(
            compositor,
            region,
            primarySampleOffset,
            opacity,
            amountScale: profile.AmountScale,
            driftAnchor: new Vector2(-1.4f, 0.9f),
            driftAmount: profile.DriftAmount,
            driftSeconds: driftSeconds,
            curvature: curvature);

        AddObliqueSample(
            compositor,
            region,
            secondarySampleOffset,
            opacity * 0.36f,
            amountScale: profile.AmountScale * 0.72f,
            driftAnchor: new Vector2(1.0f, -0.7f),
            driftAmount: profile.DriftAmount * 0.58f,
            driftSeconds: driftSeconds + 1.2,
            curvature: curvature);

        root.Children.InsertAtTop(region);
    }

    private static void AddObliqueSample(
        Compositor compositor,
        ContainerVisual region,
        Vector2 sampleOffset,
        float opacity,
        float amountScale,
        Vector2 driftAnchor,
        float driftAmount,
        double driftSeconds,
        CurvatureProfile curvature)
    {
        var sample = compositor.CreateSpriteVisual();
        sample.Brush = CreateDisplacedBackdropBrush(compositor, sampleOffset, amountScale, curvature);
        sample.Opacity = opacity;
        sample.Offset = new Vector3(driftAnchor.X, driftAnchor.Y, 0);

        BindSize(compositor, sample, region);
        BindCenterPoint(compositor, sample);
        StartOffsetDrift(compositor, sample, driftAnchor, driftAmount, driftSeconds);
        StartOpacityBreath(compositor, sample, opacity, driftSeconds + 0.9);
        region.Children.InsertAtTop(sample);
    }

    private static void AddChromaticFringeRegion(
        Compositor compositor,
        ContainerVisual root,
        ObliqueRefractionProfile profile,
        CurvatureProfile curvature,
        Vector2 offsetMultiplier,
        Vector2 sizeMultiplier,
        float rotationDegrees,
        float opacityScale)
    {
        var region = compositor.CreateContainerVisual();
        BindRelativeSize(compositor, region, root, sizeMultiplier);
        BindRelativeOffset(compositor, region, root, offsetMultiplier);
        BindCenterPoint(compositor, region);
        ApplyRoundedClip(compositor, region, 12.0f);
        region.RotationAngleInDegrees = rotationDegrees;
        region.Opacity = opacityScale;
        region.Properties.InsertScalar(BaseRotationProperty, rotationDegrees);

        AddChromaticFringeSample(
            compositor,
            region,
            CreateChromaticDisplacedBackdropBrush(
                compositor,
                new Vector2(-24.0f, 13.0f) * profile.SampleScale,
                profile.AmountScale * 1.05f,
                ChromaticChannel.Cyan,
                curvature),
            new Vector3(-1.8f, -1.0f, 0),
            profile.FringeOpacity * 0.92f);

        AddChromaticFringeSample(
            compositor,
            region,
            CreateChromaticDisplacedBackdropBrush(
                compositor,
                new Vector2(26.0f, -14.0f) * profile.SampleScale,
                profile.AmountScale * 1.08f,
                ChromaticChannel.Warm,
                curvature),
            new Vector3(1.9f, 0.9f, 0),
            profile.FringeOpacity * 0.84f);

        root.Children.InsertAtTop(region);
    }

    private static void AddChromaticFringeSample(
        Compositor compositor,
        ContainerVisual region,
        CompositionBrush brush,
        Vector3 offset,
        float opacity)
    {
        var sample = compositor.CreateSpriteVisual();
        sample.Brush = brush;
        sample.Opacity = opacity;
        sample.Offset = offset;

        BindSize(compositor, sample, region);
        region.Children.InsertAtTop(sample);
    }

    private static CompositionBrush CreateChromaticDisplacedBackdropBrush(
        Compositor compositor,
        Vector2 sampleOffset,
        float amountScale,
        ChromaticChannel channel,
        CurvatureProfile curvature)
    {
        try
        {
            var displacement = new DisplacementMapEffect
            {
                Name = LiquidDisplacementEffectName,
                Source = new CompositionEffectSourceParameter(BackdropSourceName),
                Displacement = new CompositionEffectSourceParameter(DisplacementMapSourceName),
                Amount = Math.Clamp(sampleOffset.Length() * amountScale, DisplacementAmountFloor, ChromaticDisplacementAmountCeiling),
                XChannelSelect = EffectChannelSelect.Red,
                YChannelSelect = EffectChannelSelect.Green
            };

            var chromaticSplit = new ColorMatrixEffect
            {
                Source = displacement,
                ColorMatrix = CreateChromaticMatrix(channel)
            };

            var factory = compositor.CreateEffectFactory(chromaticSplit, [LiquidDisplacementAmountProperty]);
            var brush = factory.CreateBrush();
            brush.Properties.InsertScalar(LiquidDisplacementAmountProperty, displacement.Amount);
            brush.SetSourceParameter(BackdropSourceName, CreateRefractedBackdropBrush(compositor, sampleOffset * ChromaticBackdropSamplePull));
            brush.SetSourceParameter(DisplacementMapSourceName, CreateCurvedDisplacementMap(compositor, sampleOffset, curvature));
            return brush;
        }
        catch
        {
            return CreateDisplacedBackdropBrush(compositor, sampleOffset, amountScale, curvature);
        }
    }

    private static Matrix5x4 CreateChromaticMatrix(ChromaticChannel channel)
    {
        return channel == ChromaticChannel.Cyan
            ? new Matrix5x4
            {
                M11 = 0.12f,
                M22 = 0.88f,
                M33 = 1.08f,
                M44 = 1.0f
            }
            : new Matrix5x4
            {
                M11 = 1.10f,
                M22 = 0.36f,
                M33 = 0.54f,
                M44 = 1.0f
            };
    }

    private static byte ScaleAlpha(byte alpha, float scale)
    {
        return (byte)Math.Clamp((int)Math.Round(alpha * scale), 0, 255);
    }

    private static void AddGlintLayer(
        Compositor compositor,
        ContainerVisual root,
        CompositionBrush brush,
        float rotationDegrees,
        float verticalBias,
        float opacityPeak,
        float opacityRest,
        float glintScaleX,
        float glintScaleY)
    {
        var glint = compositor.CreateSpriteVisual();
        glint.Brush = brush;
        glint.Opacity = 0.0f;
        glint.RotationAngleInDegrees = rotationDegrees;
        glint.Properties.InsertScalar(GlintPeakProperty, opacityPeak);
        glint.Properties.InsertScalar(GlintRestProperty, opacityRest);
        glint.Properties.InsertScalar(GlintScaleXProperty, glintScaleX);
        glint.Properties.InsertScalar(GlintScaleYProperty, glintScaleY);
        glint.Properties.InsertScalar(BaseRotationProperty, rotationDegrees);

        BindGlintSize(compositor, glint, root, glintScaleX, glintScaleY);
        BindCenterPoint(compositor, glint);
        CenterGlint(compositor, glint, root, verticalBias);
        root.Children.InsertAtTop(glint);
    }

    private static void AddAmbientReflectionPasses(
        Compositor compositor,
        ContainerVisual root,
        float alphaScale,
        HighlightProfile highlight)
    {
        var whiteOpacity = Math.Clamp(0.064f * alphaScale * highlight.WhiteAlphaScale, 0.020f, 0.136f);
        var colorOpacity = Math.Clamp(0.042f * alphaScale * highlight.ColorAlphaScale, 0.014f, 0.090f);

        AddAmbientReflectionLayer(
            compositor,
            root,
            CreateAmbientReflectionBrush(compositor, Color.FromArgb(104, 255, 255, 255), Color.FromArgb(42, 108, 218, 255)),
            rotationDegrees: highlight.HotRotation - 4.8f,
            verticalBias: -0.05f,
            opacity: whiteOpacity,
            widthScale: 1.26f,
            heightScale: 0.72f,
            driftAnchor: new Vector2(-2.8f, 1.2f),
            driftAmount: 1.25f,
            breathSeconds: 8.4);

        AddAmbientReflectionLayer(
            compositor,
            root,
            CreateAmbientReflectionBrush(compositor, Color.FromArgb(78, 124, 224, 255), Color.FromArgb(48, 255, 112, 216)),
            rotationDegrees: highlight.PrimaryRotation + 10.6f,
            verticalBias: 0.44f,
            opacity: colorOpacity,
            widthScale: 1.18f,
            heightScale: 0.54f,
            driftAnchor: new Vector2(2.1f, -0.8f),
            driftAmount: 0.95f,
            breathSeconds: 9.6);
    }

    private static void AddAmbientReflectionLayer(
        Compositor compositor,
        ContainerVisual root,
        CompositionBrush brush,
        float rotationDegrees,
        float verticalBias,
        float opacity,
        float widthScale,
        float heightScale,
        Vector2 driftAnchor,
        float driftAmount,
        double breathSeconds)
    {
        var reflection = compositor.CreateSpriteVisual();
        reflection.Brush = brush;
        reflection.Opacity = opacity;
        reflection.RotationAngleInDegrees = rotationDegrees;
        reflection.Properties.InsertScalar(PassiveRimLayerProperty, 1.0f);

        BindGlintSize(compositor, reflection, root, widthScale, heightScale);
        BindCenterPoint(compositor, reflection);
        CenterGlint(compositor, reflection, root, verticalBias);
        StartOffsetDrift(compositor, reflection, driftAnchor, driftAmount, breathSeconds + 1.1);
        StartOpacityBreath(compositor, reflection, opacity, breathSeconds);
        root.Children.InsertAtTop(reflection);
    }

    private static CompositionBrush CreateAmbientReflectionBrush(
        Compositor compositor,
        Color primaryColor,
        Color secondaryColor)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.0f, 0.5f);
        brush.EndPoint = new Vector2(1.0f, 0.5f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(0, primaryColor.R, primaryColor.G, primaryColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.22f, Color.FromArgb((byte)Math.Clamp(primaryColor.A * 0.18f, 0, 255), primaryColor.R, primaryColor.G, primaryColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.47f, primaryColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.60f, Color.FromArgb((byte)Math.Clamp(secondaryColor.A * 0.62f, 0, 255), secondaryColor.R, secondaryColor.G, secondaryColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.82f, Color.FromArgb((byte)Math.Clamp(primaryColor.A * 0.16f, 0, 255), 255, 255, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, 255, 255, 255)));
        return brush;
    }

    private static void AddMicroCausticPasses(
        Compositor compositor,
        ContainerVisual root,
        float alphaScale,
        HighlightProfile highlight)
    {
        var whiteOpacity = Math.Clamp(0.082f * alphaScale * highlight.WhiteAlphaScale, 0.024f, 0.166f);
        var cyanOpacity = Math.Clamp(0.050f * alphaScale * highlight.ColorAlphaScale, 0.016f, 0.102f);

        AddMicroCausticLayer(
            compositor,
            root,
            CreateMicroCausticBrush(compositor, Color.FromArgb(116, 255, 255, 255), Color.FromArgb(56, 106, 224, 255)),
            rotationDegrees: highlight.HotRotation - 1.8f,
            verticalBias: 0.18f,
            opacity: whiteOpacity,
            widthScale: 0.72f,
            heightScale: 0.058f,
            driftAnchor: new Vector2(-1.2f, 0.35f),
            driftAmount: 0.78f,
            breathSeconds: 5.9);

        AddMicroCausticLayer(
            compositor,
            root,
            CreateMicroCausticBrush(compositor, Color.FromArgb(82, 118, 226, 255), Color.FromArgb(54, 255, 104, 210)),
            rotationDegrees: highlight.SecondaryRotation + 3.4f,
            verticalBias: 0.58f,
            opacity: cyanOpacity,
            widthScale: 0.64f,
            heightScale: 0.046f,
            driftAnchor: new Vector2(1.3f, -0.25f),
            driftAmount: 0.66f,
            breathSeconds: 6.7);

        AddMicroCausticLayer(
            compositor,
            root,
            CreateMicroCausticBrush(compositor, Color.FromArgb(74, 255, 255, 255), Color.FromArgb(40, 255, 132, 224)),
            rotationDegrees: highlight.PrimaryRotation - 6.2f,
            verticalBias: 0.74f,
            opacity: whiteOpacity * 0.62f,
            widthScale: 0.52f,
            heightScale: 0.038f,
            driftAnchor: new Vector2(-0.8f, 0.25f),
            driftAmount: 0.52f,
            breathSeconds: 7.2);
    }

    private static void AddMicroCausticLayer(
        Compositor compositor,
        ContainerVisual root,
        CompositionBrush brush,
        float rotationDegrees,
        float verticalBias,
        float opacity,
        float widthScale,
        float heightScale,
        Vector2 driftAnchor,
        float driftAmount,
        double breathSeconds)
    {
        var caustic = compositor.CreateSpriteVisual();
        caustic.Brush = brush;
        caustic.Opacity = opacity;
        caustic.RotationAngleInDegrees = rotationDegrees;
        caustic.Properties.InsertScalar(PassiveRimLayerProperty, 1.0f);

        BindGlintSize(compositor, caustic, root, widthScale, heightScale);
        BindCenterPoint(compositor, caustic);
        CenterGlint(compositor, caustic, root, verticalBias);
        StartOffsetDrift(compositor, caustic, driftAnchor, driftAmount, breathSeconds + 0.8);
        StartOpacityBreath(compositor, caustic, opacity, breathSeconds);
        root.Children.InsertAtTop(caustic);
    }

    private static CompositionBrush CreateMicroCausticBrush(
        Compositor compositor,
        Color hotColor,
        Color tintColor)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.0f, 0.5f);
        brush.EndPoint = new Vector2(1.0f, 0.5f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(0, hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.24f, Color.FromArgb((byte)Math.Clamp(tintColor.A * 0.20f, 0, 255), tintColor.R, tintColor.G, tintColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.44f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.78f, 0, 255), hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.50f, hotColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.58f, Color.FromArgb((byte)Math.Clamp(tintColor.A * 0.58f, 0, 255), tintColor.R, tintColor.G, tintColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.76f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.14f, 0, 255), 255, 255, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, 255, 255, 255)));
        return brush;
    }

    private static CompositionBrush CreateGlintBrush(Compositor compositor, Color hotColor)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.0f, 0.5f);
        brush.EndPoint = new Vector2(1.0f, 0.5f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(0, hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.36f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.45f, 0, 255), hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.55f, hotColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.76f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.36f, 0, 255), 255, 255, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, 255, 255, 255)));
        return brush;
    }

    private static CompositionBrush CreateFocusGlintBrush(Compositor compositor, Color hotColor)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.0f, 0.5f);
        brush.EndPoint = new Vector2(1.0f, 0.5f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(0, hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.40f, Color.FromArgb(0, hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.49f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.56f, 0, 255), 255, 255, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.54f, hotColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.61f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.32f, 0, 255), 96, 220, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, 255, 255, 255)));
        return brush;
    }

    private static void AddDepthRimPasses(
        Compositor compositor,
        ContainerVisual root,
        RimDepthProfile profile)
    {
        var shadowColor = Color.FromArgb(168, 20, 26, 38);
        var shadowFade = Color.FromArgb(48, 32, 44, 66);
        var causticWhite = Color.FromArgb(204, 255, 255, 255);
        var causticCyan = Color.FromArgb(112, 112, 226, 255);

        AddRimBand(compositor, root, RimEdge.Top, profile.ShadowThickness, profile.ShadowOpacity, CreateRimDepthBrush(compositor, RimEdge.Top, shadowColor, shadowFade), 8.6);
        AddRimBand(compositor, root, RimEdge.Bottom, profile.ShadowThickness * 0.92f, profile.ShadowOpacity * 0.78f, CreateRimDepthBrush(compositor, RimEdge.Bottom, shadowColor, shadowFade), 9.2);
        AddRimBand(compositor, root, RimEdge.Left, profile.ShadowThickness * 0.86f, profile.ShadowOpacity * 0.70f, CreateRimDepthBrush(compositor, RimEdge.Left, shadowColor, shadowFade), 9.8);
        AddRimBand(compositor, root, RimEdge.Right, profile.ShadowThickness * 0.82f, profile.ShadowOpacity * 0.66f, CreateRimDepthBrush(compositor, RimEdge.Right, shadowColor, shadowFade), 8.9);

        AddRimBand(compositor, root, RimEdge.Top, profile.CausticThickness, profile.CausticOpacity, CreateRimDepthBrush(compositor, RimEdge.Top, causticWhite, causticCyan), 6.8);
        AddRimBand(compositor, root, RimEdge.Right, profile.CausticThickness * 0.86f, profile.CausticOpacity * 0.68f, CreateRimDepthBrush(compositor, RimEdge.Right, causticWhite, causticCyan), 7.4);
    }

    private static void AddMeniscusSheenPasses(
        Compositor compositor,
        ContainerVisual root,
        float alphaScale,
        HighlightProfile highlight)
    {
        var edgeOpacity = Math.Clamp(0.094f * alphaScale * highlight.WhiteAlphaScale, 0.024f, 0.172f);
        var colorOpacity = Math.Clamp(0.058f * alphaScale * highlight.ColorAlphaScale, 0.016f, 0.116f);

        AddRimBand(
            compositor,
            root,
            RimEdge.Top,
            thickness: 4.6f,
            opacity: edgeOpacity,
            CreateMeniscusEdgeBrush(compositor, RimEdge.Top, Color.FromArgb(226, 255, 255, 255), Color.FromArgb(94, 118, 226, 255)),
            breathSeconds: 6.4);

        AddRimBand(
            compositor,
            root,
            RimEdge.Left,
            thickness: 3.8f,
            opacity: colorOpacity,
            CreateMeniscusEdgeBrush(compositor, RimEdge.Left, Color.FromArgb(170, 112, 226, 255), Color.FromArgb(76, 255, 104, 210)),
            breathSeconds: 7.2);

        AddRimBand(
            compositor,
            root,
            RimEdge.Bottom,
            thickness: 3.4f,
            opacity: edgeOpacity * 0.56f,
            CreateMeniscusEdgeBrush(compositor, RimEdge.Bottom, Color.FromArgb(150, 255, 255, 255), Color.FromArgb(72, 118, 226, 255)),
            breathSeconds: 7.8);

        AddSurfaceSheenLayer(
            compositor,
            root,
            CreateSurfaceSheenBrush(compositor, Color.FromArgb(118, 255, 255, 255), Color.FromArgb(46, 112, 226, 255)),
            rotationDegrees: highlight.HotRotation - 3.8f,
            verticalBias: 0.045f,
            opacity: edgeOpacity * 0.82f,
            widthScale: 0.92f,
            heightScale: 0.060f,
            breathSeconds: 6.0);

        AddSurfaceSheenLayer(
            compositor,
            root,
            CreateSurfaceSheenBrush(compositor, Color.FromArgb(86, 255, 255, 255), Color.FromArgb(48, 255, 108, 218)),
            rotationDegrees: highlight.PrimaryRotation + 2.4f,
            verticalBias: 0.82f,
            opacity: colorOpacity * 0.74f,
            widthScale: 0.78f,
            heightScale: 0.044f,
            breathSeconds: 7.0);
    }

    private static void AddSurfaceSheenLayer(
        Compositor compositor,
        ContainerVisual root,
        CompositionBrush brush,
        float rotationDegrees,
        float verticalBias,
        float opacity,
        float widthScale,
        float heightScale,
        double breathSeconds)
    {
        var sheen = compositor.CreateSpriteVisual();
        sheen.Brush = brush;
        sheen.Opacity = opacity;
        sheen.RotationAngleInDegrees = rotationDegrees;
        sheen.Properties.InsertScalar(PassiveRimLayerProperty, 1.0f);

        BindGlintSize(compositor, sheen, root, widthScale, heightScale);
        BindCenterPoint(compositor, sheen);
        CenterGlint(compositor, sheen, root, verticalBias);
        StartOpacityBreath(compositor, sheen, opacity, breathSeconds);
        root.Children.InsertAtTop(sheen);
    }

    private static CompositionBrush CreateMeniscusEdgeBrush(
        Compositor compositor,
        RimEdge edge,
        Color hotColor,
        Color tintColor)
    {
        var brush = compositor.CreateLinearGradientBrush();
        switch (edge)
        {
            case RimEdge.Bottom:
                brush.StartPoint = new Vector2(0.5f, 1.0f);
                brush.EndPoint = new Vector2(0.5f, 0.0f);
                break;
            case RimEdge.Left:
                brush.StartPoint = new Vector2(0.0f, 0.5f);
                brush.EndPoint = new Vector2(1.0f, 0.5f);
                break;
            case RimEdge.Right:
                brush.StartPoint = new Vector2(1.0f, 0.5f);
                brush.EndPoint = new Vector2(0.0f, 0.5f);
                break;
            default:
                brush.StartPoint = new Vector2(0.5f, 0.0f);
                brush.EndPoint = new Vector2(0.5f, 1.0f);
                break;
        }

        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, hotColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.24f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.46f, 0, 255), hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.58f, Color.FromArgb((byte)Math.Clamp(tintColor.A * 0.58f, 0, 255), tintColor.R, tintColor.G, tintColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, hotColor.R, hotColor.G, hotColor.B)));
        return brush;
    }

    private static CompositionBrush CreateSurfaceSheenBrush(
        Compositor compositor,
        Color hotColor,
        Color tintColor)
    {
        var brush = compositor.CreateLinearGradientBrush();
        brush.StartPoint = new Vector2(0.0f, 0.5f);
        brush.EndPoint = new Vector2(1.0f, 0.5f);
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(0, hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.28f, Color.FromArgb((byte)Math.Clamp(tintColor.A * 0.36f, 0, 255), tintColor.R, tintColor.G, tintColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.48f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.72f, 0, 255), hotColor.R, hotColor.G, hotColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.52f, hotColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.60f, Color.FromArgb((byte)Math.Clamp(tintColor.A * 0.52f, 0, 255), tintColor.R, tintColor.G, tintColor.B)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.82f, Color.FromArgb((byte)Math.Clamp(hotColor.A * 0.18f, 0, 255), 255, 255, 255)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, 255, 255, 255)));
        return brush;
    }

    private static void AddRimBand(
        Compositor compositor,
        ContainerVisual root,
        RimEdge edge,
        float thickness,
        float opacity,
        CompositionBrush brush,
        double breathSeconds)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = brush;
        sprite.Opacity = opacity;
        sprite.Properties.InsertScalar(PassiveRimLayerProperty, 1.0f);

        if (edge is RimEdge.Top or RimEdge.Bottom)
        {
            sprite.Size = new Vector2(0.0f, thickness);
            BindWidth(compositor, sprite, root, thickness);

            if (edge == RimEdge.Bottom)
            {
                BindBottomOffset(compositor, sprite, root, thickness, Vector2.Zero);
            }
        }
        else
        {
            sprite.Size = new Vector2(thickness, 0.0f);
            BindHeight(compositor, sprite, root, thickness);

            if (edge == RimEdge.Right)
            {
                BindRightOffset(compositor, sprite, root, thickness, Vector2.Zero);
            }
        }

        StartOpacityBreath(compositor, sprite, opacity, breathSeconds);
        root.Children.InsertAtTop(sprite);
    }

    private static CompositionBrush CreateRimDepthBrush(
        Compositor compositor,
        RimEdge edge,
        Color edgeColor,
        Color midColor)
    {
        var brush = compositor.CreateLinearGradientBrush();

        switch (edge)
        {
            case RimEdge.Bottom:
                brush.StartPoint = new Vector2(0.5f, 1.0f);
                brush.EndPoint = new Vector2(0.5f, 0.0f);
                break;
            case RimEdge.Left:
                brush.StartPoint = new Vector2(0.0f, 0.5f);
                brush.EndPoint = new Vector2(1.0f, 0.5f);
                break;
            case RimEdge.Right:
                brush.StartPoint = new Vector2(1.0f, 0.5f);
                brush.EndPoint = new Vector2(0.0f, 0.5f);
                break;
            default:
                brush.StartPoint = new Vector2(0.5f, 0.0f);
                brush.EndPoint = new Vector2(0.5f, 1.0f);
                break;
        }

        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, edgeColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.42f, midColor));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(0, edgeColor.R, edgeColor.G, edgeColor.B)));
        return brush;
    }

    private static void AddEdgePasses(
        Compositor compositor,
        ContainerVisual root,
        float opacity,
        CurvatureProfile curvature)
    {
        AddHorizontalEdgePass(compositor, root, top: true, sampleOffset: new Vector2(-10.0f, -4.0f), opacity: opacity, height: 14.0f, curvature: curvature);
        AddHorizontalEdgePass(compositor, root, top: false, sampleOffset: new Vector2(9.0f, 4.8f), opacity: opacity * 0.84f, height: 12.0f, curvature: curvature);
        AddVerticalEdgePass(compositor, root, left: true, sampleOffset: new Vector2(-7.2f, 5.4f), opacity: opacity * 0.72f, width: 12.0f, curvature: curvature);
        AddVerticalEdgePass(compositor, root, left: false, sampleOffset: new Vector2(7.8f, -5.0f), opacity: opacity * 0.68f, width: 10.0f, curvature: curvature);
    }

    private static void AddHorizontalEdgePass(
        Compositor compositor,
        ContainerVisual root,
        bool top,
        Vector2 sampleOffset,
        float opacity,
        float height,
        CurvatureProfile curvature)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = CreateDisplacedBackdropBrush(compositor, sampleOffset * 1.72f, amountScale: 0.96f, curvature);
        sprite.Opacity = opacity;
        sprite.Offset = new Vector3(sampleOffset.X, sampleOffset.Y, 0);
        sprite.Size = new Vector2(0, height);

        BindWidth(compositor, sprite, root, height);
        BindCenterPoint(compositor, sprite);
        if (top)
        {
            StartOffsetDrift(compositor, sprite, sampleOffset, 1.5f, 6.4);
        }
        else
        {
            BindBottomOffset(compositor, sprite, root, height, sampleOffset);
        }

        StartOpacityBreath(compositor, sprite, opacity, top ? 7.2 : 7.8);
        root.Children.InsertAtTop(sprite);
    }

    private static void AddVerticalEdgePass(
        Compositor compositor,
        ContainerVisual root,
        bool left,
        Vector2 sampleOffset,
        float opacity,
        float width,
        CurvatureProfile curvature)
    {
        var sprite = compositor.CreateSpriteVisual();
        sprite.Brush = CreateDisplacedBackdropBrush(compositor, sampleOffset * 1.72f, amountScale: 0.96f, curvature);
        sprite.Opacity = opacity;
        sprite.Offset = new Vector3(sampleOffset.X, sampleOffset.Y, 0);
        sprite.Size = new Vector2(width, 0);

        BindHeight(compositor, sprite, root, width);
        BindCenterPoint(compositor, sprite);
        if (left)
        {
            StartOffsetDrift(compositor, sprite, sampleOffset, 1.25f, 7.6);
        }
        else
        {
            BindRightOffset(compositor, sprite, root, width, sampleOffset);
        }

        StartOpacityBreath(compositor, sprite, opacity, left ? 7.6 : 6.9);
        root.Children.InsertAtTop(sprite);
    }

    private static CompositionBrush CreateBackdropSourceBrush(Compositor compositor)
    {
        return compositor.CreateBackdropBrush();
    }

    private static CompositionBrush CreateRefractedBackdropBrush(Compositor compositor, Vector2 sampleOffset)
    {
        if (sampleOffset.LengthSquared() < 0.01f)
        {
            return CreateBackdropSourceBrush(compositor);
        }

        try
        {
            var transformMatrix = Matrix3x2.CreateTranslation(sampleOffset);
            var transform = new Transform2DEffect
            {
                Name = LiquidBackdropTransformEffectName,
                Source = new CompositionEffectSourceParameter(BackdropSourceName),
                TransformMatrix = transformMatrix
            };

            var factory = compositor.CreateEffectFactory(transform, [LiquidBackdropTransformMatrixProperty]);
            var brush = factory.CreateBrush();
            brush.Properties.InsertMatrix3x2(LiquidBackdropTransformMatrixProperty, transformMatrix);
            brush.Properties.InsertScalar(LiquidSampleBaseXProperty, sampleOffset.X);
            brush.Properties.InsertScalar(LiquidSampleBaseYProperty, sampleOffset.Y);
            brush.SetSourceParameter(BackdropSourceName, CreateBackdropSourceBrush(compositor));
            return brush;
        }
        catch
        {
            return CreateBackdropSourceBrush(compositor);
        }
    }

    private static CompositionBrush CreateBlurredBackdropBrush(
        Compositor compositor,
        float blurAmount,
        float saturation)
    {
        try
        {
            var blur = new GaussianBlurEffect
            {
                Name = LiquidMaterialBlurEffectName,
                Source = new CompositionEffectSourceParameter(BackdropSourceName),
                BlurAmount = blurAmount,
                BorderMode = EffectBorderMode.Hard
            };

            var saturatedBlur = new SaturationEffect
            {
                Source = blur,
                Saturation = saturation
            };

            var factory = compositor.CreateEffectFactory(saturatedBlur);
            var brush = factory.CreateBrush();
            brush.SetSourceParameter(BackdropSourceName, CreateBackdropSourceBrush(compositor));
            return brush;
        }
        catch
        {
            return CreateBackdropSourceBrush(compositor);
        }
    }

    private static CompositionBrush CreateDisplacedBackdropBrush(
        Compositor compositor,
        Vector2 sampleOffset,
        float amountScale,
        CurvatureProfile curvature)
    {
        if (sampleOffset.LengthSquared() < 0.01f)
        {
            return CreateBackdropSourceBrush(compositor);
        }

        try
        {
            var displacement = new DisplacementMapEffect
            {
                Name = LiquidDisplacementEffectName,
                Source = new CompositionEffectSourceParameter(BackdropSourceName),
                Displacement = new CompositionEffectSourceParameter(DisplacementMapSourceName),
                Amount = Math.Clamp(sampleOffset.Length() * amountScale, DisplacementAmountFloor, DisplacementAmountCeiling),
                XChannelSelect = EffectChannelSelect.Red,
                YChannelSelect = EffectChannelSelect.Green
            };

            var factory = compositor.CreateEffectFactory(displacement, [LiquidDisplacementAmountProperty]);
            var brush = factory.CreateBrush();
            brush.Properties.InsertScalar(LiquidDisplacementAmountProperty, displacement.Amount);
            brush.SetSourceParameter(BackdropSourceName, CreateRefractedBackdropBrush(compositor, sampleOffset * BackdropSamplePull));
            brush.SetSourceParameter(DisplacementMapSourceName, CreateCurvedDisplacementMap(compositor, sampleOffset, curvature));
            return brush;
        }
        catch
        {
            return CreateRefractedBackdropBrush(compositor, sampleOffset);
        }
    }

    private static CompositionBrush CreateCurvedDisplacementMap(
        Compositor compositor,
        Vector2 sampleOffset,
        CurvatureProfile curvature)
    {
        try
        {
            var cache = CurvedDisplacementMaps.GetValue(compositor, _ => new DisplacementMapCache());
            var cacheKey = CreateDisplacementMapKey(curvature, sampleOffset);
            if (cache.Brushes.TryGetValue(cacheKey, out var cachedBrush))
            {
                return cachedBrush;
            }

            var pixels = CreateCurvedDisplacementPixels(curvature, sampleOffset);
            var canvasDevice = CanvasDevice.GetSharedDevice();
            var bitmap = CanvasBitmap.CreateFromBytes(
                canvasDevice,
                pixels,
                DisplacementMapSize,
                DisplacementMapSize,
                WinDirectXPixelFormat.B8G8R8A8UIntNormalized);

            var graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(compositor, canvasDevice);
            var surface = graphicsDevice.CreateDrawingSurface(
                new Size(DisplacementMapSize, DisplacementMapSize),
                CanvasDirectXPixelFormat.B8G8R8A8UIntNormalized,
                CanvasDirectXAlphaMode.Premultiplied);

            using (var drawingSession = CanvasComposition.CreateDrawingSession(surface))
            {
                drawingSession.Clear(Color.FromArgb(255, NeutralDisplacementChannel, NeutralDisplacementChannel, NeutralDisplacementChannel));
                drawingSession.DrawImage(bitmap);
            }

            var surfaceBrush = compositor.CreateSurfaceBrush(surface);
            surfaceBrush.Stretch = CompositionStretch.Fill;
            surfaceBrush.HorizontalAlignmentRatio = 0.5f;
            surfaceBrush.VerticalAlignmentRatio = 0.5f;

            cache.Brushes[cacheKey] = surfaceBrush;
            return surfaceBrush;
        }
        catch
        {
            return CreateDirectionalDisplacementMap(compositor, sampleOffset);
        }
    }

    private static string CreateDisplacementMapKey(CurvatureProfile curvature, Vector2 sampleOffset)
    {
        return $"{curvature.Key}:{QuantizeDirection(sampleOffset.X)}:{QuantizeDirection(sampleOffset.Y)}";
    }

    private static int QuantizeDirection(float value)
    {
        return (int)Math.Clamp(MathF.Round(value / 4.0f), -12.0f, 12.0f);
    }

    private static byte[] CreateCurvedDisplacementPixels(CurvatureProfile curvature, Vector2 sampleOffset)
    {
        var pixels = new byte[DisplacementMapSize * DisplacementMapSize * 4];
        var center = (DisplacementMapSize - 1) * 0.5f;
        var sampleLength = MathF.Max(1.0f, sampleOffset.Length());
        var directionX = sampleOffset.X / sampleLength;
        var directionY = sampleOffset.Y / sampleLength;
        var tangentX = -directionY;
        var tangentY = directionX;

        for (var y = 0; y < DisplacementMapSize; y++)
        {
            for (var x = 0; x < DisplacementMapSize; x++)
            {
                var normalizedX = (x - center) / center;
                var normalizedY = (y - center) / center;
                var ellipticalX = normalizedX / curvature.EllipseX;
                var ellipticalY = normalizedY / curvature.EllipseY;
                var radius = MathF.Sqrt(ellipticalX * ellipticalX + ellipticalY * ellipticalY);

                var softenedRadius = Math.Clamp(radius, 0.0f, 1.0f);
                var bodySlope = SmoothStep(curvature.CenterSoftness, 0.68f, softenedRadius) * (1.0f - SmoothStep(0.88f, 1.0f, softenedRadius));
                var rimSlope = SmoothStep(curvature.RimStart, curvature.RimEnd, softenedRadius) * (1.0f - SmoothStep(0.94f, 1.0f, softenedRadius));
                var meniscusSlope = SmoothStep(curvature.MeniscusStart, curvature.MeniscusEnd, softenedRadius) * (1.0f - SmoothStep(0.985f, 1.0f, softenedRadius));
                var slope = Math.Clamp(
                    bodySlope * curvature.BodyStrength
                    + rimSlope * curvature.RimStrength
                    + meniscusSlope * curvature.MeniscusStrength,
                    0.0f,
                    1.0f);

                var reciprocalRadius = radius <= 0.001f ? 0.0f : 1.0f / radius;
                var normalX = ellipticalX * reciprocalRadius;
                var normalY = ellipticalY * reciprocalRadius;
                var innerLensSlope = SmoothStep(0.02f, 0.44f, softenedRadius) * (1.0f - SmoothStep(0.74f, 0.96f, softenedRadius));
                var shoulderSlope = SmoothStep(0.36f, 0.62f, softenedRadius) * (1.0f - SmoothStep(0.80f, 0.98f, softenedRadius));
                var rimReturnSlope = SmoothStep(0.74f, 0.92f, softenedRadius) * (1.0f - SmoothStep(0.965f, 1.0f, softenedRadius));
                var diagonalShear = (normalizedX - normalizedY) * curvature.ShearStrength * (1.0f - SmoothStep(0.76f, 1.0f, softenedRadius));
                var meniscusPull = (meniscusSlope - rimSlope * 0.35f) * curvature.MeniscusPull;
                var directionalDot = (normalX * directionX) + (normalY * directionY);
                var directionalBend = rimSlope * directionalDot * curvature.RimStrength * 0.78f;
                var axialPull = (bodySlope + innerLensSlope * 0.72f) * directionalDot * curvature.BodyStrength * 0.30f;
                var tangentCoordinate = (normalizedX * tangentX) + (normalizedY * tangentY);
                var flowWave = MathF.Sin((tangentCoordinate * MathF.PI * 2.35f) + ((directionX - directionY) * 0.72f))
                    * (bodySlope + shoulderSlope * 0.68f)
                    * curvature.ShearStrength
                    * 0.96f;
                var crossCurve = normalizedX * normalizedY * rimSlope * curvature.ShearStrength * 0.82f;
                var tangentBend = tangentCoordinate * rimSlope * curvature.ShearStrength * 0.52f;
                var opticalSlope = slope
                    + innerLensSlope * curvature.BodyStrength * 0.46f
                    + shoulderSlope * curvature.RimStrength * 0.34f
                    - rimReturnSlope * curvature.MeniscusStrength * 0.24f;

                var red = NormalToChannel(
                    normalX * opticalSlope
                    + diagonalShear
                    + crossCurve
                    - meniscusPull
                    + directionX * (directionalBend + axialPull)
                    + tangentX * (flowWave + tangentBend),
                    curvature.ChannelScale);
                var green = NormalToChannel(
                    normalY * opticalSlope
                    - diagonalShear
                    - crossCurve
                    + meniscusPull
                    + directionY * (directionalBend + axialPull)
                    + tangentY * (flowWave + tangentBend),
                    curvature.ChannelScale);
                var index = (y * DisplacementMapSize + x) * 4;

                pixels[index] = NeutralDisplacementChannel;
                pixels[index + 1] = green;
                pixels[index + 2] = red;
                pixels[index + 3] = 255;
            }
        }

        return pixels;
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        var normalized = Math.Clamp((value - edge0) / (edge1 - edge0), 0.0f, 1.0f);
        return normalized * normalized * (3.0f - 2.0f * normalized);
    }

    private static byte NormalToChannel(float value, float channelScale)
    {
        return (byte)Math.Clamp((int)Math.Round(NeutralDisplacementChannel + value * channelScale), 20, 236);
    }

    private static CompositionBrush CreateDirectionalDisplacementMap(Compositor compositor, Vector2 sampleOffset)
    {
        var brush = compositor.CreateLinearGradientBrush();
        var horizontalWeight = Math.Abs(sampleOffset.X) / Math.Max(1.0f, sampleOffset.Length());
        var verticalWeight = 1.0f - horizontalWeight;
        var horizontalDirection = sampleOffset.X >= 0 ? 1.0f : -1.0f;
        var verticalDirection = sampleOffset.Y >= 0 ? 1.0f : -1.0f;

        brush.StartPoint = horizontalWeight >= verticalWeight ? new Vector2(0.0f, 0.5f) : new Vector2(0.5f, 0.0f);
        brush.EndPoint = horizontalWeight >= verticalWeight ? new Vector2(1.0f, 0.5f) : new Vector2(0.5f, 1.0f);

        var leftRed = DisplacementChannel(-horizontalDirection, horizontalWeight);
        var leftGreen = DisplacementChannel(-verticalDirection, verticalWeight);
        var rightRed = DisplacementChannel(horizontalDirection, horizontalWeight);
        var rightGreen = DisplacementChannel(verticalDirection, verticalWeight);

        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.0f, Color.FromArgb(255, leftRed, leftGreen, NeutralDisplacementChannel)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.18f, Color.FromArgb(255, BlendChannel(leftRed, NeutralDisplacementChannel, 0.38f), BlendChannel(leftGreen, NeutralDisplacementChannel, 0.38f), NeutralDisplacementChannel)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.50f, Color.FromArgb(255, NeutralDisplacementChannel, NeutralDisplacementChannel, NeutralDisplacementChannel)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(0.82f, Color.FromArgb(255, BlendChannel(rightRed, NeutralDisplacementChannel, 0.38f), BlendChannel(rightGreen, NeutralDisplacementChannel, 0.38f), NeutralDisplacementChannel)));
        brush.ColorStops.Add(compositor.CreateColorGradientStop(1.0f, Color.FromArgb(255, rightRed, rightGreen, NeutralDisplacementChannel)));

        return brush;
    }

    private static byte DisplacementChannel(float direction, float weight)
    {
        var weightedOffset = 112.0f * Math.Clamp(weight, 0.22f, 1.0f) * Math.Clamp(direction, -1.0f, 1.0f);
        return (byte)Math.Clamp((int)Math.Round(NeutralDisplacementChannel + weightedOffset), 24, 232);
    }

    private static byte BlendChannel(byte value, byte neutral, float neutralWeight)
    {
        var blended = value * (1.0f - neutralWeight) + neutral * neutralWeight;
        return (byte)Math.Clamp((int)Math.Round(blended), 0, 255);
    }

    private static void BindSize(Compositor compositor, Visual target, Visual source)
    {
        var sizeExpression = compositor.CreateExpressionAnimation("source.Size");
        sizeExpression.SetReferenceParameter("source", source);
        target.StartAnimation("Size", sizeExpression);
    }

    private static void BindCenterPoint(Compositor compositor, Visual target)
    {
        var centerPointExpression = compositor.CreateExpressionAnimation("Vector3(target.Size.X * 0.5, target.Size.Y * 0.5, 0)");
        centerPointExpression.SetReferenceParameter("target", target);
        target.StartAnimation("CenterPoint", centerPointExpression);
    }

    private static void ApplyRoundedClip(Compositor compositor, Visual target, float radius)
    {
        var cornerRadius = new Vector2(radius);
        var clip = compositor.CreateRectangleClip(
            0.0f,
            0.0f,
            1.0f,
            1.0f,
            cornerRadius,
            cornerRadius,
            cornerRadius,
            cornerRadius);
        BindClipToVisualSize(compositor, clip, target);
        target.Clip = clip;
    }

    private static void BindClipToVisualSize(Compositor compositor, CompositionClip clip, Visual source)
    {
        var rightExpression = compositor.CreateExpressionAnimation("source.Size.X");
        rightExpression.SetReferenceParameter("source", source);
        clip.StartAnimation("Right", rightExpression);

        var bottomExpression = compositor.CreateExpressionAnimation("source.Size.Y");
        bottomExpression.SetReferenceParameter("source", source);
        clip.StartAnimation("Bottom", bottomExpression);
    }

    private static void BindRelativeSize(Compositor compositor, Visual target, Visual source, Vector2 multiplier)
    {
        var sizeExpression = compositor.CreateExpressionAnimation("Vector2(source.Size.X * multiplier.X, source.Size.Y * multiplier.Y)");
        sizeExpression.SetReferenceParameter("source", source);
        sizeExpression.SetVector2Parameter("multiplier", multiplier);
        target.StartAnimation("Size", sizeExpression);
    }

    private static void BindRelativeOffset(Compositor compositor, Visual target, Visual source, Vector2 multiplier)
    {
        var offsetExpression = compositor.CreateExpressionAnimation("Vector3(source.Size.X * multiplier.X, source.Size.Y * multiplier.Y, 0)");
        offsetExpression.SetReferenceParameter("source", source);
        offsetExpression.SetVector2Parameter("multiplier", multiplier);
        target.StartAnimation("Offset", offsetExpression);
    }

    private static void BindOverscanSize(Compositor compositor, Visual target, Visual source, float overscan)
    {
        var sizeExpression = compositor.CreateExpressionAnimation("Vector2(source.Size.X + overscan * 2, source.Size.Y + overscan * 2)");
        sizeExpression.SetReferenceParameter("source", source);
        sizeExpression.SetScalarParameter("overscan", overscan);
        target.StartAnimation("Size", sizeExpression);
    }

    private static void BindWidth(Compositor compositor, Visual target, Visual source, float height)
    {
        var widthExpression = compositor.CreateExpressionAnimation("Vector2(source.Size.X, edgeHeight)");
        widthExpression.SetReferenceParameter("source", source);
        widthExpression.SetScalarParameter("edgeHeight", height);
        target.StartAnimation("Size", widthExpression);
    }

    private static void BindHeight(Compositor compositor, Visual target, Visual source, float width)
    {
        var heightExpression = compositor.CreateExpressionAnimation("Vector2(edgeWidth, source.Size.Y)");
        heightExpression.SetReferenceParameter("source", source);
        heightExpression.SetScalarParameter("edgeWidth", width);
        target.StartAnimation("Size", heightExpression);
    }

    private static void BindGlintSize(
        Compositor compositor,
        Visual target,
        Visual source,
        float widthScale,
        float heightScale)
    {
        var sizeExpression = compositor.CreateExpressionAnimation("Vector2(source.Size.X * widthScale + 24, source.Size.Y * heightScale + 6)");
        sizeExpression.SetReferenceParameter("source", source);
        sizeExpression.SetScalarParameter("widthScale", widthScale);
        sizeExpression.SetScalarParameter("heightScale", heightScale);
        target.StartAnimation("Size", sizeExpression);
    }

    private static void CenterGlint(Compositor compositor, Visual target, Visual source, float verticalBias)
    {
        var offsetExpression = compositor.CreateExpressionAnimation("Vector3((source.Size.X - target.Size.X) * 0.5, source.Size.Y * verticalBias, 0)");
        offsetExpression.SetReferenceParameter("source", source);
        offsetExpression.SetReferenceParameter("target", target);
        offsetExpression.SetScalarParameter("verticalBias", verticalBias);
        target.StartAnimation("Offset", offsetExpression);
    }

    private static void BindBottomOffset(
        Compositor compositor,
        Visual target,
        Visual source,
        float height,
        Vector2 sampleOffset)
    {
        var offsetExpression = compositor.CreateExpressionAnimation("Vector3(baseOffset.X, source.Size.Y - edgeHeight + baseOffset.Y, 0)");
        offsetExpression.SetReferenceParameter("source", source);
        offsetExpression.SetVector2Parameter("baseOffset", sampleOffset);
        offsetExpression.SetScalarParameter("edgeHeight", height);
        target.StartAnimation("Offset", offsetExpression);
    }

    private static void BindRightOffset(
        Compositor compositor,
        Visual target,
        Visual source,
        float width,
        Vector2 sampleOffset)
    {
        var offsetExpression = compositor.CreateExpressionAnimation("Vector3(source.Size.X - edgeWidth + baseOffset.X, baseOffset.Y, 0)");
        offsetExpression.SetReferenceParameter("source", source);
        offsetExpression.SetVector2Parameter("baseOffset", sampleOffset);
        offsetExpression.SetScalarParameter("edgeWidth", width);
        target.StartAnimation("Offset", offsetExpression);
    }

    private static void StartOffsetDrift(
        Compositor compositor,
        Visual target,
        Vector2 anchorOffset,
        float driftAmount,
        double durationSeconds)
    {
        var animation = compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromSeconds(durationSeconds);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.Direction = AnimationDirection.Alternate;
        animation.InsertKeyFrame(0.0f, new Vector3(anchorOffset.X - driftAmount, anchorOffset.Y + driftAmount * 0.35f, 0));
        animation.InsertKeyFrame(0.55f, new Vector3(anchorOffset.X + driftAmount * 0.45f, anchorOffset.Y - driftAmount * 0.30f, 0));
        animation.InsertKeyFrame(1.0f, new Vector3(anchorOffset.X + driftAmount, anchorOffset.Y - driftAmount * 0.45f, 0));
        target.StartAnimation("Offset", animation);
    }

    private static void StartOpacityBreath(Compositor compositor, Visual target, float baseOpacity, double durationSeconds)
    {
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Duration = TimeSpan.FromSeconds(durationSeconds);
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
        animation.Direction = AnimationDirection.Alternate;
        animation.InsertKeyFrame(0.0f, Math.Max(0, baseOpacity - 0.03f));
        animation.InsertKeyFrame(1.0f, Math.Min(1, baseOpacity + 0.035f));
        target.StartAnimation("Opacity", animation);
    }

    private sealed class DisplacementMapCache
    {
        public Dictionary<string, CompositionBrush> Brushes { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct CurvatureProfile(
        string Key,
        float EllipseX,
        float EllipseY,
        float BodyStrength,
        float RimStrength,
        float MeniscusStrength,
        float ShearStrength,
        float ChannelScale,
        float CenterSoftness,
        float RimStart,
        float RimEnd,
        float MeniscusStart,
        float MeniscusEnd,
        float MeniscusPull)
    {
        public static CurvatureProfile Card { get; } = new(
            "card",
            EllipseX: 0.94f,
            EllipseY: 0.84f,
            BodyStrength: 0.42f,
            RimStrength: 0.28f,
            MeniscusStrength: 0.12f,
            ShearStrength: 0.034f,
            ChannelScale: 72.0f,
            CenterSoftness: 0.14f,
            RimStart: 0.70f,
            RimEnd: 0.94f,
            MeniscusStart: 0.86f,
            MeniscusEnd: 0.98f,
            MeniscusPull: 0.045f);

        public static CurvatureProfile Lane { get; } = new(
            "lane",
            EllipseX: 1.08f,
            EllipseY: 0.94f,
            BodyStrength: 0.22f,
            RimStrength: 0.16f,
            MeniscusStrength: 0.06f,
            ShearStrength: 0.016f,
            ChannelScale: 52.0f,
            CenterSoftness: 0.18f,
            RimStart: 0.76f,
            RimEnd: 0.96f,
            MeniscusStart: 0.90f,
            MeniscusEnd: 0.99f,
            MeniscusPull: 0.022f);

        public static CurvatureProfile Toolbar { get; } = new(
            "toolbar",
            EllipseX: 0.86f,
            EllipseY: 0.72f,
            BodyStrength: 1.02f,
            RimStrength: 0.76f,
            MeniscusStrength: 0.48f,
            ShearStrength: 0.132f,
            ChannelScale: 132.0f,
            CenterSoftness: 0.06f,
            RimStart: 0.58f,
            RimEnd: 0.90f,
            MeniscusStart: 0.76f,
            MeniscusEnd: 0.97f,
            MeniscusPull: 0.20f);

        public static CurvatureProfile Sidebar { get; } = new(
            "sidebar",
            EllipseX: 0.72f,
            EllipseY: 1.14f,
            BodyStrength: 0.64f,
            RimStrength: 0.48f,
            MeniscusStrength: 0.24f,
            ShearStrength: 0.046f,
            ChannelScale: 92.0f,
            CenterSoftness: 0.12f,
            RimStart: 0.62f,
            RimEnd: 0.94f,
            MeniscusStart: 0.82f,
            MeniscusEnd: 0.98f,
            MeniscusPull: 0.095f);

        public static CurvatureProfile TitleCapsule { get; } = new(
            "title-capsule",
            EllipseX: 0.76f,
            EllipseY: 0.56f,
            BodyStrength: 0.62f,
            RimStrength: 0.46f,
            MeniscusStrength: 0.24f,
            ShearStrength: 0.060f,
            ChannelScale: 92.0f,
            CenterSoftness: 0.082f,
            RimStart: 0.62f,
            RimEnd: 0.92f,
            MeniscusStart: 0.80f,
            MeniscusEnd: 0.975f,
            MeniscusPull: 0.105f);

        public static CurvatureProfile Panel { get; } = new(
            "panel",
            EllipseX: 0.92f,
            EllipseY: 0.80f,
            BodyStrength: 0.54f,
            RimStrength: 0.38f,
            MeniscusStrength: 0.20f,
            ShearStrength: 0.060f,
            ChannelScale: 88.0f,
            CenterSoftness: 0.105f,
            RimStart: 0.66f,
            RimEnd: 0.93f,
            MeniscusStart: 0.82f,
            MeniscusEnd: 0.975f,
            MeniscusPull: 0.085f);

        public static CurvatureProfile ContentPanel { get; } = new(
            "content-panel",
            EllipseX: 1.08f,
            EllipseY: 0.86f,
            BodyStrength: 0.34f,
            RimStrength: 0.22f,
            MeniscusStrength: 0.095f,
            ShearStrength: 0.024f,
            ChannelScale: 62.0f,
            CenterSoftness: 0.18f,
            RimStart: 0.74f,
            RimEnd: 0.965f,
            MeniscusStart: 0.88f,
            MeniscusEnd: 0.99f,
            MeniscusPull: 0.035f);

        public static CurvatureProfile Flyout { get; } = new(
            "flyout",
            EllipseX: 0.84f,
            EllipseY: 0.70f,
            BodyStrength: 0.82f,
            RimStrength: 0.66f,
            MeniscusStrength: 0.40f,
            ShearStrength: 0.090f,
            ChannelScale: 108.0f,
            CenterSoftness: 0.082f,
            RimStart: 0.58f,
            RimEnd: 0.91f,
            MeniscusStart: 0.76f,
            MeniscusEnd: 0.975f,
            MeniscusPull: 0.155f);
    }

    private readonly record struct HighlightProfile(
        float ScaleX,
        float ScaleY,
        float ColorAlphaScale,
        float WhiteAlphaScale,
        float PrimaryRotation,
        float SecondaryRotation,
        float HotRotation,
        float PrimaryBias,
        float SecondaryBias,
        float HotBias,
        float PrimaryPeak,
        float PrimaryRest,
        float SecondaryPeak,
        float SecondaryRest,
        float HotPeak,
        float HotRest)
    {
        public static HighlightProfile Card { get; } = new(
            ScaleX: 0.68f,
            ScaleY: 0.13f,
            ColorAlphaScale: 0.38f,
            WhiteAlphaScale: 0.42f,
            PrimaryRotation: -11.4f,
            SecondaryRotation: -8.8f,
            HotRotation: -10.2f,
            PrimaryBias: 0.10f,
            SecondaryBias: 0.066f,
            HotBias: 0.075f,
            PrimaryPeak: 0.088f,
            PrimaryRest: 0.028f,
            SecondaryPeak: 0.040f,
            SecondaryRest: 0.010f,
            HotPeak: 0.032f,
            HotRest: 0.008f);

        public static HighlightProfile Lane { get; } = new(
            ScaleX: 0.78f,
            ScaleY: 0.075f,
            ColorAlphaScale: 0.22f,
            WhiteAlphaScale: 0.25f,
            PrimaryRotation: -6.2f,
            SecondaryRotation: -4.6f,
            HotRotation: -5.4f,
            PrimaryBias: 0.060f,
            SecondaryBias: 0.038f,
            HotBias: 0.044f,
            PrimaryPeak: 0.046f,
            PrimaryRest: 0.014f,
            SecondaryPeak: 0.020f,
            SecondaryRest: 0.005f,
            HotPeak: 0.016f,
            HotRest: 0.004f);

        public static HighlightProfile Toolbar { get; } = new(
            ScaleX: 0.86f,
            ScaleY: 0.18f,
            ColorAlphaScale: 1.16f,
            WhiteAlphaScale: 1.20f,
            PrimaryRotation: -13.6f,
            SecondaryRotation: -10.8f,
            HotRotation: -12.2f,
            PrimaryBias: 0.205f,
            SecondaryBias: 0.145f,
            HotBias: 0.17f,
            PrimaryPeak: 0.38f,
            PrimaryRest: 0.135f,
            SecondaryPeak: 0.180f,
            SecondaryRest: 0.050f,
            HotPeak: 0.155f,
            HotRest: 0.042f);

        public static HighlightProfile Sidebar { get; } = new(
            ScaleX: 0.62f,
            ScaleY: 0.24f,
            ColorAlphaScale: 0.72f,
            WhiteAlphaScale: 0.78f,
            PrimaryRotation: -8.6f,
            SecondaryRotation: -6.4f,
            HotRotation: -7.8f,
            PrimaryBias: 0.145f,
            SecondaryBias: 0.098f,
            HotBias: 0.112f,
            PrimaryPeak: 0.215f,
            PrimaryRest: 0.070f,
            SecondaryPeak: 0.094f,
            SecondaryRest: 0.026f,
            HotPeak: 0.078f,
            HotRest: 0.020f);

        public static HighlightProfile TitleCapsule { get; } = new(
            ScaleX: 0.66f,
            ScaleY: 0.105f,
            ColorAlphaScale: 0.66f,
            WhiteAlphaScale: 0.72f,
            PrimaryRotation: -10.8f,
            SecondaryRotation: -8.2f,
            HotRotation: -9.6f,
            PrimaryBias: 0.135f,
            SecondaryBias: 0.090f,
            HotBias: 0.105f,
            PrimaryPeak: 0.185f,
            PrimaryRest: 0.055f,
            SecondaryPeak: 0.074f,
            SecondaryRest: 0.018f,
            HotPeak: 0.060f,
            HotRest: 0.014f);

        public static HighlightProfile Panel { get; } = new(
            ScaleX: 0.76f,
            ScaleY: 0.16f,
            ColorAlphaScale: 0.58f,
            WhiteAlphaScale: 0.62f,
            PrimaryRotation: -12.4f,
            SecondaryRotation: -9.6f,
            HotRotation: -11.1f,
            PrimaryBias: 0.135f,
            SecondaryBias: 0.092f,
            HotBias: 0.108f,
            PrimaryPeak: 0.180f,
            PrimaryRest: 0.060f,
            SecondaryPeak: 0.082f,
            SecondaryRest: 0.022f,
            HotPeak: 0.070f,
            HotRest: 0.018f);

        public static HighlightProfile ContentPanel { get; } = new(
            ScaleX: 0.82f,
            ScaleY: 0.105f,
            ColorAlphaScale: 0.30f,
            WhiteAlphaScale: 0.36f,
            PrimaryRotation: -7.2f,
            SecondaryRotation: -5.6f,
            HotRotation: -6.4f,
            PrimaryBias: 0.082f,
            SecondaryBias: 0.052f,
            HotBias: 0.060f,
            PrimaryPeak: 0.072f,
            PrimaryRest: 0.022f,
            SecondaryPeak: 0.030f,
            SecondaryRest: 0.008f,
            HotPeak: 0.024f,
            HotRest: 0.006f);

        public static HighlightProfile Flyout { get; } = new(
            ScaleX: 0.74f,
            ScaleY: 0.15f,
            ColorAlphaScale: 0.94f,
            WhiteAlphaScale: 1.02f,
            PrimaryRotation: -10.6f,
            SecondaryRotation: -8.0f,
            HotRotation: -9.4f,
            PrimaryBias: 0.165f,
            SecondaryBias: 0.115f,
            HotBias: 0.145f,
            PrimaryPeak: 0.275f,
            PrimaryRest: 0.088f,
            SecondaryPeak: 0.122f,
            SecondaryRest: 0.030f,
            HotPeak: 0.102f,
            HotRest: 0.026f);
    }

    private readonly record struct ObliqueRefractionProfile(
        float Opacity,
        float PrimaryBandHeight,
        float CrossRibbonHeight,
        float FringeHeight,
        float FringeOpacity,
        float SampleScale,
        float AmountScale,
        float DriftAmount,
        float PrimaryRotation,
        float SecondaryRotation,
        float CrossRotation)
    {
        public static ObliqueRefractionProfile Card { get; } = new(
            Opacity: 0.062f,
            PrimaryBandHeight: 0.122f,
            CrossRibbonHeight: 0.060f,
            FringeHeight: 0.018f,
            FringeOpacity: 0.038f,
            SampleScale: 0.92f,
            AmountScale: 0.90f,
            DriftAmount: 0.35f,
            PrimaryRotation: -4.6f,
            SecondaryRotation: 3.8f,
            CrossRotation: -7.0f);

        public static ObliqueRefractionProfile Lane { get; } = new(
            Opacity: 0.028f,
            PrimaryBandHeight: 0.092f,
            CrossRibbonHeight: 0.046f,
            FringeHeight: 0.014f,
            FringeOpacity: 0.020f,
            SampleScale: 0.80f,
            AmountScale: 0.72f,
            DriftAmount: 0.16f,
            PrimaryRotation: -3.2f,
            SecondaryRotation: 2.6f,
            CrossRotation: -4.8f);

        public static ObliqueRefractionProfile Toolbar { get; } = new(
            Opacity: 0.360f,
            PrimaryBandHeight: 0.258f,
            CrossRibbonHeight: 0.150f,
            FringeHeight: 0.046f,
            FringeOpacity: 0.220f,
            SampleScale: 1.42f,
            AmountScale: 1.84f,
            DriftAmount: 4.2f,
            PrimaryRotation: -8.8f,
            SecondaryRotation: 7.2f,
            CrossRotation: -14.0f);

        public static ObliqueRefractionProfile Sidebar { get; } = new(
            Opacity: 0.168f,
            PrimaryBandHeight: 0.216f,
            CrossRibbonHeight: 0.086f,
            FringeHeight: 0.026f,
            FringeOpacity: 0.092f,
            SampleScale: 1.04f,
            AmountScale: 1.16f,
            DriftAmount: 0.55f,
            PrimaryRotation: -6.2f,
            SecondaryRotation: 4.8f,
            CrossRotation: -9.6f);

        public static ObliqueRefractionProfile TitleCapsule { get; } = new(
            Opacity: 0.132f,
            PrimaryBandHeight: 0.168f,
            CrossRibbonHeight: 0.078f,
            FringeHeight: 0.022f,
            FringeOpacity: 0.070f,
            SampleScale: 0.92f,
            AmountScale: 1.06f,
            DriftAmount: 0.42f,
            PrimaryRotation: -5.4f,
            SecondaryRotation: 4.0f,
            CrossRotation: -8.4f);

        public static ObliqueRefractionProfile Panel { get; } = new(
            Opacity: 0.150f,
            PrimaryBandHeight: 0.174f,
            CrossRibbonHeight: 0.090f,
            FringeHeight: 0.026f,
            FringeOpacity: 0.086f,
            SampleScale: 1.06f,
            AmountScale: 1.12f,
            DriftAmount: 0.75f,
            PrimaryRotation: -8.1f,
            SecondaryRotation: 6.8f,
            CrossRotation: -13.4f);

        public static ObliqueRefractionProfile ContentPanel { get; } = new(
            Opacity: 0.060f,
            PrimaryBandHeight: 0.118f,
            CrossRibbonHeight: 0.052f,
            FringeHeight: 0.014f,
            FringeOpacity: 0.034f,
            SampleScale: 0.78f,
            AmountScale: 0.70f,
            DriftAmount: 0.16f,
            PrimaryRotation: -4.0f,
            SecondaryRotation: 3.2f,
            CrossRotation: -6.4f);

        public static ObliqueRefractionProfile Flyout { get; } = new(
            Opacity: 0.246f,
            PrimaryBandHeight: 0.206f,
            CrossRibbonHeight: 0.108f,
            FringeHeight: 0.032f,
            FringeOpacity: 0.142f,
            SampleScale: 1.26f,
            AmountScale: 1.54f,
            DriftAmount: 1.1f,
            PrimaryRotation: -6.8f,
            SecondaryRotation: 5.2f,
            CrossRotation: -10.6f);
    }

    private readonly record struct SoftMaterialProfile(
        float BlurAmount,
        float Saturation,
        float Opacity,
        float Overscan)
    {
        public static SoftMaterialProfile Card { get; } = new(
            BlurAmount: 11.0f,
            Saturation: 1.16f,
            Opacity: 0.070f,
            Overscan: 18.0f);

        public static SoftMaterialProfile Lane { get; } = new(
            BlurAmount: 8.0f,
            Saturation: 1.08f,
            Opacity: 0.036f,
            Overscan: 16.0f);

        public static SoftMaterialProfile Toolbar { get; } = new(
            BlurAmount: 15.5f,
            Saturation: 1.22f,
            Opacity: 0.235f,
            Overscan: 22.0f);

        public static SoftMaterialProfile Sidebar { get; } = new(
            BlurAmount: 14.5f,
            Saturation: 1.18f,
            Opacity: 0.150f,
            Overscan: 24.0f);

        public static SoftMaterialProfile TitleCapsule { get; } = new(
            BlurAmount: 10.0f,
            Saturation: 1.14f,
            Opacity: 0.105f,
            Overscan: 14.0f);

        public static SoftMaterialProfile Panel { get; } = new(
            BlurAmount: 14.0f,
            Saturation: 1.20f,
            Opacity: 0.120f,
            Overscan: 22.0f);

        public static SoftMaterialProfile ContentPanel { get; } = new(
            BlurAmount: 10.5f,
            Saturation: 1.12f,
            Opacity: 0.058f,
            Overscan: 20.0f);

        public static SoftMaterialProfile Flyout { get; } = new(
            BlurAmount: 12.5f,
            Saturation: 1.16f,
            Opacity: 0.170f,
            Overscan: 18.0f);
    }

    private readonly record struct CornerLensProfile(
        float SizeScale,
        float Radius,
        float Opacity,
        float SampleScale,
        float AmountScale,
        float DriftAmount)
    {
        public static CornerLensProfile Card { get; } = new(
            SizeScale: 0.225f,
            Radius: 18.0f,
            Opacity: 0.034f,
            SampleScale: 0.60f,
            AmountScale: 0.56f,
            DriftAmount: 0.20f);

        public static CornerLensProfile Lane { get; } = new(
            SizeScale: 0.155f,
            Radius: 20.0f,
            Opacity: 0.016f,
            SampleScale: 0.48f,
            AmountScale: 0.44f,
            DriftAmount: 0.10f);

        public static CornerLensProfile Toolbar { get; } = new(
            SizeScale: 0.250f,
            Radius: 26.0f,
            Opacity: 0.215f,
            SampleScale: 1.12f,
            AmountScale: 1.22f,
            DriftAmount: 2.25f);

        public static CornerLensProfile Sidebar { get; } = new(
            SizeScale: 0.210f,
            Radius: 28.0f,
            Opacity: 0.104f,
            SampleScale: 0.86f,
            AmountScale: 0.96f,
            DriftAmount: 0.50f);

        public static CornerLensProfile TitleCapsule { get; } = new(
            SizeScale: 0.190f,
            Radius: 15.0f,
            Opacity: 0.078f,
            SampleScale: 0.70f,
            AmountScale: 0.78f,
            DriftAmount: 0.32f);

        public static CornerLensProfile Panel { get; } = new(
            SizeScale: 0.245f,
            Radius: 26.0f,
            Opacity: 0.090f,
            SampleScale: 0.82f,
            AmountScale: 0.88f,
            DriftAmount: 0.55f);

        public static CornerLensProfile ContentPanel { get; } = new(
            SizeScale: 0.170f,
            Radius: 26.0f,
            Opacity: 0.030f,
            SampleScale: 0.52f,
            AmountScale: 0.48f,
            DriftAmount: 0.14f);

        public static CornerLensProfile Flyout { get; } = new(
            SizeScale: 0.220f,
            Radius: 24.0f,
            Opacity: 0.136f,
            SampleScale: 0.94f,
            AmountScale: 1.04f,
            DriftAmount: 0.9f);
    }

    private readonly record struct RimDepthProfile(
        float ShadowOpacity,
        float ShadowThickness,
        float CausticOpacity,
        float CausticThickness)
    {
        public static RimDepthProfile Card { get; } = new(
            ShadowOpacity: 0.052f,
            ShadowThickness: 10.0f,
            CausticOpacity: 0.032f,
            CausticThickness: 2.6f);

        public static RimDepthProfile Lane { get; } = new(
            ShadowOpacity: 0.026f,
            ShadowThickness: 7.0f,
            CausticOpacity: 0.014f,
            CausticThickness: 1.8f);

        public static RimDepthProfile Toolbar { get; } = new(
            ShadowOpacity: 0.238f,
            ShadowThickness: 23.0f,
            CausticOpacity: 0.192f,
            CausticThickness: 6.6f);

        public static RimDepthProfile Sidebar { get; } = new(
            ShadowOpacity: 0.130f,
            ShadowThickness: 19.0f,
            CausticOpacity: 0.088f,
            CausticThickness: 4.2f);

        public static RimDepthProfile TitleCapsule { get; } = new(
            ShadowOpacity: 0.096f,
            ShadowThickness: 10.0f,
            CausticOpacity: 0.062f,
            CausticThickness: 2.8f);

        public static RimDepthProfile Panel { get; } = new(
            ShadowOpacity: 0.110f,
            ShadowThickness: 15.0f,
            CausticOpacity: 0.076f,
            CausticThickness: 3.8f);

        public static RimDepthProfile ContentPanel { get; } = new(
            ShadowOpacity: 0.044f,
            ShadowThickness: 9.0f,
            CausticOpacity: 0.022f,
            CausticThickness: 2.0f);

        public static RimDepthProfile Flyout { get; } = new(
            ShadowOpacity: 0.184f,
            ShadowThickness: 17.0f,
            CausticOpacity: 0.148f,
            CausticThickness: 5.0f);
    }

    private readonly record struct DistortionProfile(
        float BaseOpacity,
        float CoolOpacity,
        float WarmOpacity,
        float EdgeOpacity,
        float LensOpacity,
        float PrismOpacity,
        float GlintAlphaScale,
        float CornerRadius,
        CurvatureProfile Curvature,
        HighlightProfile Highlight,
        ObliqueRefractionProfile Oblique,
        SoftMaterialProfile Material,
        CornerLensProfile CornerLens,
        RimDepthProfile Rim)
    {
        public static DistortionProfile FromTag(string? tag)
        {
            return tag switch
            {
                "Card" => new DistortionProfile(0.046f, 0.028f, 0.024f, 0.066f, 0.054f, 0.058f, 0.30f, 18.0f, CurvatureProfile.Card, HighlightProfile.Card, ObliqueRefractionProfile.Card, SoftMaterialProfile.Card, CornerLensProfile.Card, RimDepthProfile.Card),
                "Lane" => new DistortionProfile(0.022f, 0.014f, 0.012f, 0.030f, 0.024f, 0.026f, 0.18f, 24.0f, CurvatureProfile.Lane, HighlightProfile.Lane, ObliqueRefractionProfile.Lane, SoftMaterialProfile.Lane, CornerLensProfile.Lane, RimDepthProfile.Lane),
                "Toolbar" => new DistortionProfile(0.194f, 0.118f, 0.106f, 0.330f, 0.345f, 0.380f, 1.28f, 28.0f, CurvatureProfile.Toolbar, HighlightProfile.Toolbar, ObliqueRefractionProfile.Toolbar, SoftMaterialProfile.Toolbar, CornerLensProfile.Toolbar, RimDepthProfile.Toolbar),
                "Sidebar" => new DistortionProfile(0.104f, 0.064f, 0.052f, 0.168f, 0.156f, 0.168f, 0.66f, 28.0f, CurvatureProfile.Sidebar, HighlightProfile.Sidebar, ObliqueRefractionProfile.Sidebar, SoftMaterialProfile.Sidebar, CornerLensProfile.Sidebar, RimDepthProfile.Sidebar),
                "TitleCapsule" => new DistortionProfile(0.072f, 0.044f, 0.038f, 0.132f, 0.118f, 0.128f, 0.52f, 15.0f, CurvatureProfile.TitleCapsule, HighlightProfile.TitleCapsule, ObliqueRefractionProfile.TitleCapsule, SoftMaterialProfile.TitleCapsule, CornerLensProfile.TitleCapsule, RimDepthProfile.TitleCapsule),
                "ContentPanel" => new DistortionProfile(0.040f, 0.024f, 0.020f, 0.056f, 0.044f, 0.046f, 0.24f, 28.0f, CurvatureProfile.ContentPanel, HighlightProfile.ContentPanel, ObliqueRefractionProfile.ContentPanel, SoftMaterialProfile.ContentPanel, CornerLensProfile.ContentPanel, RimDepthProfile.ContentPanel),
                "Flyout" => new DistortionProfile(0.128f, 0.076f, 0.064f, 0.232f, 0.238f, 0.270f, 0.88f, 30.0f, CurvatureProfile.Flyout, HighlightProfile.Flyout, ObliqueRefractionProfile.Flyout, SoftMaterialProfile.Flyout, CornerLensProfile.Flyout, RimDepthProfile.Flyout),
                _ => new DistortionProfile(0.086f, 0.052f, 0.046f, 0.132f, 0.118f, 0.128f, 0.52f, 28.0f, CurvatureProfile.Panel, HighlightProfile.Panel, ObliqueRefractionProfile.Panel, SoftMaterialProfile.Panel, CornerLensProfile.Panel, RimDepthProfile.Panel)
            };
        }
    }
}
