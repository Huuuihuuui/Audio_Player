// 简单文本输入对话框
using System.Windows;

namespace MusicPlayer.Views;

public partial class InputDialog : Window
{
    public string Result { get; private set; } = string.Empty;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = defaultValue;
        InputBox.SelectAll();
        InputBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Result = InputBox.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
