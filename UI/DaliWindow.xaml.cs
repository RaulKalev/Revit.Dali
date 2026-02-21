using Autodesk.Revit.UI;
using Dali.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Dali.UI
{
    // Keeping converters here as they are local to UI namespace and utilized by XAML
    
    public partial class DaliWindow : Window
    {
        #region Constants / PInvoke

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        #endregion

        #region Fields

        private readonly WindowResizer _windowResizer;
        private bool _isDarkMode = true;
        private bool _isDataLoaded;
        private UIApplication _uiApplication;
        
        private readonly Services.Revit.RevitExternalEventService _externalEventService;
        private readonly Services.SettingsService _settingsService;
        private readonly Services.ParameterResolver _parameterResolver;

        public ViewModels.SettingsViewModel SettingsVM { get; private set; }
        public ViewModels.BatchSetupViewModel BatchSetupVM { get; private set; }
        public ViewModels.GroupingViewModel GroupingVM { get; private set; }
        public ViewModels.DeviceManagerViewModel DeviceManagerVM { get; private set; }

        #endregion

        public DaliWindow(UIApplication app, Services.Revit.RevitExternalEventService externalEventService, Services.SettingsService settingsService, Services.ParameterResolver parameterResolver)
        {
            _externalEventService = externalEventService;
            _settingsService = settingsService;
            _parameterResolver = parameterResolver;
            _uiApplication = app;
            
            InitializeComponent();
            
            SettingsVM = new ViewModels.SettingsViewModel(_settingsService, _parameterResolver, _externalEventService);
            SettingsViewControl.DataContext = SettingsVM;

            // Wire up Batch Setup ViewModel
            BatchSetupVM = new ViewModels.BatchSetupViewModel(_settingsService, _externalEventService);
            BatchSetupViewControl.DataContext = BatchSetupVM;

            // Wire up Grouping ViewModel
            GroupingVM = new ViewModels.GroupingViewModel(_settingsService, _externalEventService);
            GroupingViewControl.DataContext = GroupingVM;

            // Wire up Device Manager ViewModel
            DeviceManagerVM = new ViewModels.DeviceManagerViewModel(new Services.Core.DeviceDatabaseService(new Services.Core.SessionLogger()));
            DeviceManagerViewControl.DataContext = DeviceManagerVM;

            DataContext = this;

            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            DeferWindowShow();

            // Window infrastructure
            _windowResizer = new WindowResizer(this);
            Closed += MainWindow_Closed;

            MouseLeftButtonUp += Window_MouseLeftButtonUp;

            // Theme
            LoadThemeState();
            LoadWindowState();
            
            // Mark data as loaded to allow window to show
            _isDataLoaded = true;
            TryShowWindow();

            // Check for updates after window loads and initial Revit scans finish
            Loaded += async (s, e) => 
            {
                try
                {
                    // Wait for the window to settle
                    await System.Threading.Tasks.Task.Delay(2000);
                    
                    // Wait until the GroupingViewModel has finished its initial Revit scan
                    while (GroupingVM != null && GroupingVM.IsBusy)
                    {
                        await System.Threading.Tasks.Task.Delay(500);
                    }

                    // Safe to call directly — we're on the UI thread and Revit's
                    // ExternalEvent has finished by now.
                    Services.UpdateLogService.CheckAndShow(this);
                }
                catch { /* Silently ignore update check failures */ }
            };
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            SaveWindowState();
            
            // Clear any active DALI highlight filters
            if (GroupingVM?.ResetOverridesCommand?.CanExecute(null) == true)
            {
                GroupingVM.ResetOverridesCommand.Execute(null);
            }
        }

        #region Actions

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = System.Windows.Visibility.Visible;
        }

        private void SettingsBackground_Click(object sender, MouseButtonEventArgs e)
        {
            SettingsOverlay.Visibility = System.Windows.Visibility.Collapsed;
        }

        private void SettingsClose_Click(object sender, RoutedEventArgs e)
        {
            SettingsOverlay.Visibility = System.Windows.Visibility.Collapsed;
        }

        #endregion

        #region Window chrome / resize handlers

        private void TitleBar_Loaded(object sender, RoutedEventArgs e)
        {
        }

        private void LeftEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeWE;
        private void RightEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeWE;
        private void BottomEdge_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNS;
        private void Edge_MouseLeave(object sender, MouseEventArgs e) => Cursor = Cursors.Arrow;
        private void BottomLeftCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNESW;
        private void BottomRightCorner_MouseEnter(object sender, MouseEventArgs e) => Cursor = Cursors.SizeNWSE;

        private void Window_MouseMove(object sender, MouseEventArgs e) => _windowResizer.ResizeWindow(e);
        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _windowResizer.StopResizing();
        private void LeftEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Left);
        private void RightEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Right);
        private void BottomEdge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.Bottom);
        private void BottomLeftCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomLeft);
        private void BottomRightCorner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _windowResizer.StartResizing(e, ResizeDirection.BottomRight);

        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
        }
        
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
        }

        #endregion

        #region Window Startup

        private void DeferWindowShow()
        {
            Opacity = 0;
            Loaded += DaliWindow_Loaded;
        }

        private void DaliWindow_Loaded(object sender, RoutedEventArgs e)
        {
            TryShowWindow();
        }

        private void TryShowWindow()
        {
            if (!_isDataLoaded)
            {
                return;
            }

            // Window is now created outside of Revit's suspended Dispatcher context,
            // so direct property assignment is safe.
            Opacity = 1;
        }

        #endregion

        #region Theme

        private ResourceDictionary _currentThemeDictionary;

        private void LoadTheme()
        {
            try
            {
                var themeUri = new Uri(_isDarkMode 
                    ? "pack://application:,,,/Dali;component/UI/Themes/DarkTheme.xaml" 
                    : "pack://application:,,,/Dali;component/UI/Themes/LightTheme.xaml", UriKind.Absolute);
                
                var newDict = new ResourceDictionary { Source = themeUri };
                
                if (_currentThemeDictionary != null)
                {
                    Resources.MergedDictionaries.Remove(_currentThemeDictionary);
                }
                
                Resources.MergedDictionaries.Add(newDict);
                _currentThemeDictionary = newDict;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading theme: {ex.Message}");
            }
        }

        private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        {
            _isDarkMode = ThemeToggleButton.IsChecked == true;
            LoadTheme();
            SaveThemeState();

            var icon = ThemeToggleButton?.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                       as MaterialDesignThemes.Wpf.PackIcon;
            if (icon != null)
            {
                icon.Kind = _isDarkMode
                    ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                    : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
            }
        }

        private void LoadThemeState()
        {
            try
            {
                var settings = _settingsService.Load();
                _isDarkMode = settings.IsDarkMode;
            }
            catch (Exception)
            {
            }

            if (ThemeToggleButton != null)
            {
                ThemeToggleButton.IsChecked = _isDarkMode;
                var icon = ThemeToggleButton.Template?.FindName("ThemeToggleIcon", ThemeToggleButton)
                           as MaterialDesignThemes.Wpf.PackIcon;
                if (icon != null)
                {
                    icon.Kind = _isDarkMode
                        ? MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOffOutline
                        : MaterialDesignThemes.Wpf.PackIconKind.ToggleSwitchOutline;
                }
            }
            
            LoadTheme();
        }

        private void SaveThemeState()
        {
            try
            {
                var settings = _settingsService.Load();
                settings.IsDarkMode = _isDarkMode;
                _settingsService.Save(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Window State

        private void LoadWindowState()
        {
            try
            {
                var settings = _settingsService.Load();
                double left = settings.WindowLeft;
                double top = settings.WindowTop;
                double width = settings.WindowWidth;
                double height = settings.WindowHeight;

                bool hasSize = !double.IsNaN(width) && !double.IsNaN(height) && width > 0 && height > 0;
                bool hasPos = !double.IsNaN(left) && !double.IsNaN(top);

                if (!hasSize && !hasPos)
                {
                    return;
                }

                WindowStartupLocation = WindowStartupLocation.Manual;

                if (hasSize)
                {
                    Width = Math.Max(MinWidth, width);
                    Height = Math.Max(MinHeight, height);
                }

                if (hasPos)
                {
                    Left = left;
                    Top = top;
                }
            }
            catch (Exception)
            {
            }
        }

        private void SaveWindowState()
        {
            try
            {
                var settings = _settingsService.Load();
                var bounds = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;

                settings.WindowLeft = bounds.Left;
                settings.WindowTop = bounds.Top;
                settings.WindowWidth = bounds.Width;
                settings.WindowHeight = bounds.Height;

                _settingsService.Save(settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save window state: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion
    }
}
