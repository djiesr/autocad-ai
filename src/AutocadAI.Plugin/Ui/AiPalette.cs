using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace AutocadAI.Ui;

public sealed class AiPalette : UserControl
{
    private readonly RichTextBox _transcriptBox;
    private readonly TextBox _promptBox;
    private readonly Border _settingsPanel;
    private readonly Style _darkComboStyle;

    public AiPalette()
    {
        // Colors (same palette as XAML)
        var bg = new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27));
        var panel = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
        var border = new SolidColorBrush(Color.FromRgb(0x26, 0x32, 0x44));
        var text = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
        var muted = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
        var accent = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
        var inputBg = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));

        Background = bg;

        // In AutoCAD WPF host, simple setters can be overridden by theme resources.
        // Using explicit templates (ported from the original XAML) makes colors reliable.
        Style ParseStyle(string xaml) => (Style)XamlReader.Parse(xaml);

        var iconButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type Button}'>
  <Setter Property='Background' Value='#0B1220'/>
  <Setter Property='BorderBrush' Value='#263244'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Foreground' Value='#E5E7EB'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type Button}'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='6'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='Bd' Property='Background' Value='#1E3A5F'/>
            <Setter TargetName='Bd' Property='BorderBrush' Value='#3B82F6'/>
          </Trigger>
          <Trigger Property='IsPressed' Value='True'>
            <Setter TargetName='Bd' Property='Background' Value='#263244'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");

        var sendButtonStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type Button}'>
  <Setter Property='Background' Value='#3B82F6'/>
  <Setter Property='Foreground' Value='White'/>
  <Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type Button}'>
        <Border x:Name='Bd' Background='{TemplateBinding Background}' CornerRadius='10' Padding='12,6'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsEnabled' Value='False'>
            <Setter TargetName='Bd' Property='Background' Value='#475569'/>
            <Setter Property='Foreground' Value='#E2E8F0'/>
            <Setter Property='Opacity' Value='1'/>
          </Trigger>
          <Trigger Property='IsMouseOver' Value='True'>
            <Setter TargetName='Bd' Property='Background' Value='#2563EB'/>
          </Trigger>
          <Trigger Property='IsPressed' Value='True'>
            <Setter TargetName='Bd' Property='Background' Value='#1D4ED8'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");

        _darkComboStyle = ParseStyle(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
       xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
       TargetType='{x:Type ComboBox}'>
  <Setter Property='OverridesDefaultStyle' Value='True'/>
  <Setter Property='Foreground' Value='#E5E7EB'/>
  <Setter Property='Background' Value='#0B1220'/>
  <Setter Property='BorderBrush' Value='#263244'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='6,0'/>
  <Setter Property='SnapsToDevicePixels' Value='True'/>
  <Setter Property='ItemContainerStyle'>
    <Setter.Value>
      <Style TargetType='{x:Type ComboBoxItem}'>
        <Setter Property='OverridesDefaultStyle' Value='True'/>
        <Setter Property='Foreground' Value='#E5E7EB'/>
        <Setter Property='Background' Value='#0B1220'/>
        <Setter Property='Padding' Value='8,5'/>
        <Setter Property='Template'>
          <Setter.Value>
            <ControlTemplate TargetType='{x:Type ComboBoxItem}'>
              <Border x:Name='Bd' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
                <ContentPresenter TextElement.Foreground='#E5E7EB'/>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property='IsHighlighted' Value='True'>
                  <Setter TargetName='Bd' Property='Background' Value='#1E3A5F'/>
                </Trigger>
                <Trigger Property='IsSelected' Value='True'>
                  <Setter TargetName='Bd' Property='Background' Value='#263244'/>
                </Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Style>
    </Setter.Value>
  </Setter>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='{x:Type ComboBox}'>
        <Grid>
          <ToggleButton x:Name='ToggleButton' Focusable='False' ClickMode='Press'
                        IsChecked='{Binding Path=IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
            <ToggleButton.Template>
              <ControlTemplate TargetType='{x:Type ToggleButton}'>
                <Border x:Name='Bd' Background='#0B1220' BorderBrush='#263244' BorderThickness='1' CornerRadius='4'>
                  <Path x:Name='Arrow' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,8,0'
                        Data='M 0 0 L 4 4 L 8 0 Z' Fill='#9CA3AF'/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property='IsMouseOver' Value='True'>
                    <Setter TargetName='Bd' Property='BorderBrush' Value='#3B82F6'/>
                  </Trigger>
                  <Trigger Property='IsChecked' Value='True'>
                    <Setter TargetName='Bd' Property='BorderBrush' Value='#3B82F6'/>
                    <Setter TargetName='Arrow' Property='Fill' Value='#E5E7EB'/>
                  </Trigger>
                </ControlTemplate.Triggers>
              </ControlTemplate>
            </ToggleButton.Template>
          </ToggleButton>
          <ContentPresenter x:Name='ContentSite' IsHitTestVisible='False'
                            Content='{TemplateBinding SelectionBoxItem}'
                            ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'
                            Margin='8,0,24,0'
                            VerticalAlignment='Center'
                            HorizontalAlignment='Left'
                            TextBlock.Foreground='#E5E7EB'/>
          <Popup x:Name='Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}'
                 AllowsTransparency='True' Focusable='False' PopupAnimation='Slide'>
            <Border SnapsToDevicePixels='True' MinWidth='{TemplateBinding ActualWidth}' MaxHeight='200'
                    Background='#0B1220' BorderBrush='#263244' BorderThickness='1' CornerRadius='4'>
              <ScrollViewer SnapsToDevicePixels='True'>
                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Contained'/>
              </ScrollViewer>
            </Border>
          </Popup>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>");

        var root = new Grid { Background = bg, Margin = new Thickness(0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header
        var header = new Border
        {
            Background = panel,
            BorderBrush = border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 10, 12, 10)
        };
        Grid.SetRow(header, 0);

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Vertical };
        Grid.SetColumn(titleStack, 0);
        titleStack.Children.Add(new TextBlock
        {
            Text = "AutoCAD AI",
            Foreground = text,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        });

        var workspaceTb = new TextBlock
        {
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = muted,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        workspaceTb.SetBinding(TextBlock.TextProperty, new Binding("WorkspaceRoot"));
        titleStack.Children.Add(workspaceTb);

        var enginePolicyTb = new TextBlock
        {
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = muted,
            FontSize = 11
        };
        var engineRun = new Run();
        engineRun.SetBinding(Run.TextProperty, new Binding("PreferredEngine"));
        enginePolicyTb.Inlines.Add(engineRun);
        enginePolicyTb.Inlines.Add(new Run(" · "));
        var policyRun = new Run();
        policyRun.SetBinding(Run.TextProperty, new Binding("PolicyLevel"));
        enginePolicyTb.Inlines.Add(policyRun);
        titleStack.Children.Add(enginePolicyTb);

        headerGrid.Children.Add(titleStack);

        var headerButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetColumn(headerButtons, 1);

        var modeCombo = new ComboBox
        {
            Width = 110,
            Height = 28,
            Margin = new Thickness(0, 0, 8, 0)
        };
        modeCombo.Style = _darkComboStyle;
        modeCombo.Items.Add(new ComboBoxItem { Content = "AutoCAD", Tag = "autocad" });
        modeCombo.Items.Add(new ComboBoxItem { Content = "Général", Tag = "general" });
        modeCombo.SelectedValuePath = "Tag";
        modeCombo.SetBinding(ComboBox.SelectedValueProperty, new Binding("InteractionMode") { Mode = BindingMode.TwoWay });
        headerButtons.Children.Add(modeCombo);

        var clearBtn = new Button
        {
            Width = 34,
            Height = 28,
            Margin = new Thickness(4, 0, 0, 0),
            Content = "✕",
            ToolTip = "Effacer la conversation"
        };
        clearBtn.Style = iconButtonStyle;
        clearBtn.SetBinding(Button.CommandProperty, new Binding("ClearCommand"));
        headerButtons.Children.Add(clearBtn);

        var settingsBtn = new Button
        {
            Width = 34,
            Height = 28,
            Margin = new Thickness(4, 0, 0, 0),
            Content = "⚙",
            ToolTip = "Paramètres"
        };
        settingsBtn.Style = iconButtonStyle;
        settingsBtn.SetBinding(Button.CommandProperty, new Binding("ToggleSettingsCommand"));
        headerButtons.Children.Add(settingsBtn);

        headerGrid.Children.Add(headerButtons);
        header.Child = headerGrid;
        root.Children.Add(header);

        // Body
        var body = new Grid { Margin = new Thickness(12) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);

        _settingsPanel = BuildSettingsPanel(panel, border, text, muted, accent);
        Grid.SetRow(_settingsPanel, 0);
        _settingsPanel.SetBinding(VisibilityProperty, new Binding("SettingsOpen") { Converter = new BooleanToVisibilityConverter() });
        body.Children.Add(_settingsPanel);

        var transcriptBorder = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Background = panel,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10)
        };
        Grid.SetRow(transcriptBorder, 1);

        _transcriptBox = new RichTextBox
        {
            Background = panel,
            Foreground = text,
            BorderThickness = new Thickness(0),
            IsReadOnly = true,
            IsDocumentEnabled = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55))
        };
        transcriptBorder.Child = _transcriptBox;
        body.Children.Add(transcriptBorder);

        root.Children.Add(body);

        // Composer
        var composer = new Border
        {
            Background = panel,
            BorderBrush = border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(12, 10, 12, 10)
        };
        Grid.SetRow(composer, 2);

        var dock = new DockPanel { LastChildFill = true };
        var sendBtn = new Button
        {
            Width = 96,
            Height = 32,
            Margin = new Thickness(10, 0, 0, 0),
            Content = "Envoyer",
            FontWeight = FontWeights.SemiBold,
        };
        sendBtn.Style = sendButtonStyle;
        sendBtn.SetBinding(Button.CommandProperty, new Binding("SendCommand"));
        sendBtn.SetBinding(IsEnabledProperty, new Binding("Prompt") { Converter = new NonEmptyStringToBoolConverter() });
        DockPanel.SetDock(sendBtn, Dock.Right);
        dock.Children.Add(sendBtn);

        var inputBorder = new Border
        {
            Background = inputBg,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 6, 10, 6)
        };

        _promptBox = new TextBox
        {
            MinHeight = 50,
            Background = Brushes.Transparent,
            Foreground = text,
            CaretBrush = text,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Top,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap
        };
        _promptBox.SetBinding(TextBox.TextProperty, new Binding("Prompt") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        _promptBox.PreviewKeyDown += PromptBoxOnPreviewKeyDown;
        inputBorder.Child = _promptBox;
        dock.Children.Add(inputBorder);

        composer.Child = dock;
        root.Children.Add(composer);

        Content = root;

        DataContextChanged += OnDataContextChanged;
    }

    private Border BuildSettingsPanel(Brush panel, Brush border, Brush text, Brush muted, Brush accent)
    {
        var inputBg = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
        var outer = new Border
        {
            Background = panel,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 8; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock { Foreground = text, FontWeight = FontWeights.SemiBold, Text = "Paramètres" };
        Grid.SetRow(header, 0);
        Grid.SetColumnSpan(header, 2);
        grid.Children.Add(header);

        // Engine
        var engineLbl = new TextBlock { Margin = new Thickness(0, 10, 8, 2), Foreground = muted, Text = "Moteur", FontSize = 11 };
        Grid.SetRow(engineLbl, 1);
        Grid.SetColumn(engineLbl, 0);
        grid.Children.Add(engineLbl);

        var engineCombo = new ComboBox { Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        engineCombo.Style = _darkComboStyle;
        engineCombo.Items.Add("auto");
        engineCombo.Items.Add("local");
        engineCombo.Items.Add("cloud");
        engineCombo.SetBinding(ComboBox.SelectedValueProperty, new Binding("PreferredEngine") { Mode = BindingMode.TwoWay });
        Grid.SetRow(engineCombo, 2);
        Grid.SetColumn(engineCombo, 0);
        grid.Children.Add(engineCombo);

        // Policy
        var policyLbl = new TextBlock { Margin = new Thickness(8, 10, 0, 2), Foreground = muted, Text = "Politique", FontSize = 11 };
        Grid.SetRow(policyLbl, 1);
        Grid.SetColumn(policyLbl, 1);
        grid.Children.Add(policyLbl);

        var policyCombo = new ComboBox { Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        policyCombo.Style = _darkComboStyle;
        policyCombo.Items.Add("local-only");
        policyCombo.Items.Add("minimal-cloud");
        policyCombo.Items.Add("sanitized-cloud");
        policyCombo.SetBinding(ComboBox.SelectedValueProperty, new Binding("PolicyLevel") { Mode = BindingMode.TwoWay });
        Grid.SetRow(policyCombo, 2);
        Grid.SetColumn(policyCombo, 1);
        grid.Children.Add(policyCombo);

        // Endpoint / Model
        var endpointLbl = new TextBlock { Margin = new Thickness(0, 10, 8, 2), Foreground = muted, Text = "LM Studio endpoint", FontSize = 11 };
        Grid.SetRow(endpointLbl, 3);
        Grid.SetColumn(endpointLbl, 0);
        grid.Children.Add(endpointLbl);

        var endpointTb = new TextBox { Height = 28, Margin = new Thickness(0, 0, 8, 0) };
        endpointTb.Background = inputBg;
        endpointTb.Foreground = text;
        endpointTb.BorderBrush = border;
        endpointTb.SetBinding(TextBox.TextProperty, new Binding("LocalEndpoint") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Grid.SetRow(endpointTb, 4);
        Grid.SetColumn(endpointTb, 0);
        grid.Children.Add(endpointTb);

        var modelLbl = new TextBlock { Margin = new Thickness(8, 10, 0, 2), Foreground = muted, Text = "Modèle local", FontSize = 11 };
        Grid.SetRow(modelLbl, 3);
        Grid.SetColumn(modelLbl, 1);
        grid.Children.Add(modelLbl);

        var modelTb = new TextBox { Height = 28, Margin = new Thickness(8, 0, 0, 0) };
        modelTb.Background = inputBg;
        modelTb.Foreground = text;
        modelTb.BorderBrush = border;
        modelTb.SetBinding(TextBox.TextProperty, new Binding("LocalModel") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        Grid.SetRow(modelTb, 4);
        Grid.SetColumn(modelTb, 1);
        grid.Children.Add(modelTb);

        var logsCb = new CheckBox { Margin = new Thickness(0, 10, 0, 0), Foreground = text, Content = "Activer les logs IA (réponses brutes)" };
        logsCb.SetBinding(ToggleButton.IsCheckedProperty, new Binding("EnableAiLogs") { Mode = BindingMode.TwoWay });
        Grid.SetRow(logsCb, 5);
        Grid.SetColumnSpan(logsCb, 2);
        grid.Children.Add(logsCb);

        var webCb = new CheckBox { Margin = new Thickness(0, 8, 0, 0), Foreground = text, Content = "Activer la recherche web (DuckDuckGo)" };
        webCb.SetBinding(ToggleButton.IsCheckedProperty, new Binding("EnableWebSearch") { Mode = BindingMode.TwoWay });
        Grid.SetRow(webCb, 6);
        Grid.SetColumnSpan(webCb, 2);
        grid.Children.Add(webCb);

        var saveBtn = new Button
        {
            Width = 120,
            Height = 28,
            Background = accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Content = "Sauvegarder",
            HorizontalAlignment = HorizontalAlignment.Right
        };
        saveBtn.SetBinding(Button.CommandProperty, new Binding("SaveSettingsCommand"));
        Grid.SetRow(saveBtn, 7);
        Grid.SetColumnSpan(saveBtn, 2);
        grid.Children.Add(saveBtn);

        outer.Child = grid;
        return outer;
    }

    private void PromptBoxOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var isEnter = e.Key == Key.Enter || e.Key == Key.Return;
        if (!isEnter) return;

        var hasShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (hasShift)
        {
            var tb = (TextBox)sender;
            var i = tb.CaretIndex;
            tb.Text = (tb.Text ?? "").Insert(i, "\n");
            tb.CaretIndex = i + 1;
            e.Handled = true;
            return;
        }

        if (DataContext is AiPaletteViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= VmOnPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += VmOnPropertyChanged;

        RefreshTranscript();
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiPaletteViewModel.Transcript))
            RefreshTranscript();
    }

    private void RefreshTranscript()
    {
        if (DataContext is not AiPaletteViewModel vm)
            return;

        FlowDocument doc = ChatDocumentBuilder.Build(vm.Transcript);
        _transcriptBox.Document = doc;
        _transcriptBox.ScrollToEnd();
    }
}

