// Version: 0.3.4-preview
// Generated/Updated/Coverage: 2026-10-30 (Asia/Shanghai)
// Source type: RAW (user provider-tab request), LOCAL-WORK (implementation).
// Node: Windows / Codex Desktop / Codex. Original source: current Lumi conversation.
using LumiCompanion.Infrastructure;
using LumiCompanion.Models;
using Controls = System.Windows.Controls;
using Wpf = System.Windows;

namespace LumiCompanion.UI;

public sealed class ProviderTabsEditorWindow : Wpf.Window
{
    private readonly Controls.ListBox _list;
    private readonly Controls.TextBox _name;
    private readonly Controls.TextBox _url;
    private readonly Controls.TextBlock _hint;
    private readonly Controls.TextBlock _error;
    private readonly Controls.Button _remove;
    private ProviderTab? _editing;
    private bool _populating;

    public ProviderTabsDocument Result { get; }

    public ProviderTabsEditorWindow(Wpf.Window owner, ProviderTabsDocument document)
    {
        Owner = owner;
        Title = "Lumi · 用量标签";
        Width = 490;
        Height = 350;
        ResizeMode = Wpf.ResizeMode.NoResize;
        WindowStartupLocation = Wpf.WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Result = document.Copy();

        var root = new Controls.Grid { Margin = new Wpf.Thickness(14) };
        root.RowDefinitions.Add(new Controls.RowDefinition());
        root.RowDefinitions.Add(new Controls.RowDefinition { Height = Wpf.GridLength.Auto });
        root.ColumnDefinitions.Add(new Controls.ColumnDefinition { Width = new Wpf.GridLength(130) });
        root.ColumnDefinitions.Add(new Controls.ColumnDefinition());
        Content = root;

        var left = new Controls.StackPanel { Margin = new Wpf.Thickness(0, 0, 12, 0) };
        _list = new Controls.ListBox { Height = 200, DisplayMemberPath = nameof(ProviderTab.Name) };
        _list.SelectionChanged += (_, _) => SelectTab();
        left.Children.Add(_list);
        var tabActions = new Controls.StackPanel { Orientation = Controls.Orientation.Horizontal };
        var add = Button("新增", (_, _) => AddTab());
        _remove = Button("移除", (_, _) => RemoveTab());
        tabActions.Children.Add(add);
        tabActions.Children.Add(_remove);
        left.Children.Add(tabActions);
        root.Children.Add(left);

        var fields = new Controls.StackPanel();
        Controls.Grid.SetColumn(fields, 1);
        root.Children.Add(fields);
        fields.Children.Add(new Controls.TextBlock { Text = "标签名称" });
        _name = new Controls.TextBox { Margin = new Wpf.Thickness(0, 5, 0, 10), MaxLength = 48 };
        fields.Children.Add(_name);
        fields.Children.Add(new Controls.TextBlock { Text = "用量页面链接（可留空）" });
        _url = new Controls.TextBox { Margin = new Wpf.Thickness(0, 5, 0, 10) };
        fields.Children.Add(_url);
        _hint = new Controls.TextBlock { TextWrapping = Wpf.TextWrapping.Wrap, FontSize = 12 };
        fields.Children.Add(_hint);
        _error = new Controls.TextBlock
        {
            TextWrapping = Wpf.TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Firebrick,
            Margin = new Wpf.Thickness(0, 10, 0, 0)
        };
        fields.Children.Add(_error);
        _name.TextChanged += (_, _) => CaptureFields();
        _url.TextChanged += (_, _) => CaptureFields();

        var footer = new Controls.StackPanel
        {
            Orientation = Controls.Orientation.Horizontal,
            HorizontalAlignment = Wpf.HorizontalAlignment.Right,
            Margin = new Wpf.Thickness(0, 10, 0, 0)
        };
        var cancel = Button("取消", (_, _) => DialogResult = false);
        cancel.IsCancel = true;
        var save = Button("保存", (_, _) => Save());
        save.IsDefault = true;
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        Controls.Grid.SetRow(footer, 1);
        Controls.Grid.SetColumnSpan(footer, 2);
        root.Children.Add(footer);

        Populate(Result.SelectedTabId);
    }

    private void Populate(string selectedId)
    {
        _populating = true;
        _list.ItemsSource = null;
        _list.ItemsSource = Result.Tabs;
        _populating = false;
        _list.SelectedItem = Result.Tabs.FirstOrDefault(tab => tab.Id == selectedId) ?? Result.Tabs[0];
    }

    private void SelectTab()
    {
        if (_populating || _list.SelectedItem is not ProviderTab tab) return;
        _populating = true;
        _editing = tab;
        _name.Text = tab.Name;
        _url.Text = tab.DashboardUrl;
        _remove.IsEnabled = !tab.IsCodex;
        _hint.Text = tab.IsCodex
            ? "Codex 显示本地限额快照；链接留空使用官方用量页，也可填写自己的页面。"
            : tab.Id == "workbuddy"
                ? "链接留空使用 WorkBuddy 官方套餐与用量页；可能需要登录。积分尚未自动读取。"
                : "此标签是网页入口，尚未自动读取用量。点击后在系统浏览器查看已登录的账户；套餐额度、API 用量与积分各自独立。";
        _populating = false;
        _error.Text = string.Empty;
    }

    private void CaptureFields()
    {
        if (_populating || _editing is null) return;
        _editing.Name = _name.Text;
        _editing.DashboardUrl = _url.Text;
        _list.Items.Refresh();
    }

    private void AddTab()
    {
        var tab = new ProviderTab { Name = "用量入口 " + (Result.Tabs.Count + 1) };
        Result.Tabs.Add(tab);
        Populate(tab.Id);
        _name.Focus();
        _name.SelectAll();
    }

    private void RemoveTab()
    {
        if (_editing is null || _editing.IsCodex) return;
        Result.Tabs.Remove(_editing);
        Populate("codex");
    }

    private void Save()
    {
        foreach (ProviderTab tab in Result.Tabs)
        {
            if (string.IsNullOrWhiteSpace(tab.Name))
            {
                Populate(tab.Id);
                _error.Text = "请填写标签名称。";
                return;
            }
            if (!string.IsNullOrWhiteSpace(tab.DashboardUrl) &&
                !ProviderTabsStore.TryGetDashboardUri(tab.DashboardUrl, out _))
            {
                Populate(tab.Id);
                _error.Text = "请填写 http 或 https 页面地址，链接中不要包含账号密码。";
                return;
            }
        }
        ProviderTabsStore.Normalize(Result);
        DialogResult = true;
    }

    private static Controls.Button Button(string text, Wpf.RoutedEventHandler click)
    {
        var button = new Controls.Button { Content = text, Padding = new Wpf.Thickness(9, 4, 9, 4), Margin = new Wpf.Thickness(2) };
        button.Click += click;
        return button;
    }
}
