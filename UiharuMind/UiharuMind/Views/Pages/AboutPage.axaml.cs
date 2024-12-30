/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace UiharuMind.Views.Pages;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        LinksPanel.Children.Add(CreateLink("Avalonia",
            "https://github.com/AvaloniaUI/Avalonia",
            "Cross-platform UI framework for dotnet"));

        LinksPanel.Children.Add(CreateLink("Avalonia.HtmlRenderer",
            "https://github.com/AvaloniaUI/Avalonia.HtmlRenderer",
            "Avalonia port of the HTMLRenderer project"));

        LinksPanel.Children.Add(CreateLink("CliWrap",
            "https://github.com/Tyrrrz/CliWrap",
            "Library for interacting with external command-line interfaces"));

        LinksPanel.Children.Add(CreateLink("Clowd.Clipboard",
            "https://github.com/clowd/Clowd.Clipboard",
            "This library is a light-weight clipboard replacement library for dotnet"));

        LinksPanel.Children.Add(CreateLink("Ursa.Avalonia",
            "https://github.com/irihitech/Ursa.Avalonia",
            "Ursa is a UI library for building cross-platform UIs with Avalonia UI"));

        LinksPanel.Children.Add(CreateLink("llama.cpp",
            "https://github.com/ggerganov/llama.cpp",
            "Inference of Meta's LLaMA model (and others) in pure C/C++"));

        LinksPanel.Children.Add(CreateLink("Microsoft.SemanticKernel",
            "https://github.com/microsoft/semantic-kernel",
            "Semantic Kernel is an SDK that integrates Large Language Models (LLMs) like OpenAI, Azure OpenAI, and Hugging Face with conventional programming languages like C#, Python, and Java."));

        LinksPanel.Children.Add(CreateLink("Downloader",
            "https://github.com/bezzad/Downloader",
            "Fast, cross-platform, and reliable multipart downloader in .Net"));

        LinksPanel.Children.Add(CreateLink("ScreenCapture.NET",
            "https://github.com/DarthAffe/ScreenCapture.NET",
            "Core functionality for Screen-Capturing"));

        LinksPanel.Children.Add(CreateLink("SharpHook",
            "https://github.com/TolikPylypchuk/SharpHook",
            "SharpHook provides a cross-platform global keyboard and mouse hook"));

        LinksPanel.Children.Add(CreateLink("AngleSharp",
            "https://github.com/AngleSharp/AngleSharp",
            "AngleSharp is a .NET library that gives you the ability to parse angle bracket based hyper-texts like HTML, SVG, and MathML."));

        LinksPanel.Children.Add(CreateLink("Markdown.ColorCode",
            "https://github.com/wbaldoumas/markdown-colorcode",
            "An extension for Markdig that adds syntax highlighting to code through the power of ColorCode."));

        LinksPanel.Children.Add(CreateLink("Markdig",
            "https://github.com/xoofx/markdig",
            "Markdig is a fast, powerful, CommonMark compliant, extensible Markdown processor for .NET."));
    }

    private Control CreateLink(string name, string uri, string description)
    {
        var link = new StackPanel
        {
            // Orientation = Orientation.Horizontal,
            Spacing = 5
        };

        Border border = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = this.FindResource("UiBorderBrush") as IBrush,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(5),
            Child = link
        };

        var title = new TextBlock
        {
            Text = name,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        ToolTip tip = new ToolTip
        {
            Content = description,
        };
        ToolTip.SetTip(title, tip);
        ToolTip.SetShowDelay(title, 0);

        link.Children.Add(title);
        link.Children.Add(new HyperlinkButton
        {
            Content = uri, NavigateUri = new Uri(uri), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 10
        });

        return border;
    }
}