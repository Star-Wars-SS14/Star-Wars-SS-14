using System.Text;
using System.Numerics;
using Content.Shared._SW.Cutscenes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.WebViewHook;
using Robust.Shared.Utility;

namespace Content.Client._SW.Cutscenes;

public sealed class SWCutsceneSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IWebViewControlFactory _webViewFactory = default!;

    private LayoutContainer? _root;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SWPlayCutsceneEvent>(OnPlayCutscene);
    }

    private void OnPlayCutscene(SWPlayCutsceneEvent ev)
    {
        CloseCurrent();

        _root = new LayoutContainer
        {
            MouseFilter = Control.MouseFilterMode.Stop,
        };
        LayoutContainer.SetAnchorPreset(_root, LayoutContainer.LayoutPreset.Wide);

        var player = CreateWebView(ev.Path);
        LayoutContainer.SetAnchorPreset(player, LayoutContainer.LayoutPreset.Wide);
        _root.AddChild(player);

        var closeButton = new Button
        {
            Text = "Skip",
            MinSize = new Vector2(90, 36),
        };
        closeButton.OnPressed += _ => CloseCurrent();
        LayoutContainer.SetAnchorPreset(closeButton, LayoutContainer.LayoutPreset.TopRight);
        LayoutContainer.SetMarginTop(closeButton, 12);
        LayoutContainer.SetMarginRight(closeButton, 12);
        _root.AddChild(closeButton);

        _ui.PopupRoot.AddChild(_root);
        _root.SetPositionLast();
    }

    private Control CreateWebView(string path)
    {
        return _webViewFactory.CreateWebViewControl(BuildVideoPageUrl(path), true);
    }

    private static string BuildVideoPageUrl(string path)
    {
        var resourcePath = path.StartsWith('/') ? path : $"/{path}";
        var source = $"res://localhost{resourcePath}";
        var sourceType = resourcePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            ? "video/mp4"
            : "video/webm";

        var html = $$"""
            <!doctype html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
            html, body {
                width: 100%;
                height: 100%;
                margin: 0;
                overflow: hidden;
                background: black;
            }
            video {
                width: 100vw;
                height: 100vh;
                object-fit: contain;
                background: black;
            }
            </style>
            </head>
            <body>
            <video autoplay playsinline controls>
                <source src="{{HtmlAttributeEncode(source)}}" type="{{sourceType}}">
            </video>
            </body>
            </html>
            """;

        return $"data:text/html;charset=utf-8,{Uri.EscapeDataString(html)}";
    }

    private void CloseCurrent()
    {
        _root?.Parent?.RemoveChild(_root);
        _root = null;
    }

    private static string HtmlAttributeEncode(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => c,
            });
        }

        return builder.ToString();
    }
}
