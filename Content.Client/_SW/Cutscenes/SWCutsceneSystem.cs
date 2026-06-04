using System.Text;
using Content.Shared._SW.Cutscenes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.WebViewHook;
using Robust.Shared.Asynchronous;
using Robust.Shared.Utility;

namespace Content.Client._SW.Cutscenes;

public sealed class SWCutsceneSystem : EntitySystem
{
    private const string CutsceneFinishedUrl = "sw-cutscene://ended";

    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;
    [Dependency] private readonly IWebViewControlFactory _webViewFactory = default!;

    private LayoutContainer? _root;
    private int _activeCutsceneId;

    public override void Initialize()
    {
        SubscribeNetworkEvent<SWPlayCutsceneEvent>(OnPlayCutscene);
    }

    private void OnPlayCutscene(SWPlayCutsceneEvent ev)
    {
        CloseCurrent();
        var cutsceneId = ++_activeCutsceneId;

        _root = new LayoutContainer
        {
            MouseFilter = Control.MouseFilterMode.Stop,
        };
        LayoutContainer.SetAnchorPreset(_root, LayoutContainer.LayoutPreset.Wide);

        var player = CreateWebView(ev.Path, cutsceneId);
        LayoutContainer.SetAnchorPreset(player, LayoutContainer.LayoutPreset.Wide);
        _root.AddChild(player);

        _ui.PopupRoot.AddChild(_root);
        _root.SetPositionLast();
    }

    private Control CreateWebView(string path, int cutsceneId)
    {
        return _webViewFactory.CreateWebViewControl(BuildVideoPageUrl(path), true, url =>
        {
            if (!url.StartsWith(CutsceneFinishedUrl, StringComparison.OrdinalIgnoreCase))
                return false;

            _taskManager.RunOnMainThread(() => CloseCurrent(cutsceneId));
            return true;
        });
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
            video::-webkit-media-controls {
                display: none !important;
            }
            .close-button {
                position: fixed;
                top: 16px;
                right: 16px;
                z-index: 10;
                width: 42px;
                height: 42px;
                border: 1px solid rgba(255, 255, 255, 0.55);
                border-radius: 21px;
                background: rgba(0, 0, 0, 0.62);
                color: white;
                font: 28px/38px Arial, sans-serif;
                text-align: center;
                cursor: pointer;
            }
            .close-button:hover {
                background: rgba(50, 50, 50, 0.8);
            }
            </style>
            </head>
            <body>
            <video id="cutscene" autoplay playsinline>
                <source src="{{HtmlAttributeEncode(source)}}" type="{{sourceType}}">
            </video>
            <button id="close" class="close-button" type="button">X</button>
            <script>
            const video = document.getElementById('cutscene');
            const closeButton = document.getElementById('close');
            video.controls = false;
            video.disablePictureInPicture = true;
            video.addEventListener('ended', () => {
                window.location.href = '{{CutsceneFinishedUrl}}';
            }, { once: true });
            closeButton.addEventListener('click', () => {
                window.location.href = '{{CutsceneFinishedUrl}}';
            });
            </script>
            </body>
            </html>
            """;

        return $"data:text/html;charset=utf-8,{Uri.EscapeDataString(html)}";
    }

    private void CloseCurrent(int? cutsceneId = null)
    {
        if (cutsceneId != null && cutsceneId.Value != _activeCutsceneId)
            return;

        if (_root == null)
            return;

        _root?.Parent?.RemoveChild(_root);
        _root = null;
        _activeCutsceneId++;
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
