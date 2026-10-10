// WPF wins all naming conflicts. WinForms is only used through fully-qualified
// names (e.g. System.Windows.Forms.Screen) so we don't accidentally instantiate
// a WinForms control where a WPF control was meant.
global using Application  = System.Windows.Application;
global using MessageBox   = System.Windows.MessageBox;
global using Clipboard    = System.Windows.Clipboard;
global using TextBox      = System.Windows.Controls.TextBox;
global using Button       = System.Windows.Controls.Button;
global using ComboBox     = System.Windows.Controls.ComboBox;
global using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
global using CheckBox     = System.Windows.Controls.CheckBox;
global using Label        = System.Windows.Controls.Label;
global using Control      = System.Windows.Controls.Control;
global using DragEventArgs = System.Windows.DragEventArgs;
global using DataFormats  = System.Windows.DataFormats;
global using DragDropEffects = System.Windows.DragDropEffects;
global using ContextMenu  = System.Windows.Controls.ContextMenu;
global using MenuItem     = System.Windows.Controls.MenuItem;
global using Separator    = System.Windows.Controls.Separator;
global using ListBox      = System.Windows.Controls.ListBox;
global using Orientation  = System.Windows.Controls.Orientation;
global using FontFamily   = System.Windows.Media.FontFamily;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using MouseWheelEventArgs  = System.Windows.Input.MouseWheelEventArgs;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using Point        = System.Windows.Point;
global using Rect         = System.Windows.Rect;
global using Size         = System.Windows.Size;
global using Brush        = System.Windows.Media.Brush;
global using Brushes      = System.Windows.Media.Brushes;
global using Color        = System.Windows.Media.Color;
global using Colors       = System.Windows.Media.Colors;
