using System.Linq;
using System.Threading;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Security;
using XB1ControllerBatteryIndicator.ShellHelpers;
using XB1ControllerBatteryIndicator.Localization;
using XB1ControllerBatteryIndicator.Properties;
using Microsoft.Win32;

namespace XB1ControllerBatteryIndicator
{
    public class SystemTrayViewModel : Caliburn.Micro.Screen
    {
        private string _activeIcon;
        private string _tooltipText;
        private System.Windows.Visibility _trayIconVisibility = System.Windows.Visibility.Visible;
        private const string APP_ID = "NiyaShy.XB1ControllerBatteryIndicator";
        private const int DisplayIntervalMs = 5000;
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan FastCheckInterval = TimeSpan.FromSeconds(5);
        private readonly AutoResetEvent _checkNow = new AutoResetEvent(false);
        private readonly Controller[] _controllers =
        {
            new Controller(UserIndex.One), new Controller(UserIndex.Two), new Controller(UserIndex.Three),
            new Controller(UserIndex.Four)
        };
        //result of the last check, same order as _controllers, null if not connected
        private readonly BatteryInformation?[] _batteryInfo = new BatteryInformation?[4];
        private bool[] toast_shown = new bool[5];
        private bool[] lowBatterySoundPlayed = new bool[5];
        private Dictionary<string, int> numdict = new Dictionary<string, int>();
        private const string ThemeRegKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ThemeRegValueName = "SystemUsesLightTheme";
        private const int ThemeModeAuto = 0;
        private const int ThemeModeLight = 1;
        private const int ThemeModeDark = 2;

        public SystemTrayViewModel()
        {
            AppLogger.Info($"Application started, version {typeof(SystemTrayViewModel).Assembly.GetName().Version}.");
            GetAvailableLanguages();
            TranslationManager.CurrentLanguageChangedEvent += (sender, args) => GetAvailableLanguages();

            ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
            numdict["One"] = 1;
            numdict["Two"] = 2;
            numdict["Three"] = 3;
            numdict["Four"] = 4;
            TryCreateShortcut();
            Thread th = new Thread(RefreshControllerState);
            th.IsBackground = true;
            th.Start();
        }

        public string ActiveIcon
        {
            get { return _activeIcon; }
            set { Set(ref _activeIcon, value); }
        }

        public string TooltipText
        {
            get { return _tooltipText; }
            set { Set(ref _tooltipText, value); }
        }

        public System.Windows.Visibility TrayIconVisibility
        {
            get { return _trayIconVisibility; }
            set { Set(ref _trayIconVisibility, value); }
        }

        public ObservableCollection<CultureInfo> AvailableLanguages { get; } = new ObservableCollection<CultureInfo>();

        //wakes up every few seconds to rotate the displayed controller, checks the controllers only when due
        private void RefreshControllerState()
        {
            var nextCheck = DateTime.MinValue;
            var displayIndex = -1;

            while (true)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    if (now >= nextCheck)
                    {
                        nextCheck = now + (Settings.Default.FastPolling ? FastCheckInterval : CheckInterval);
                        CheckControllers();
                    }
                    displayIndex = NextConnectedIndex(displayIndex);
                    ShowController(displayIndex);
                    PlayLoopedWarningSound();
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Controller check failed.", ex);
                }
                //always wait, also after an error, so a persistent failure cannot spin the loop
                if (_checkNow.WaitOne(DisplayIntervalMs))
                {
                    nextCheck = DateTime.MinValue;
                }
            }
        }

        //request a check right away instead of waiting for the next interval
        public void CheckNow()
        {
            _checkNow.Set();
        }

        //the setting stores the highest BatteryLevel that triggers the warning
        public bool IsWarningThresholdEmpty => Settings.Default.LowBatteryWarningThreshold == (int)BatteryLevel.Empty;
        public bool IsWarningThresholdLow => Settings.Default.LowBatteryWarningThreshold == (int)BatteryLevel.Low;

        public void SetWarningThreshold(int batteryLevel)
        {
            Settings.Default.LowBatteryWarningThreshold = batteryLevel;
            Settings.Default.Save();
            NotifyOfPropertyChange(nameof(IsWarningThresholdEmpty));
            NotifyOfPropertyChange(nameof(IsWarningThresholdLow));
            CheckNow();
        }

        public bool IsThemeAuto => Settings.Default.ThemeMode == ThemeModeAuto;
        public bool IsThemeLight => Settings.Default.ThemeMode == ThemeModeLight;
        public bool IsThemeDark => Settings.Default.ThemeMode == ThemeModeDark;

        public void SetThemeMode(int themeMode)
        {
            Settings.Default.ThemeMode = themeMode;
            Settings.Default.Save();
            NotifyOfPropertyChange(nameof(IsThemeAuto));
            NotifyOfPropertyChange(nameof(IsThemeLight));
            NotifyOfPropertyChange(nameof(IsThemeDark));
            CheckNow();
        }

        //the menu checkbox already changed the setting through its binding
        public void FastPollingChanged()
        {
            Settings.Default.Save();
            CheckNow();
        }

        //a "Show controllers" checkbox already changed the setting through its binding
        public void ShownControllersChanged()
        {
            Settings.Default.Save();
            CheckNow();
        }

        //the menu checkbox already changed the setting through its binding
        public void HideWhenNoControllerChanged()
        {
            Settings.Default.Save();
            CheckNow();
        }

        private void CheckControllers()
        {
            for (int i = 0; i < _controllers.Length; i++)
            {
                var controller = _controllers[i];
                BatteryInformation? batteryInfo = null;
                if (controller.IsConnected)
                {
                    try
                    {
                        batteryInfo = controller.GetBatteryInformation(BatteryDeviceType.Gamepad);
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        //disconnected between the two calls
                        Debug.WriteLine(ex);
                    }
                }
                if (!Nullable.Equals(_batteryInfo[i], batteryInfo))
                {
                    AppLogger.Info($"Controller {controller.UserIndex}: " +
                        (batteryInfo == null ? "not connected" : $"{batteryInfo.Value.BatteryType}, {batteryInfo.Value.BatteryLevel}") + ".");
                }
                _batteryInfo[i] = batteryInfo;
                UpdateWarning(controller.UserIndex, batteryInfo);
            }
        }

        //show the toast and play the sound once when a controller reaches the warning level,
        //reset both when it is above it again, wired or disconnected
        private void UpdateWarning(UserIndex controllerIndex, BatteryInformation? batteryInfo)
        {
            int controllerId = numdict[$"{controllerIndex}"];
            if (!IsBatteryWarning(batteryInfo))
            {
                lowBatterySoundPlayed[controllerId] = false;
                if (toast_shown[controllerId])
                {
                    toast_shown[controllerId] = false;
                    ToastNotificationManager.History.Remove($"Controller{controllerIndex}", "ControllerToast", APP_ID);
                }
                return;
            }
            //check if toast (notification) for this controller was already triggered
            if (!toast_shown[controllerId])
            {
                toast_shown[controllerId] = true;
                AppLogger.Info($"Controller {controllerIndex}: low battery warning shown.");
                ShowToast(controllerIndex, batteryInfo.Value.BatteryLevel);
            }
            //with "repeat on loop" the sound is played by PlayLoopedWarningSound instead
            if (Settings.Default.LowBatteryWarningSound_Enabled && !Settings.Default.LowBatteryWarningSound_Loop_Enabled &&
                !lowBatterySoundPlayed[controllerId])
            {
                lowBatterySoundPlayed[controllerId] = true;
                PlayWarningSound();
            }
        }

        private static bool IsBatteryWarning(BatteryInformation? batteryInfo)
        {
            if (batteryInfo == null)
            {
                return false;
            }
            //wired controllers and controllers still waiting for data report no meaningful level
            var batteryType = batteryInfo.Value.BatteryType;
            bool onBattery = batteryType == BatteryType.Alkaline || batteryType == BatteryType.Nimh;
            return onBattery && (int)batteryInfo.Value.BatteryLevel <= Settings.Default.LowBatteryWarningThreshold;
        }

        private void PlayLoopedWarningSound()
        {
            if (Settings.Default.LowBatteryWarningSound_Enabled && Settings.Default.LowBatteryWarningSound_Loop_Enabled &&
                _batteryInfo.Any(IsBatteryWarning))
            {
                PlayWarningSound();
            }
        }

        //uses the sound chosen for "Exclamation" in the Windows sound scheme
        private void PlayWarningSound()
        {
            SystemSounds.Exclamation.Play();
        }

        //index of the next connected controller after current that is not hidden by the "Show controllers" menu,
        //-1 if there is none
        private int NextConnectedIndex(int current)
        {
            for (int offset = 1; offset <= _batteryInfo.Length; offset++)
            {
                int candidate = (current + offset) % _batteryInfo.Length;
                if (_batteryInfo[candidate] != null && IsShown(_batteryInfo[candidate].Value))
                {
                    return candidate;
                }
            }
            return -1;
        }

        private static bool IsShown(BatteryInformation batteryInfo)
        {
            switch (batteryInfo.BatteryType)
            {
                case BatteryType.Wired:
                    return Settings.Default.ShowWired;
                case BatteryType.Alkaline:
                case BatteryType.Nimh:
                    return Settings.Default.ShowWirelessKnownLevel;
                default:
                    return Settings.Default.ShowWirelessUnknownLevel;
            }
        }

        private void ShowController(int index)
        {
            if (index < 0)
            {
                TooltipText = Strings.ToolTip_NoController;
                ActiveIcon = $"Resources/battery_unknown{LightTheme()}.ico";
                TrayIconVisibility = Settings.Default.HideWhenNoController
                    ? System.Windows.Visibility.Collapsed
                    : System.Windows.Visibility.Visible;
                return;
            }
            TrayIconVisibility = System.Windows.Visibility.Visible;

            var controllerIndex = _controllers[index].UserIndex;
            var batteryInfo = _batteryInfo[index].Value;
            var controllerIndexCaption = GetControllerIndexCaption(controllerIndex);
            //wired
            if (batteryInfo.BatteryType == BatteryType.Wired)
            {
                TooltipText = string.Format(Strings.ToolTip_Wired, controllerIndexCaption);
                ActiveIcon = $"Resources/battery_wired_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
            //"disconnected", a controller that was detected but hasn't sent battery data yet has this state
            else if (batteryInfo.BatteryType == BatteryType.Disconnected)
            {
                TooltipText = string.Format(Strings.ToolTip_WaitingForData, controllerIndexCaption);
                ActiveIcon = $"Resources/battery_disconnected_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
            //this state should never happen
            else if (batteryInfo.BatteryType == BatteryType.Unknown)
            {
                TooltipText = string.Format(Strings.ToolTip_Unknown, controllerIndexCaption);
                ActiveIcon = $"Resources/battery_disconnected_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
            //a battery level was detected
            else
            {
                var batteryLevelCaption = GetBatteryLevelCaption(batteryInfo.BatteryLevel);
                TooltipText = string.Format(Strings.ToolTip_Wireless, controllerIndexCaption, batteryLevelCaption);
                ActiveIcon = $"Resources/battery_{batteryInfo.BatteryLevel.ToString().ToLower()}_{controllerIndex.ToString().ToLower() + LightTheme()}.ico";
            }
        }

        //try to create a start menu shortcut (required for sending toasts)
        private bool TryCreateShortcut()
        {
            String shortcutPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\Microsoft\\Windows\\Start Menu\\Programs\\XB1ControllerBatteryIndicator.lnk";
            if (!File.Exists(shortcutPath))
            {
                InstallShortcut(shortcutPath);
                return true;
            }
            return false;
        }
        //create the shortcut
        private void InstallShortcut(String shortcutPath)
        {
            // Find the path to the current executable 
            String exePath = Process.GetCurrentProcess().MainModule.FileName;
            IShellLinkW newShortcut = (IShellLinkW)new CShellLink();

            // Create a shortcut to the exe 
            ErrorHelper.VerifySucceeded(newShortcut.SetPath(exePath));
            ErrorHelper.VerifySucceeded(newShortcut.SetArguments(""));

            // Open the shortcut property store, set the AppUserModelId property 
            IPropertyStore newShortcutProperties = (IPropertyStore)newShortcut;

            using (PropVariant appId = new PropVariant(APP_ID))
            {
                ErrorHelper.VerifySucceeded(newShortcutProperties.SetValue(PropertyKeys.AppUserModelId, appId));
                ErrorHelper.VerifySucceeded(newShortcutProperties.Commit());
            }

            // Commit the shortcut to disk 
            IPersistFile newShortcutSave = (IPersistFile)newShortcut;

            ErrorHelper.VerifySucceeded(newShortcutSave.Save(shortcutPath, true));
        }
        //send a toast
        private void ShowToast(UserIndex controllerIndex, BatteryLevel batteryLevel)
        {
            int controllerId = numdict[$"{controllerIndex}"];
            var controllerIndexCaption = GetControllerIndexCaption(controllerIndex);
            var toastText = batteryLevel == BatteryLevel.Empty ? Strings.Toast_Text : Strings.Toast_TextLow;
            string argsDismiss = $"dismissed";
            string argsLaunch = $"{controllerId}";
            //how the content gets arranged, translated texts are escaped so a "&", "<" or "'" cannot break the XML
            string toastVisual =
                $@"<visual>
                        <binding template='ToastGeneric'>
                            <text>{SecurityElement.Escape(string.Format(Strings.Toast_Title, controllerIndexCaption))}</text>
                            <text>{SecurityElement.Escape(string.Format(toastText, controllerIndexCaption))}</text>
                            <text>{SecurityElement.Escape(Strings.Toast_Text2)}</text>
                        </binding>
                    </visual>";
            //Button on the toast
            string toastActions =
                $@"<actions>
                        <action content='{SecurityElement.Escape(Strings.Toast_Dismiss)}' arguments='{argsDismiss}'/>
                   </actions>";
            //combine content and button
            string toastXmlString =
                $@"<toast scenario='reminder' launch='{argsLaunch}'>
                        {toastVisual}
                        {toastActions}
                   </toast>";

            XmlDocument toastXml = new XmlDocument();
            toastXml.LoadXml(toastXmlString);
            //create the toast
            var toast = new ToastNotification(toastXml);
            toast.Activated += ToastActivated;
            toast.Dismissed += ToastDismissed;
            toast.Tag = $"Controller{controllerIndex}";
            toast.Group = "ControllerToast";
            //..and send it
            ToastNotificationManager.CreateToastNotifier(APP_ID).Show(toast);

        }
        //react to click on toast or button
        private void ToastActivated(ToastNotification sender, object e)
        {
            var toastArgs = e as ToastActivatedEventArgs;
            int controllerId = 0;
            //if the return value contains a controller ID
            if (Int32.TryParse(toastArgs.Arguments, out controllerId))
            {
                //reset the toast warning (it will trigger again if battery level is still empty)
                toast_shown[controllerId] = false;
            }
            //otherwise, do nothing
        }
        private void ToastDismissed(ToastNotification sender, object e)
        {
            //do nothing
        }

        public void OpenLogFolder()
        {
            try
            {
                Directory.CreateDirectory(AppLogger.LogDirectory);
                Process.Start(new ProcessStartInfo(AppLogger.LogDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLogger.Error("Opening the log folder failed.", ex);
            }
        }

        public void ExitApplication()
        {
            AppLogger.Info("Application exiting.");
            System.Windows.Application.Current.Shutdown();
        }

        private string GetBatteryLevelCaption(BatteryLevel batteryLevel)
        {
            switch (batteryLevel)
            {
                case BatteryLevel.Empty:
                    return Strings.BatteryLevel_Empty;
                case BatteryLevel.Low:
                    return Strings.BatteryLevel_Low;
                case BatteryLevel.Medium:
                    return Strings.BatteryLevel_Medium;
                case BatteryLevel.Full:
                    return Strings.BatteryLevel_Full;
                default:
                    throw new ArgumentOutOfRangeException(nameof(batteryLevel), batteryLevel, null);
            }
        }

        private string GetControllerIndexCaption(UserIndex index)
        {
            switch (index)
            {
                case UserIndex.One:
                    return Strings.ControllerIndex_One;
                case UserIndex.Two:
                    return Strings.ControllerIndex_Two;
                case UserIndex.Three:
                    return Strings.ControllerIndex_Three;
                case UserIndex.Four:
                    return Strings.ControllerIndex_Four;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index), index, null);
            }
        }

        private void GetAvailableLanguages()
        {
            AvailableLanguages.Clear();
            foreach (var language in TranslationManager.AvailableLanguages)
            {
                AvailableLanguages.Add(language);
            }
        }

        //icon file suffix: "-black" icons for a light taskbar
        private string LightTheme()
        {
            if (Settings.Default.ThemeMode == ThemeModeLight)
            {
                return "-black";
            }
            if (Settings.Default.ThemeMode == ThemeModeDark)
            {
                return "";
            }
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ThemeRegKeyPath))
            {
                object registryValueObject = key?.GetValue(ThemeRegValueName);
                if (registryValueObject == null)
                {
                    return "";
                }

                int registryValue = (int)registryValueObject;

                return registryValue > 0 ? "-black" : "";
            }
        }
    }
}