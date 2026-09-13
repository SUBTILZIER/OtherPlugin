using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfUserControl = System.Windows.Controls.UserControl;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutomationStudioWpf.Controls;

public partial class NumericUpDown : WpfUserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(NumericUpDown), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(NumericUpDown), new PropertyMetadata(double.MinValue));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(NumericUpDown), new PropertyMetadata(double.MaxValue));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(nameof(Step), typeof(double), typeof(NumericUpDown), new PropertyMetadata(1d));
    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(nameof(Format), typeof(string), typeof(NumericUpDown), new PropertyMetadata("0.##"));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(NumericUpDown), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsValidProperty = DependencyProperty.Register(nameof(IsValid), typeof(bool), typeof(NumericUpDown), new PropertyMetadata(true));
    public static readonly DependencyProperty ErrorMessageProperty = DependencyProperty.Register(nameof(ErrorMessage), typeof(string), typeof(NumericUpDown), new PropertyMetadata(string.Empty));
    public double Value { get=>(double)GetValue(ValueProperty); set=>SetValue(ValueProperty,value); }
    public double Minimum { get=>(double)GetValue(MinimumProperty); set=>SetValue(MinimumProperty,value); }
    public double Maximum { get=>(double)GetValue(MaximumProperty); set=>SetValue(MaximumProperty,value); }
    public double Step { get=>(double)GetValue(StepProperty); set=>SetValue(StepProperty,value); }
    public string Format { get=>(string)GetValue(FormatProperty); set=>SetValue(FormatProperty,value); }
    public string Unit { get=>(string)GetValue(UnitProperty); set=>SetValue(UnitProperty,value); }
    public bool IsValid { get=>(bool)GetValue(IsValidProperty); private set=>SetValue(IsValidProperty, value); }
    public string ErrorMessage { get=>(string)GetValue(ErrorMessageProperty); private set=>SetValue(ErrorMessageProperty, value); }
    public NumericUpDown(){ InitializeComponent(); }
    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e){ if(d is NumericUpDown n && n.Input is not null) n.Input.Text=((double)e.NewValue).ToString(n.Format,CultureInfo.CurrentCulture); }
    internal static bool TryParseFinite(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
    private void Input_TextChanged(object s, TextChangedEventArgs e){ if(TryParseFinite(Input.Text, out var v)){ IsValid = v >= Minimum && v <= Maximum; ErrorMessage = IsValid ? string.Empty : $"范围 {Minimum:g}–{Maximum:g}"; if(IsValid) Value=v; } else { IsValid=false; ErrorMessage="请输入有效数字"; } }
    private void Input_LostFocus(object s,RoutedEventArgs e)=>Commit();
    private void Input_PreviewKeyDown(object s,WpfKeyEventArgs e){ if(e.Key==Key.Enter){Commit(); e.Handled=true;} else if(e.Key==Key.Escape){ Input.Text=Value.ToString(Format); e.Handled=true;} }
    private void Commit(){ if(TryParseFinite(Input.Text, out var v)){ Value=Math.Clamp(v,Minimum,Maximum); Input.Text=Value.ToString(Format,CultureInfo.CurrentCulture); IsValid=true; ErrorMessage=string.Empty; } else { IsValid=false; ErrorMessage="请输入有效数字"; } }
    private void Up_Click(object s,RoutedEventArgs e){ Value=Math.Clamp(Value+Step,Minimum,Maximum); }
    private void Down_Click(object s,RoutedEventArgs e){ Value=Math.Clamp(Value-Step,Minimum,Maximum); }
}
