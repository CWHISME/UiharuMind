using System;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace UiharuMind.Views.Windows;

public partial class QuickChatResultWindow : Window
{
    public QuickChatResultWindow()
    {
        InitializeComponent();

        SizeToContent = SizeToContent.WidthAndHeight;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Test();
    }

    private StringBuilder sb = new StringBuilder();
    private Random random = new Random();

    private async void Test()
    {
        await RadomCharGenerator();
    }

    private async Task RadomCharGenerator()
    {
        while (true)
        {
            await Task.Delay(10);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 随机生成一个字符（假设为ASCII字符）
                char randomChar = (char)random.Next(32, 127);
                sb.Append(randomChar);
                ResultTextBlock.Text = sb.ToString();
            });
        }
    }
}